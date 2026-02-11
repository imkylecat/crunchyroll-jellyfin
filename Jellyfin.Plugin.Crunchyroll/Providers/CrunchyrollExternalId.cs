using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Crunchyroll.Providers;

/// <summary>
/// External ID provider for Crunchyroll series.
/// </summary>
public class CrunchyrollSeriesExternalId : IExternalId
{
    /// <inheritdoc />
    public string ProviderName => "Crunchyroll";

    /// <inheritdoc />
    public string Key => "Crunchyroll";

    /// <inheritdoc />
    public ExternalIdMediaType? Type => ExternalIdMediaType.Series;

    /// <inheritdoc />
    public string? UrlFormatString => "https://www.crunchyroll.com/series/{0}";

    /// <inheritdoc />
    public bool Supports(IHasProviderIds item)
    {
        return item is Series;
    }
}

/// <summary>
/// External ID provider for Crunchyroll seasons.
/// </summary>
public class CrunchyrollSeasonExternalId : IExternalId
{
    /// <inheritdoc />
    public string ProviderName => "Crunchyroll";

    /// <inheritdoc />
    public string Key => "CrunchyrollSeason";

    /// <inheritdoc />
    public ExternalIdMediaType? Type => ExternalIdMediaType.Season;

    /// <inheritdoc />
    public string? UrlFormatString => null;

    /// <inheritdoc />
    public bool Supports(IHasProviderIds item)
    {
        return item is Season;
    }
}

/// <summary>
/// External ID provider for Crunchyroll episodes.
/// </summary>
public class CrunchyrollEpisodeExternalId : IExternalId
{
    /// <inheritdoc />
    public string ProviderName => "Crunchyroll";

    /// <inheritdoc />
    public string Key => "CrunchyrollEpisode";

    /// <inheritdoc />
    public ExternalIdMediaType? Type => ExternalIdMediaType.Episode;

    /// <inheritdoc />
    public string? UrlFormatString => "https://www.crunchyroll.com/watch/{0}";

    /// <inheritdoc />
    public bool Supports(IHasProviderIds item)
    {
        return item is Episode;
    }
}

/// <summary>
/// External ID provider for Crunchyroll movies.
/// </summary>
public class CrunchyrollMovieExternalId : IExternalId
{
    /// <inheritdoc />
    public string ProviderName => "Crunchyroll";

    /// <inheritdoc />
    public string Key => "CrunchyrollMovie";

    /// <inheritdoc />
    public ExternalIdMediaType? Type => ExternalIdMediaType.Movie;

    /// <inheritdoc />
    public string? UrlFormatString => "https://www.crunchyroll.com/series/{0}";

    /// <inheritdoc />
    public bool Supports(IHasProviderIds item)
    {
        return item is Movie;
    }
}
