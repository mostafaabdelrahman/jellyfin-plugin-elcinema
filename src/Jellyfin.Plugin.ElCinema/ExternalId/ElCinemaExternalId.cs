using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.ElCinema.ExternalId;

/// <summary>
/// External ID provider for elCinema works.
/// </summary>
public class ElCinemaExternalId : IExternalId
{
    /// <summary>
    /// Name of provider key used across metadata and image providers.
    /// </summary>
    public const string ProviderIdKey = "ElCinema";

    /// <inheritdoc />
    public string ProviderName => "elCinema";

    /// <inheritdoc />
    public string Key => ProviderIdKey;

    /// <inheritdoc />
    public ExternalIdMediaType? Type => ExternalIdMediaType.Movie;

    /// <inheritdoc />
    public string UrlFormatString => "https://elcinema.com/work/{0}/";

    /// <inheritdoc />
    public bool Supports(IHasProviderIds item) => item is Movie;
}
