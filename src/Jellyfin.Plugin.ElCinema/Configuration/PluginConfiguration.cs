using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ElCinema.Configuration;

/// <summary>
/// Plugin configuration for elCinema provider.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether to prioritize the Arabic title as the primary Jellyfin title.
    /// If false, transliterated English title is used as the primary title.
    /// </summary>
    public bool PreferArabicTitle { get; set; } = true;

    /// <summary>
    /// Gets or sets the HTTP User-Agent string used for elCinema requests.
    /// </summary>
    public string UserAgent { get; set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";

    /// <summary>
    /// Gets or sets a value indicating whether to import actor credits.
    /// </summary>
    public bool ImportActors { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to import director credits.
    /// </summary>
    public bool ImportDirectors { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to import writer credits.
    /// </summary>
    public bool ImportWriters { get; set; } = true;
}
