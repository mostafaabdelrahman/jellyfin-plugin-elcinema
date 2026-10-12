using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ElCinema.ExternalId;
using Jellyfin.Plugin.ElCinema.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ElCinema.Providers;

/// <summary>
    /// elCinema remote image provider for movie posters and actor profile images.
/// </summary>
public class ElCinemaImageProvider : IRemoteImageProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ElCinemaImageProvider> _logger;
    private readonly ElCinemaApiClient _apiClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="ElCinemaImageProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Instance of <see cref="IHttpClientFactory"/>.</param>
    /// <param name="logger">Instance of <see cref="ILogger{ElCinemaImageProvider}"/>.</param>
    /// <param name="loggerFactory">Instance of <see cref="ILoggerFactory"/>.</param>
    public ElCinemaImageProvider(
        IHttpClientFactory httpClientFactory,
        ILogger<ElCinemaImageProvider> logger,
        ILoggerFactory loggerFactory)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _apiClient = new ElCinemaApiClient(_httpClientFactory, loggerFactory.CreateLogger<ElCinemaApiClient>());
    }

    /// <inheritdoc />
    public string Name => "elCinema";

    /// <inheritdoc />
    public bool Supports(BaseItem item) => item is Movie or Person;

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        return new[]
        {
            ImageType.Primary
        };
    }

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        var list = new List<RemoteImageInfo>();

        if (item is Person person)
        {
            var personId = person.GetProviderId(ElCinemaPersonExternalId.ProviderIdKey);
            if (string.IsNullOrWhiteSpace(personId))
            {
                var personResults = await _apiClient.SearchPersonsAsync(person.Name, cancellationToken).ConfigureAwait(false);
                personId = personResults.FirstOrDefault(result => string.Equals(result.Name, person.Name, StringComparison.OrdinalIgnoreCase))?.PersonId
                    ?? personResults.FirstOrDefault()?.PersonId;
            }

            if (!string.IsNullOrWhiteSpace(personId))
            {
                var personDetails = await _apiClient.GetPersonDetailsAsync(personId, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(personDetails?.ImageUrl))
                {
                    list.Add(new RemoteImageInfo
                    {
                        Url = personDetails.ImageUrl,
                        Type = ImageType.Primary,
                        ProviderName = Name
                    });
                }
            }

            return list;
        }

        if (item is not Movie movie)
        {
            return list;
        }

        var workId = item.GetProviderId(ElCinemaExternalId.ProviderIdKey);

        if (string.IsNullOrEmpty(workId) && !string.IsNullOrEmpty(movie.Name))
        {
            var searchResults = await _apiClient.SearchAsync(movie.Name, cancellationToken).ConfigureAwait(false);
            var match = searchResults.FirstOrDefault();
            if (match != null)
            {
                workId = match.WorkId;
            }
        }

        if (string.IsNullOrEmpty(workId))
        {
            return list;
        }

        var details = await _apiClient.GetMovieDetailsAsync(workId, cancellationToken).ConfigureAwait(false);
        if (details != null && !string.IsNullOrEmpty(details.PosterUrl))
        {
            list.Add(new RemoteImageInfo
            {
                Url = details.PosterUrl,
                Type = ImageType.Primary,
                ProviderName = Name
            });
        }

        return list;
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient("Default");
        var userAgent = Plugin.Instance?.Configuration.UserAgent ?? "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(userAgent);
        request.Headers.Referrer = new Uri("https://elcinema.com/");

        return httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
}
