using System;
using System.Collections.Generic;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Crunchyroll.Providers;

/// <summary>
/// External URL provider for Crunchyroll.
/// </summary>
public class CrunchyrollExternalUrlProvider : IExternalUrlProvider
{
    /// <inheritdoc/>
    public string Name => "Crunchyroll";

    /// <inheritdoc/>
    public IEnumerable<string> GetExternalUrls(BaseItem item)
    {
        switch (item)
        {
            case Series:
                if (item.TryGetProviderId(MetadataProvider.Tmdb, out var externalId))
                {
                    yield return $"https://www.crunchyroll.com/series/{externalId}";
                }

                break;
        }
    }
}