using System.Collections.Generic;

namespace Jellyfin.Plugin.ElCinema.Models;

/// <summary>
/// Detailed movie metadata extracted from elCinema work pages.
/// </summary>
public class ElCinemaMovie
{
    /// <summary>
    /// Gets or sets the elCinema work ID.
    /// </summary>
    public string WorkId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the primary Arabic title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the transliterated English title.
    /// </summary>
    public string TransliteratedTitle { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the release / production year.
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// Gets or sets the community star rating.
    /// </summary>
    public float? Rating { get; set; }

    /// <summary>
    /// Gets or sets the main poster artwork URL.
    /// </summary>
    public string PosterUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the trailer or stream URL.
    /// </summary>
    public string StreamUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the full plot overview.
    /// </summary>
    public string Plot { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the genres list.
    /// </summary>
    public List<string> Genres { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of directors.
    /// </summary>
    public List<string> Directors { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of writers.
    /// </summary>
    public List<string> Writers { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of actors / cast members.
    /// </summary>
    public List<string> Cast { get; set; } = new();

    /// <summary>
    /// Gets or sets elCinema person IDs keyed by person name.
    /// </summary>
    public Dictionary<string, string> PersonIds { get; set; } = new();

    /// <summary>
    /// Gets or sets person image URLs keyed by elCinema person ID.
    /// </summary>
    public Dictionary<string, string> PersonImageUrls { get; set; } = new();
}
