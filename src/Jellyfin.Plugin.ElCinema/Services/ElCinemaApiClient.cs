using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using Jellyfin.Plugin.ElCinema.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ElCinema.Services;

/// <summary>
/// HTTP client and HTML parser service for elcinema.com.
/// </summary>
public class ElCinemaApiClient
{
    private static readonly TimeSpan MinimumRequestInterval = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan MovieCacheDuration = TimeSpan.FromHours(6);
    private static readonly TimeSpan PersonCacheDuration = TimeSpan.FromHours(24);
    private static readonly TimeSpan RateLimitedPersonCacheDuration = TimeSpan.FromMinutes(2);
    private static readonly SemaphoreSlim RequestGate = new(1, 1);
    private static readonly ConcurrentDictionary<string, CacheEntry<ElCinemaMovie>> MovieDetailsCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, CacheEntry<ElCinemaPerson>> PersonDetailsCache = new(StringComparer.Ordinal);
    private static DateTimeOffset _nextRequestAt = DateTimeOffset.MinValue;
    private static int _consecutiveRateLimits;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ElCinemaApiClient> _logger;
    private readonly IBrowsingContext _browsingContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="ElCinemaApiClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Instance of <see cref="IHttpClientFactory"/>.</param>
    /// <param name="logger">Instance of <see cref="ILogger{ElCinemaApiClient}"/>.</param>
    public ElCinemaApiClient(IHttpClientFactory httpClientFactory, ILogger<ElCinemaApiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        
        var config = AngleSharp.Configuration.Default.WithDefaultLoader();
        _browsingContext = BrowsingContext.New(config);
    }

    /// <summary>
    /// Searches elCinema for works matching the query.
    /// </summary>
    /// <param name="query">Search query string.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of search results.</returns>
    public async Task<IEnumerable<ElCinemaSearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var results = new List<ElCinemaSearchResult>();
        if (string.IsNullOrWhiteSpace(query))
        {
            return results;
        }

        var encodedQuery = Uri.EscapeDataString(query.Trim());
        var url = $"https://elcinema.com/search/?q={encodedQuery}";

