using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.ElCinema.ExternalId;

/// <summary>
/// External ID provider for elCinema people.
/// </summary>
public class ElCinemaPersonExternalId : IExternalId
{
    /// <summary>
    /// Provider key used for elCinema person IDs.
    /// </summary>
    public const string ProviderIdKey = "ElCinemaPerson";

    /// <inheritdoc />
    public string ProviderName => "elCinema";

    /// <inheritdoc />
    public string Key => ProviderIdKey;

    /// <inheritdoc />
    public ExternalIdMediaType? Type => ExternalIdMediaType.Person;

    /// <inheritdoc />
    public string UrlFormatString => "https://elcinema.com/person/{0}/";

    /// <inheritdoc />
    public bool Supports(IHasProviderIds item) => item is Person;
}