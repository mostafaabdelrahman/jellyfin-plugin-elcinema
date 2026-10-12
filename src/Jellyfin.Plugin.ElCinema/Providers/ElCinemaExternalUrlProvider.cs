using System;
using System.Collections.Generic;
using Jellyfin.Plugin.ElCinema.ExternalId;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.ElCinema.Providers;

/// <summary>
/// Provides elCinema links for movies with a matched work ID.
/// </summary>
public class ElCinemaExternalUrlProvider : IExternalUrlProvider
{
    /// <inheritdoc />
    public string Name => "ElCinema";

    /// <inheritdoc />
    public IEnumerable<string> GetExternalUrls(BaseItem item)
    {
        if (item is not Movie)
        {
            return Array.Empty<string>();
        }

        var workId = item.GetProviderId(ElCinemaExternalId.ProviderIdKey);
        if (string.IsNullOrWhiteSpace(workId))
        {
            return Array.Empty<string>();
        }

        return new[] { $"https://elcinema.com/work/{Uri.EscapeDataString(workId)}/" };
    }
}
