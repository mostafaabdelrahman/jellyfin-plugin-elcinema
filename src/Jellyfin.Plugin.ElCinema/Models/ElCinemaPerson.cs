using System.Collections.Generic;

namespace Jellyfin.Plugin.ElCinema.Models;

/// <summary>
/// Detailed person / actor metadata extracted from elCinema person pages.
/// </summary>
public class ElCinemaPerson
{
    /// <summary>
    /// Gets or sets the elCinema person ID.
    /// </summary>
    public string PersonId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the primary person name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the original / transliterated name if available.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the role or profession for this person.
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the biography text.
    /// </summary>
    public string Biography { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the main profile image URL.
    /// </summary>
    public string ImageUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the birth date text if available.
    /// </summary>
    public string BirthDate { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the nationality text if available.
    /// </summary>
    public string Nationality { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a list of notable works or credits.
    /// </summary>
    public List<string> KnownFor { get; set; } = new();

    /// <summary>
    /// Gets the canonical elCinema person URL.
    /// </summary>
    public string ExternalUrl => $"https://elcinema.com/person/{PersonId}/";
}