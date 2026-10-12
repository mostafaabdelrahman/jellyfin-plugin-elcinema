namespace Jellyfin.Plugin.ElCinema.Models;

/// <summary>
/// Search candidate result from elCinema.
/// </summary>
public class ElCinemaSearchResult
{
    /// <summary>
    /// Gets or sets the elCinema work identifier.
    /// </summary>
    public string WorkId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the primary title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the transliterated / English title if available.
    /// </summary>
    public string? TransliteratedTitle { get; set; }

    /// <summary>
    /// Gets or sets the production year if discovered in the search entry.
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// Gets or sets the thumbnail/poster URL if present in search result.
    /// </summary>
    public string? PosterUrl { get; set; }
}