        try
        {
            var httpClient = _httpClientFactory.CreateClient("Default");
            var userAgent = Plugin.Instance?.Configuration.UserAgent ?? "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
            
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(userAgent);
            request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9,ar;q=0.8");

            var response = await SendElCinemaRequestAsync(httpClient, request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("elCinema search HTTP GET {Url} returned status code {StatusCode}", url, response.StatusCode);
                return results;
            }

            var htmlContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var document = await _browsingContext.OpenAsync(req => req.Content(htmlContent), cancellationToken).ConfigureAwait(false);

            var seenWorkIds = new HashSet<string>();

            // Find all anchor tags matching /work/{id}
            var anchors = document.QuerySelectorAll("a[href]");
            foreach (var anchor in anchors)
            {
                var href = anchor.GetAttribute("href") ?? string.Empty;
                var match = Regex.Match(href, @"/work/(\d+)");
                if (match.Success)
                {
                    var workId = match.Groups[1].Value;
                    if (!seenWorkIds.Add(workId))
                    {
                        continue;
                    }

                    var resultTitle = query;
                    int? year = null;

                    // Try to locate parent list item or container for title/year
                    var listItem = anchor.Closest("li") ?? anchor.Closest(".media") ?? anchor.ParentElement;
                    if (listItem != null)
                    {
                        var aTagInList = listItem.QuerySelector("a[href*=\"/work/\"]");
                        if (aTagInList != null && !string.IsNullOrWhiteSpace(aTagInList.TextContent))
                        {
                            resultTitle = aTagInList.TextContent.Trim();
                        }

                        var listText = listItem.TextContent;
                        var yearMatch = Regex.Match(listText, @"\((\d{4})\)");
                        if (yearMatch.Success && int.TryParse(yearMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedYear))
                        {
                            year = parsedYear;
                        }
                    }

                    results.Add(new ElCinemaSearchResult
                    {
                        WorkId = workId,
                        Title = resultTitle,
                        Year = year
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching elCinema for query '{Query}'", query);
        }

        return results;
    }

    /// <summary>
    /// Retrieves detailed movie metadata for a given elCinema work ID.
    /// </summary>
    /// <param name="workId">elCinema work ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extracted movie metadata, or null if not found.</returns>
    public async Task<ElCinemaMovie?> GetMovieDetailsAsync(string workId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workId))
        {
            return null;
        }

        if (TryGetCached(MovieDetailsCache, workId, out var cachedMovie))
        {
            return cachedMovie;
        }

        var url = $"https://elcinema.com/work/{workId}/";

        try
        {
            var httpClient = _httpClientFactory.CreateClient("Default");
            var userAgent = Plugin.Instance?.Configuration.UserAgent ?? "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(userAgent);
            request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9,ar;q=0.8");

            var response = await SendElCinemaRequestAsync(httpClient, request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("elCinema work details HTTP GET {Url} returned status code {StatusCode}", url, response.StatusCode);
                return null;
            }

            var htmlContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var document = await _browsingContext.OpenAsync(req => req.Content(htmlContent), cancellationToken).ConfigureAwait(false);

            var metadata = new ElCinemaMovie
            {
                WorkId = workId
            };

            // 1. Main Title Extraction (Panel Jumbo Container)
            var panelH1 = document.QuerySelector("div.panel.jumbo h1");
            if (panelH1 != null)
            {
                var titleArSpan = panelH1.QuerySelector("span[dir=\"rtl\"]");
                if (titleArSpan != null)
                {
                    metadata.Title = titleArSpan.TextContent.Trim();
                }

                var titleEnSpan = panelH1.QuerySelector("span[dir=\"ltr\"]");
                if (titleEnSpan != null)
                {
                    metadata.TransliteratedTitle = titleEnSpan.TextContent.Trim();
                }

                if (string.IsNullOrEmpty(metadata.Title))
                {
                    metadata.Title = panelH1.FirstChild?.TextContent?.Trim() ?? string.Empty;
                }

                var yearMatch = Regex.Match(panelH1.TextContent, @"\((\d{4})\)");
                if (yearMatch.Success && int.TryParse(yearMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedYear))
                {
                    metadata.Year = parsedYear;
                }
            }

            // 2. User Community Star Rating Data
            var ratingLegend = document.QuerySelector(".stars-rating-lg .legend");
            if (ratingLegend != null)
            {
                var ratingMatch = Regex.Match(ratingLegend.TextContent, @"(\d+\.\d+|\d+)");
                if (ratingMatch.Success && float.TryParse(ratingMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedRating))
                {
                    metadata.Rating = parsedRating;
                }
            }

            // 3. Main Poster Image Resource
            metadata.PosterUrl = GetImageUrl(document.QuerySelector("div.intro-box img"), url)
                ?? document.QuerySelector("meta[property='og:image']")?.GetAttribute("content")
                ?? string.Empty;

            // 4. Interactive Streaming/Trailer URLs
            var watchNowBtn = document.QuerySelector("a.cta-watch_now");
            if (watchNowBtn != null)
            {
                var href = watchNowBtn.GetAttribute("href");
                if (!string.IsNullOrEmpty(href))
                {
                    metadata.StreamUrl = href;
                }
            }

            // 5. Genre Extraction
            var genreList = document.QuerySelector("ul#jump-here-genre")
                ?? document.QuerySelectorAll("ul.list-separator").FirstOrDefault(list =>
                    list.TextContent.Contains("تصنيف العمل:", StringComparison.Ordinal));
            if (genreList != null)
            {
                foreach (var aTag in genreList.QuerySelectorAll("a[href*='/genre/']"))
                {
                    var genreText = aTag.TextContent.Trim();
                    if (!string.IsNullOrEmpty(genreText) && !metadata.Genres.Contains(genreText))
                    {
                        metadata.Genres.Add(genreText);
                    }
                }
            }

            // 6. Full Plot Description Extraction
            var plotLabel = document.QuerySelectorAll("strong").FirstOrDefault(element =>
                element.TextContent.Contains("ملخص القصة:", StringComparison.Ordinal));
            var plotParagraph = plotLabel?.NextElementSibling;
            while (plotParagraph != null
                && (plotParagraph.TagName != "P" || string.IsNullOrWhiteSpace(plotParagraph.TextContent)))
            {
                if (plotParagraph.TagName == "A" && plotParagraph.ClassList.Contains("button"))
                {
                    plotParagraph = null;
                    break;
                }

                plotParagraph = plotParagraph.NextElementSibling;
            }

            if (plotParagraph != null)
            {
                // Clone or copy HTML inner text and remove read-more
                var clonedHtml = plotParagraph.InnerHtml;
                var subDoc = await _browsingContext.OpenAsync(req => req.Content($"<html><body><p>{clonedHtml}</p></body></html>"), cancellationToken).ConfigureAwait(false);
                var readMore = subDoc.QuerySelector("#read-more");
                readMore?.Remove();
                
                var plotText = subDoc.Body?.TextContent?.Trim();
                if (!string.IsNullOrEmpty(plotText))
                {
                    // Clean extra whitespace / newlines
                    metadata.Plot = Regex.Replace(plotText, @"\s+", " ");
                }
            }

            // 7. Directors, Writers, and Cast List Parsing
            foreach (var ul in document.QuerySelectorAll("ul.list-separator"))
            {
                var ulText = ul.TextContent;

                if (ulText.Contains("ﺇﺧﺮaﺝ:", StringComparison.Ordinal) || ulText.Contains("إخراج:", StringComparison.Ordinal) || ulText.Contains("ﺇﺧﺮاﺝ:", StringComparison.Ordinal))
                {
                    foreach (var aTag in ul.QuerySelectorAll("a[href*=\"/person/\"]"))
                    {
                        var name = aTag.TextContent.Trim();
                        if (!string.IsNullOrEmpty(name) && !metadata.Directors.Contains(name))
                        {
                            metadata.Directors.Add(name);
                        }

                        var personMatch = Regex.Match(aTag.GetAttribute("href") ?? string.Empty, @"/person/(\d+)");
                        if (!string.IsNullOrEmpty(name) && personMatch.Success)
                        {
                            metadata.PersonIds[name] = personMatch.Groups[1].Value;
                        }
                    }
                }
                else if (ulText.Contains("ﺗﺄﻟﻴﻒ:", StringComparison.Ordinal) || ulText.Contains("تأليف:", StringComparison.Ordinal))
                {
                    foreach (var aTag in ul.QuerySelectorAll("a[href*=\"/person/\"]"))
                    {
                        var name = aTag.TextContent.Trim();
                        if (!string.IsNullOrEmpty(name) && !metadata.Writers.Contains(name))
                        {
                            metadata.Writers.Add(name);
                        }

                        var personMatch = Regex.Match(aTag.GetAttribute("href") ?? string.Empty, @"/person/(\d+)");
                        if (!string.IsNullOrEmpty(name) && personMatch.Success)
                        {
                            metadata.PersonIds[name] = personMatch.Groups[1].Value;
                        }
                    }
                }
                else if (ulText.Contains("طاقم العمل:", StringComparison.Ordinal))
                {
                    foreach (var aTag in ul.QuerySelectorAll("a[href*=\"/person/\"]"))
                    {
                        var name = aTag.TextContent.Trim();
                        if (!string.IsNullOrEmpty(name) && !metadata.Cast.Contains(name))
                        {
                            metadata.Cast.Add(name);
                        }

                        var personMatch = Regex.Match(aTag.GetAttribute("href") ?? string.Empty, @"/person/(\d+)");
                        if (!string.IsNullOrEmpty(name) && personMatch.Success)
                        {
                            metadata.PersonIds[name] = personMatch.Groups[1].Value;
                        }
                    }
                }
            }

            var castHeading = document.QuerySelector(".section-title a[href*='/cast']");
            var castContainer = castHeading?.Closest(".boxed-0");
            IEnumerable<IElement> castThumbnails = castContainer == null
                ? document.QuerySelectorAll(".thumbnail-wrapper")
                : castContainer.QuerySelectorAll(".thumbnail-wrapper");

            foreach (var thumbnail in castThumbnails)
            {
                var personLink = thumbnail.QuerySelector("a[href*='/person/']");
                var personMatch = Regex.Match(personLink?.GetAttribute("href") ?? string.Empty, @"/person/(\d+)");
                if (!personMatch.Success)
                {
                    continue;
                }

                var personId = personMatch.Groups[1].Value;
                var nameLink = thumbnail.QuerySelector(".description a[href*='/person/']") ?? personLink;
                var name = nameLink?.TextContent.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (!metadata.Cast.Contains(name))
                {
                    metadata.Cast.Add(name);
                }

                metadata.PersonIds[name] = personId;
                var imageUrl = GetImageUrl(thumbnail.QuerySelector("img"), url);
                if (!string.IsNullOrWhiteSpace(imageUrl))
                {
                    metadata.PersonImageUrls[personId] = imageUrl;
                }
            }

            _logger.LogInformation(
                "Parsed elCinema credits for work {WorkId}: {PeopleCount} people, {ImageCount} portrait URLs",
                workId,
                metadata.Cast.Count + metadata.Directors.Count + metadata.Writers.Count,
                metadata.PersonImageUrls.Count);

            AddToCache(MovieDetailsCache, workId, metadata, MovieCacheDuration);

            return metadata;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching elCinema movie details for work ID '{WorkId}'", workId);
            return null;
        }
    }

    /// <summary>
    /// Searches elCinema for actors / persons matching the provided query.
    /// </summary>
    /// <param name="query">Search query value.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of discovered people.</returns>
    public async Task<IEnumerable<ElCinemaPerson>> SearchPersonsAsync(string query, CancellationToken cancellationToken)
    {
        var results = new List<ElCinemaPerson>();
        if (string.IsNullOrWhiteSpace(query))
        {
            return results;
        }

        var encodedQuery = Uri.EscapeDataString(query.Trim());
        var url = $"https://elcinema.com/search/?q={encodedQuery}";

        try
        {
            var httpClient = _httpClientFactory.CreateClient("Default");
            var userAgent = Plugin.Instance?.Configuration.UserAgent ?? "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(userAgent);
            request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9,ar;q=0.8");

            var response = await SendElCinemaRequestAsync(httpClient, request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("elCinema people search HTTP GET {Url} returned status code {StatusCode}", url, response.StatusCode);
                return results;
            }

            var htmlContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var document = await _browsingContext.OpenAsync(req => req.Content(htmlContent), cancellationToken).ConfigureAwait(false);

            var seenIds = new HashSet<string>();

            foreach (var anchor in document.QuerySelectorAll("a[href]"))
            {
                var href = anchor.GetAttribute("href") ?? string.Empty;
                var match = Regex.Match(href, @"/person/(\d+)");
                if (!match.Success)
                {
                    continue;
                }

                var personId = match.Groups[1].Value;
                if (!seenIds.Add(personId))
                {
                    continue;
                }

                var name = anchor.TextContent.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    var closest = anchor.Closest("li") ?? anchor.ParentElement;
                    name = closest?.TextContent?.Trim() ?? string.Empty;
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                results.Add(new ElCinemaPerson
                {
                    PersonId = personId,
                    Name = name,
                    DisplayName = name
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching elCinema for person '{Query}'", query);
        }

        return results;
    }

    /// <summary>
    /// Retrieves detailed person metadata for a given elCinema person ID.
    /// </summary>
    /// <param name="personId">elCinema person ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Person metadata or null when not found.</returns>
    public async Task<ElCinemaPerson?> GetPersonDetailsAsync(string personId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(personId))
        {
            return null;
        }

        if (TryGetCached(PersonDetailsCache, personId, out var cachedPerson))
        {
            return cachedPerson;
        }

        var url = $"https://elcinema.com/person/{personId}/";

        try
        {
            var httpClient = _httpClientFactory.CreateClient("Default");
            var userAgent = Plugin.Instance?.Configuration.UserAgent ?? "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(userAgent);
            request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9,ar;q=0.8");

            var response = await SendElCinemaRequestAsync(httpClient, request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("elCinema person details HTTP GET {Url} returned status code {StatusCode}", url, response.StatusCode);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    AddToCache(PersonDetailsCache, personId, null, RateLimitedPersonCacheDuration);
                }

                return null;
            }

            var htmlContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var document = await _browsingContext.OpenAsync(req => req.Content(htmlContent), cancellationToken).ConfigureAwait(false);

            var person = new ElCinemaPerson
            {
                PersonId = personId
            };

            var mainTitle = document.QuerySelector("h1") ?? document.QuerySelector("h2") ?? document.QuerySelector(".title");
            if (mainTitle != null)
            {
                var name = mainTitle.QuerySelectorAll("span.left")
                    .Select(span => Regex.Replace(span.TextContent, @"\s+", " ").Trim())
                    .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)
                        && !Regex.IsMatch(text, @"^\(?\s*\d{4}\s*[-\u2013\u2014]\s*\d{4}\s*\)?$"));
                name ??= Regex.Replace(mainTitle.TextContent, @"\(\s*\d{4}\s*[-\u2013\u2014]\s*\d{4}\s*\)", string.Empty);
                name = Regex.Replace(name, @"\s+", " ").Trim();
                var displayName = mainTitle.QuerySelector("span.right, span[dir='ltr']")?.TextContent;
                displayName = string.IsNullOrWhiteSpace(displayName)
                    ? name
                    : Regex.Replace(displayName, @"\s+", " ").Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    person.Name = name;
                    person.DisplayName = displayName;
                }
            }

            person.ImageUrl = GetImageUrl(document.QuerySelector("div.intro-box img"), url)
                ?? document.QuerySelector("meta[property='og:image']")?.GetAttribute("content")
                ?? string.Empty;

            var biographyHeading = document.QuerySelectorAll("h3")
                .FirstOrDefault(element => string.Equals(element.TextContent.Trim(), "السيرة الذاتية", StringComparison.Ordinal));
            var biographyParagraphs = new List<string>();
            for (var sibling = biographyHeading?.NextElementSibling; sibling != null && sibling.LocalName != "ul"; sibling = sibling.NextElementSibling)
            {
                if (!string.Equals(sibling.LocalName, "p", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                sibling.QuerySelector("#read-more")?.Remove();
                var paragraph = Regex.Replace(sibling.TextContent, @"\s+", " ").Trim();
                if (!string.IsNullOrWhiteSpace(paragraph))
                {
                    biographyParagraphs.Add(paragraph);
                }
            }

            person.Biography = string.Join(" ", biographyParagraphs);

            var birthPlaceRow = document.QuerySelectorAll("ul.list-title")
                .FirstOrDefault(row => string.Equals(row.Children.FirstOrDefault()?.TextContent.Trim(), "بلد الميلاد:", StringComparison.Ordinal));
            person.BirthPlace = birthPlaceRow?.Children.Skip(1).FirstOrDefault()?.TextContent.Trim() ?? string.Empty;

            var nationalityRow = document.QuerySelectorAll(".intro-box li").FirstOrDefault(element => element.TextContent.Contains("الموطن:", StringComparison.Ordinal) || element.TextContent.Contains("الجنسية:", StringComparison.Ordinal));
            if (nationalityRow != null)
            {
                person.Nationality = nationalityRow.QuerySelector("a")?.TextContent.Trim() ?? string.Empty;
            }

            var birthRow = document.QuerySelectorAll(".intro-box li").FirstOrDefault(element => element.TextContent.Contains("تاريخ الميلاد:", StringComparison.Ordinal));
            if (birthRow != null)
            {
                person.BirthDate = Regex.Replace(birthRow.TextContent.Replace("تاريخ الميلاد:", string.Empty, StringComparison.Ordinal), @"\s+", " ").Trim();
            }

            foreach (var anchor in document.QuerySelectorAll("a[href*='/work/']"))
            {
                var workTitle = anchor.TextContent.Trim();
                if (!string.IsNullOrWhiteSpace(workTitle) && !person.KnownFor.Contains(workTitle))
                {
                    person.KnownFor.Add(workTitle);
                }
            }

            if (string.IsNullOrWhiteSpace(person.Name))
            {
                person.Name = person.DisplayName;
            }

            AddToCache(PersonDetailsCache, personId, person, PersonCacheDuration);

            return person;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching elCinema person details for person ID '{PersonId}'", personId);
            return null;
        }
    }

    private static string? GetImageUrl(IElement? image, string pageUrl)
    {
        if (image == null)
        {
            return null;
        }

        var candidate = image.GetAttribute("src")
            ?? image.GetAttribute("data-src")
            ?? image.GetAttribute("data-original");

        if (string.IsNullOrWhiteSpace(candidate) || candidate.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            candidate = image.GetAttribute("data-src")
                ?? image.GetAttribute("data-original")
                ?? image.GetAttribute("srcset")?.Split(',', StringSplitOptions.TrimEntries)[0].Split(' ')[0];
        }

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        return Uri.TryCreate(new Uri(pageUrl), candidate, out var absoluteUri)
            ? absoluteUri.AbsoluteUri
            : null;
    }

    private async Task<HttpResponseMessage> SendElCinemaRequestAsync(
        HttpClient httpClient,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        await RequestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var delay = _nextRequestAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _consecutiveRateLimits++;
                var retryDelay = GetRetryDelay(response, _consecutiveRateLimits);
                _nextRequestAt = now + (retryDelay > MinimumRequestInterval ? retryDelay : MinimumRequestInterval);
                _logger.LogWarning(
                    "elCinema rate limited a request; delaying further requests for {DelaySeconds} seconds",
                    (_nextRequestAt - now).TotalSeconds);
            }
            else
            {
                _nextRequestAt = now + MinimumRequestInterval;
                if (response.IsSuccessStatusCode)
                {
                    _consecutiveRateLimits = 0;
                }
            }

            return response;
        }
        finally
        {
            RequestGate.Release();
        }
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int consecutiveRateLimits)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is TimeSpan delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (retryAfter?.Date is DateTimeOffset retryDate)
        {
            var dateDelay = retryDate - DateTimeOffset.UtcNow;
            if (dateDelay > TimeSpan.Zero)
            {
                return dateDelay;
            }
        }

        var exponent = Math.Min(consecutiveRateLimits - 1, 5);
        return TimeSpan.FromSeconds(Math.Min(120, 5 * (1 << exponent)));
    }

    private static bool TryGetCached<T>(ConcurrentDictionary<string, CacheEntry<T>> cache, string key, out T? value)
        where T : class
    {
        if (cache.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAt > DateTimeOffset.UtcNow)
            {
                value = entry.Value;
                return true;
            }

            cache.TryRemove(key, out _);
        }

        value = null;
        return false;
    }

    private static void AddToCache<T>(ConcurrentDictionary<string, CacheEntry<T>> cache, string key, T? value, TimeSpan duration)
        where T : class
    {
        cache[key] = new CacheEntry<T>(value, DateTimeOffset.UtcNow.Add(duration));

        if (cache.Count > 1000)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var cacheItem in cache)
            {
                if (cacheItem.Value.ExpiresAt <= now)
                {
                    cache.TryRemove(cacheItem.Key, out _);
                }
            }
        }
    }

    private sealed record CacheEntry<T>(T? Value, DateTimeOffset ExpiresAt)
        where T : class;
}
