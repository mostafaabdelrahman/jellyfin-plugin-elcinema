using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.ElCinema.ExternalId;
using Jellyfin.Plugin.ElCinema.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ElCinema.Providers;

/// <summary>
/// elCinema remote movie metadata provider.
/// </summary>
public class ElCinemaMovieProvider : IRemoteMetadataProvider<Movie, MovieInfo>, IHasOrder
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ElCinemaMovieProvider> _logger;
    private readonly ElCinemaApiClient _apiClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="ElCinemaMovieProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Instance of <see cref="IHttpClientFactory"/>.</param>
    /// <param name="logger">Instance of <see cref="ILogger{ElCinemaMovieProvider}"/>.</param>
    /// <param name="loggerFactory">Instance of <see cref="ILoggerFactory"/>.</param>
    public ElCinemaMovieProvider(
        IHttpClientFactory httpClientFactory,
        ILogger<ElCinemaMovieProvider> logger,
        ILoggerFactory loggerFactory)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _apiClient = new ElCinemaApiClient(_httpClientFactory, loggerFactory.CreateLogger<ElCinemaApiClient>());
    }

    /// <inheritdoc />
    public string Name => "elCinema";

    /// <inheritdoc />
    public int Order => 1;

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo searchInfo, CancellationToken cancellationToken)
    {
        var list = new List<RemoteSearchResult>();
        if (searchInfo == null)
        {
            return list;
        }

        var query = searchInfo.Name;
        if (string.IsNullOrWhiteSpace(query))
        {
            return list;
        }

        _logger.LogInformation("Searching elCinema for movie: '{Query}' ({Year})", query, searchInfo.Year);

        var searchResults = await _apiClient.SearchAsync(query, cancellationToken).ConfigureAwait(false);

        foreach (var sr in searchResults)
        {
            var result = new RemoteSearchResult
            {
                Name = sr.Title,
                SearchProviderName = Name,
                ProviderIds = new Dictionary<string, string>
                {
                    { ElCinemaExternalId.ProviderIdKey, sr.WorkId }
                },
                ProductionYear = sr.Year ?? searchInfo.Year
            };

            if (!string.IsNullOrEmpty(sr.PosterUrl))
            {
                result.ImageUrl = sr.PosterUrl;
            }

            list.Add(result);
        }

        return list;
    }

    /// <inheritdoc />
    public async Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Movie>
        {
            Item = new Movie(),
            HasMetadata = false
        };

        if (info == null)
        {
            _logger.LogWarning("elCinema GetMetadata was called without movie lookup information");
            return result;
        }

        string? workId = info.GetProviderId(ElCinemaExternalId.ProviderIdKey);
        _logger.LogInformation(
            "elCinema GetMetadata called for '{Name}' ({Year}); supplied work ID: {WorkId}",
            info.Name,
            info.Year,
            workId ?? "<none>");

        if (string.IsNullOrEmpty(workId))
        {
            // Fallback search to find best match
            var searchResults = await _apiClient.SearchAsync(info.Name, cancellationToken).ConfigureAwait(false);
            var match = searchResults.FirstOrDefault(s =>
                !info.Year.HasValue || !s.Year.HasValue || s.Year.Value == info.Year.Value) ?? searchResults.FirstOrDefault();

            if (match != null)
            {
                workId = match.WorkId;
            }
        }

        if (string.IsNullOrEmpty(workId))
        {
            _logger.LogWarning("No elCinema work ID found for movie '{Name}' ({Year})", info.Name, info.Year);
            return result;
        }

        _logger.LogInformation("Fetching elCinema movie details for '{Name}' using work ID {WorkId}", info.Name, workId);
        var movieDetails = await _apiClient.GetMovieDetailsAsync(workId, cancellationToken).ConfigureAwait(false);
        if (movieDetails == null)
        {
            _logger.LogWarning("elCinema returned no movie details for '{Name}' (work ID {WorkId})", info.Name, workId);
            return result;
        }

        _logger.LogInformation(
            "Fetched elCinema movie '{Title}' (work ID {WorkId}): {CastCount} cast entries, {PortraitCount} portrait URLs",
            movieDetails.Title,
            workId,
            movieDetails.Cast.Count,
            movieDetails.PersonImageUrls.Count);

        var config = Plugin.Instance?.Configuration;
        bool preferArabic = config?.PreferArabicTitle ?? true;

        if (preferArabic && !string.IsNullOrEmpty(movieDetails.Title))
        {
            result.Item.Name = movieDetails.Title;
            if (!string.IsNullOrEmpty(movieDetails.TransliteratedTitle))
            {
                result.Item.OriginalTitle = movieDetails.TransliteratedTitle;
                result.Item.SortName = movieDetails.TransliteratedTitle;
            }
        }
        else if (!preferArabic && !string.IsNullOrEmpty(movieDetails.TransliteratedTitle))
        {
            result.Item.Name = movieDetails.TransliteratedTitle;
            result.Item.OriginalTitle = movieDetails.Title;
            result.Item.SortName = movieDetails.TransliteratedTitle;
        }
        else
        {
            result.Item.Name = !string.IsNullOrEmpty(movieDetails.Title) ? movieDetails.Title : movieDetails.TransliteratedTitle;
        }

        result.Item.Overview = movieDetails.Plot;
        result.Item.ProductionYear = movieDetails.Year;
        
        if (movieDetails.Rating.HasValue)
        {
            result.Item.CommunityRating = (float)movieDetails.Rating.Value;
        }

        if (!string.IsNullOrEmpty(movieDetails.StreamUrl))
        {
            // Store stream / trailer link
            result.Item.RemoteTrailers = new[] { new MediaUrl { Url = movieDetails.StreamUrl } };
        }

        result.Item.SetProviderId(ElCinemaExternalId.ProviderIdKey, workId);

        // Genres
        foreach (var genre in movieDetails.Genres)
        {
            result.Item.AddGenre(genre);
        }

        // People: Directors
        if (config?.ImportDirectors ?? true)
        {
            foreach (var director in movieDetails.Directors)
            {
                result.AddPerson(await CreatePersonInfoAsync(movieDetails, director, PersonKind.Director, cancellationToken).ConfigureAwait(false));
            }
        }

        // People: Writers
        if (config?.ImportWriters ?? true)
        {
            foreach (var writer in movieDetails.Writers)
            {
                result.AddPerson(await CreatePersonInfoAsync(movieDetails, writer, PersonKind.Writer, cancellationToken).ConfigureAwait(false));
            }
        }

        // People: Actors
        if (config?.ImportActors ?? true)
        {
            foreach (var actor in movieDetails.Cast)
            {
                result.AddPerson(await CreatePersonInfoAsync(movieDetails, actor, PersonKind.Actor, cancellationToken).ConfigureAwait(false));
            }
        }

        result.HasMetadata = true;
        return result;
    }

    private async Task<PersonInfo> CreatePersonInfoAsync(
        Models.ElCinemaMovie movieDetails,
        string name,
        PersonKind personKind,
        CancellationToken cancellationToken)
    {
        var personInfo = new PersonInfo
        {
            Name = name,
            Type = personKind,
            ProviderIds = new Dictionary<string, string>()
        };

        if (!movieDetails.PersonIds.TryGetValue(name, out var personId))
        {
            _logger.LogDebug("No elCinema person ID found for {PersonKind} '{Name}'", personKind, name);
            return personInfo;
        }

        personInfo.ProviderIds[ElCinemaPersonExternalId.ProviderIdKey] = personId;
        movieDetails.PersonImageUrls.TryGetValue(personId, out var imageUrl);
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            var personDetails = await _apiClient.GetPersonDetailsAsync(personId, cancellationToken).ConfigureAwait(false);
            imageUrl = personDetails?.ImageUrl;
        }

        if (!string.IsNullOrWhiteSpace(imageUrl))
        {
            personInfo.ImageUrl = imageUrl;
        }

        _logger.LogInformation(
            "elCinema movie credit for {PersonKind} {Name}: person ID {PersonId}, primary image URL present: {HasImageUrl}",
            personKind,
            name,
            personId,
            !string.IsNullOrWhiteSpace(personInfo.ImageUrl));

        return personInfo;
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
