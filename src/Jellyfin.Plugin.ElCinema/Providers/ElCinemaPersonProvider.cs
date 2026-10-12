using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ElCinema.ExternalId;
using Jellyfin.Plugin.ElCinema.Models;
using Jellyfin.Plugin.ElCinema.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ElCinema.Providers;

/// <summary>
/// Provider for retrieving actor/person metadata from elCinema.
/// </summary>
public class ElCinemaPersonProvider : IRemoteMetadataProvider<Person, PersonLookupInfo>, IRemoteSearchProvider<PersonLookupInfo>, IHasOrder
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ElCinemaPersonProvider> _logger;
    private readonly ElCinemaApiClient _apiClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="ElCinemaPersonProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Factory used to create HTTP clients.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    public ElCinemaPersonProvider(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    {
        _httpClientFactory = httpClientFactory;
        _logger = loggerFactory.CreateLogger<ElCinemaPersonProvider>();
        _apiClient = new ElCinemaApiClient(httpClientFactory, loggerFactory.CreateLogger<ElCinemaApiClient>());
    }

    /// <inheritdoc />
    public string Name => "elCinema";

    /// <inheritdoc />
    public int Order => 1;

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(PersonLookupInfo searchInfo, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(searchInfo?.Name))
        {
            return new List<RemoteSearchResult>();
        }

        var matches = await SearchAsync(searchInfo.Name, cancellationToken).ConfigureAwait(false);
        return matches.Select(person => new RemoteSearchResult
        {
            Name = person.Name,
            SearchProviderName = Name,
            ProviderIds = new Dictionary<string, string>
            {
                { ElCinemaPersonExternalId.ProviderIdKey, person.PersonId }
            }
        }).ToList();
    }

    /// <inheritdoc />
    public async Task<MetadataResult<Person>> GetMetadata(PersonLookupInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Person>
        {
            Item = new Person(),
            HasMetadata = false
        };

        if (info == null)
        {
            return result;
        }

        var personId = info.ProviderIds.TryGetValue(ElCinemaPersonExternalId.ProviderIdKey, out var id)
            ? id
            : null;

        if (string.IsNullOrWhiteSpace(personId))
        {
            var matches = await SearchAsync(info.Name, cancellationToken).ConfigureAwait(false);
            personId = matches.FirstOrDefault(match => string.Equals(match.Name, info.Name, System.StringComparison.OrdinalIgnoreCase))?.PersonId
                ?? matches.FirstOrDefault()?.PersonId;
        }

        if (string.IsNullOrWhiteSpace(personId))
        {
            return result;
        }

        var person = await GetPersonAsync(personId, cancellationToken).ConfigureAwait(false);
        if (person == null)
        {
            return result;
        }

        _logger.LogInformation(
            "elCinema person '{PersonName}' ({PersonId}) overview present: {HasOverview}; overview: {Overview}",
            person.Name,
            person.PersonId,
            !string.IsNullOrWhiteSpace(person.Biography),
            person.Biography);

        result.Item.Name = string.IsNullOrWhiteSpace(person.Name) ? info.Name : person.Name;
        result.Item.Overview = person.Biography;
        result.Item.SetProviderId(ElCinemaPersonExternalId.ProviderIdKey, person.PersonId);
        result.HasMetadata = true;
        return result;
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient("Default");
        var userAgent = Plugin.Instance?.Configuration.UserAgent ?? "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(userAgent);
        request.Headers.Referrer = new System.Uri("https://elcinema.com/");
        return httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }


    /// <summary>
    /// Searches elCinema for people matching the provided query.
    /// </summary>
    public async Task<IEnumerable<ElCinemaPerson>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new List<ElCinemaPerson>();
        }

        _logger.LogInformation("Searching elCinema for person: '{Query}'", query);
        return await _apiClient.SearchPersonsAsync(query, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the full metadata available for a specific person ID.
    /// </summary>
    public async Task<ElCinemaPerson?> GetPersonAsync(string personId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(personId))
        {
            return null;
        }

        return await _apiClient.GetPersonDetailsAsync(personId, cancellationToken).ConfigureAwait(false);
    }
}