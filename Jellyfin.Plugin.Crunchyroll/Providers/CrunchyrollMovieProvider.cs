using Jellyfin.Plugin.Crunchyroll.Api;
using Jellyfin.Plugin.Crunchyroll.Models;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Crunchyroll.Providers;

/// <summary>
/// Metadata provider for anime movies from Crunchyroll.
/// Uses a multi-level matching algorithm since Crunchyroll has no movie_listing type:
///   Step 1: L1 Search → score all API results
///   Step 2: L1-Direct → best film-like series with score >= threshold → DONE
///   Step 3: L2-Cascade → for each L1 result (score >= L2MinScore):
///           → fetch seasons, score titles + DISTINCTIVE WORD BONUS
///           → collect ALL film-like matches, pick BEST combined score → DONE
///   Step 4: L1-Film-Fallback → best film-like L1 with score >= L2MinScore → DONE
/// 
/// Three film patterns on Crunchyroll:
///   (A) Standalone 1-ep series (e.g., Jujutsu Kaisen 0)
///   (B) 1-ep season inside parent anime (e.g., Demon Slayer Mugen Train)
///   (C) Season inside dedicated film collection (e.g., Dragon Ball Filmes)
/// </summary>
public class CrunchyrollMovieProvider : IRemoteMetadataProvider<Movie, MovieInfo>, IHasOrder
{
    private readonly ILogger<CrunchyrollMovieProvider> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    // Movie-specific matching constants
    private const int L2MinScore = 25;           // Min L1 score to attempt L2 drill-down
    private const int L2SeasonMinScore = 25;     // Min combined season score for L2 match
    private const int L2MaxCandidates = 10;      // Max L1 results to try L2 on
    private const int DistinctiveWordBonus = 40; // Max bonus for distinctive word matches

    /// <summary>
    /// Initializes a new instance of the <see cref="CrunchyrollMovieProvider"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    public CrunchyrollMovieProvider(ILogger<CrunchyrollMovieProvider> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc />
    public string Name => "Crunchyroll";

    /// <inheritdoc />
    public int Order => 3;

    /// <inheritdoc />
    public async Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Movie>();

        var config = Plugin.Instance?.Configuration;
        var locale = config?.PreferredLanguage ?? "pt-BR";

        using var httpClient = _httpClientFactory.CreateClient();
        var flareSolverrUrl = config?.FlareSolverrUrl;
        var username = config?.Username;
        var password = config?.Password;
        var dockerContainerName = config?.DockerContainerName;
        var chromeCdpUrl = config?.ChromeCdpUrl;
        using var apiClient = new CrunchyrollApiClient(httpClient, _logger, locale, flareSolverrUrl, username, password, dockerContainerName, chromeCdpUrl);

        // Check if we already have a Crunchyroll ID
        string? crunchyrollId = info.GetProviderId("CrunchyrollMovie");
        string? crunchyrollSeasonId = info.GetProviderId("CrunchyrollMovieSeason");

        // If we already have an ID, fetch directly
        if (!string.IsNullOrEmpty(crunchyrollId))
        {
            _logger.LogDebug("[Movie] Fetching movie by Crunchyroll ID: {Id}", crunchyrollId);
            return await FetchMovieMetadataById(apiClient, crunchyrollId, crunchyrollSeasonId, cancellationToken).ConfigureAwait(false);
        }

        // Search for the movie
        if (string.IsNullOrEmpty(info.Name))
        {
            return result;
        }

        _logger.LogDebug("[Movie] Searching for movie: {Name}", info.Name);
        var match = await FindMovieMatch(apiClient, info.Name, info.Year, cancellationToken).ConfigureAwait(false);

        if (match == null)
        {
            _logger.LogDebug("[Movie] No match found for: {Name}", info.Name);
            return result;
        }

        _logger.LogInformation("[Movie] Matched \"{Name}\" → series={SeriesId}, season={SeasonTitle} via {Strategy}",
            info.Name, match.SeriesId, match.SeasonTitle ?? "(direct)", match.Strategy);

        return await FetchMovieMetadataById(apiClient, match.SeriesId, match.SeasonId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo searchInfo, CancellationToken cancellationToken)
    {
        var results = new List<RemoteSearchResult>();

        var config = Plugin.Instance?.Configuration;
        var locale = config?.PreferredLanguage ?? "pt-BR";

        using var httpClient = _httpClientFactory.CreateClient();
        var flareSolverrUrl = config?.FlareSolverrUrl;
        var username = config?.Username;
        var password = config?.Password;
        var dockerContainerName = config?.DockerContainerName;
        var chromeCdpUrl = config?.ChromeCdpUrl;
        using var apiClient = new CrunchyrollApiClient(httpClient, _logger, locale, flareSolverrUrl, username, password, dockerContainerName, chromeCdpUrl);

        // Check if we have a Crunchyroll ID
        string? crunchyrollId = searchInfo.GetProviderId("CrunchyrollMovie");
        if (!string.IsNullOrEmpty(crunchyrollId))
        {
            var series = await apiClient.GetSeriesAsync(crunchyrollId, cancellationToken).ConfigureAwait(false);
            if (series != null)
            {
                results.Add(CreateSearchResult(series));
                return results;
            }
        }

        // Search by name
        if (!string.IsNullOrEmpty(searchInfo.Name))
        {
            var searchResults = await apiClient.SearchSeriesAsync(searchInfo.Name, 10, cancellationToken).ConfigureAwait(false);

            foreach (var item in searchResults)
            {
                results.Add(CreateSearchResultFromItem(item));
            }
        }

        return results;
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        return _httpClientFactory.CreateClient().GetAsync(new Uri(url), cancellationToken);
    }

    // ─── Movie Matching Algorithm ───

    /// <summary>
    /// Finds the best Crunchyroll match for a movie name.
    /// Uses a multi-level strategy: L1-Direct → L2-Cascade → L1-Fallback.
    /// </summary>
    private async Task<MovieMatch?> FindMovieMatch(CrunchyrollApiClient apiClient, string movieName, int? year, CancellationToken cancellationToken)
    {
        int mappingSensitivity = Plugin.Instance?.Configuration.MappingSensitivity ?? 70;

        // ═══ STEP 1: L1 Search ═══
        var searchResults = await apiClient.SearchSeriesAsync(movieName, 10, cancellationToken).ConfigureAwait(false);
        if (searchResults.Count == 0)
        {
            _logger.LogDebug("[Movie] No search results for: {Name}", movieName);
            return null;
        }

        var normalizedMovie = NormalizeName(movieName);
        var scored = searchResults
            .Where(r => r.Title != null)
            .Select(r => new
            {
                Item = r,
                Score = CalculateMatchScore(normalizedMovie, NormalizeName(r.Title!)),
                IsFilmLike = IsLikelyFilm(r)
            })
            .OrderByDescending(x => x.Score)
            .ToList();

        foreach (var s in scored.Take(5))
        {
            _logger.LogDebug("[Movie L1] {Id} \"{Title}\" score={Score} film={Film} (S={Seasons} E={Episodes})",
                s.Item.Id, s.Item.Title, s.Score, s.IsFilmLike,
                s.Item.SeriesMetadata?.SeasonCount ?? 0, s.Item.SeriesMetadata?.EpisodeCount ?? 0);
        }

        // ═══ STEP 2: L1-Direct ═══
        // If a film-like series scores above threshold, it's a direct match (Pattern A)
        var directHit = scored.FirstOrDefault(x => x.Score >= mappingSensitivity && x.IsFilmLike);
        if (directHit != null)
        {
            _logger.LogDebug("[Movie] L1-Direct match: {Id} \"{Title}\" score={Score}",
                directHit.Item.Id, directHit.Item.Title, directHit.Score);
            return new MovieMatch(directHit.Item.Id!, null, null, null, "L1-Direct");
        }

        // ═══ STEP 3: L2-Cascade with Distinctive Word Scoring ═══
        // Drill into top L1 candidates to find film-like seasons (Pattern B & C)
        var l2Candidates = scored
            .Where(x => x.Score >= L2MinScore)
            .Take(L2MaxCandidates)
            .ToList();

        _logger.LogDebug("[Movie] L2-Cascade: {Count} candidates (score >= {Min})", l2Candidates.Count, L2MinScore);

        var movieWords = normalizedMovie
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet();

        var allMatches = new List<(string SeriesId, CrunchyrollSeason Season, int TitleScore, int Bonus, int Combined)>();

        foreach (var cand in l2Candidates)
        {
            var seasons = await apiClient.GetSeasonsAsync(cand.Item.Id!, cancellationToken).ConfigureAwait(false);
            if (seasons.Count == 0)
            {
                continue;
            }

            // Distinctive words: movie name words NOT present in the series title
            var seriesWords = NormalizeName(cand.Item.Title ?? "")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet();
            var distinctiveWords = movieWords.Except(seriesWords).ToHashSet();

            // Only check film-like seasons (1-2 episodes)
            var filmSeasons = seasons.Where(s => s.NumberOfEpisodes <= 2).ToList();
            if (filmSeasons.Count == 0)
            {
                continue;
            }

            foreach (var season in filmSeasons)
            {
                if (string.IsNullOrEmpty(season.Title))
                {
                    continue;
                }

                var normalizedSeason = NormalizeName(season.Title);
                int titleScore = CalculateMatchScore(normalizedMovie, normalizedSeason);

                // Distinctive word bonus: words unique to movie name that appear in season title
                var seasonWordSet = normalizedSeason
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .ToHashSet();
                int distinctiveHits = distinctiveWords.Count(dw =>
                    seasonWordSet.Any(sw => sw.Contains(dw) || dw.Contains(sw)));
                int bonus = distinctiveWords.Count > 0
                    ? (int)((float)DistinctiveWordBonus * distinctiveHits / distinctiveWords.Count)
                    : 0;

                int combined = titleScore + bonus;

                _logger.LogDebug("[Movie L2] Series={SeriesId} Season=\"{SeasonTitle}\" title={TitleScore} +dist={Bonus} ={Combined} (eps={Eps})",
                    cand.Item.Id, season.Title, titleScore, bonus, combined, season.NumberOfEpisodes);

                if (combined >= L2SeasonMinScore)
                {
                    allMatches.Add((cand.Item.Id!, season, titleScore, bonus, combined));
                }
            }
        }

        // Pick BEST L2 match across all candidates
        var bestL2 = allMatches
            .OrderByDescending(m => m.Combined)
            .ThenByDescending(m => m.Bonus)
            .FirstOrDefault();

        if (bestL2 != default)
        {
            _logger.LogDebug("[Movie] L2-Cascade match: series={SeriesId} season=\"{SeasonTitle}\" combined={Score}",
                bestL2.SeriesId, bestL2.Season.Title, bestL2.Combined);
            return new MovieMatch(bestL2.SeriesId, bestL2.Season.Id, bestL2.Season.Title, bestL2.Season.Description, "L2-Cascade");
        }

        // ═══ STEP 4: L1-Film-Fallback ═══
        // Last resort: any film-like L1 result above minimum score
        var fallback = scored.FirstOrDefault(x => x.IsFilmLike && x.Score >= L2MinScore);
        if (fallback != null)
        {
            _logger.LogDebug("[Movie] L1-Fallback match: {Id} \"{Title}\" score={Score}",
                fallback.Item.Id, fallback.Item.Title, fallback.Score);

            // Try to also find a matching season within the fallback series
            var fbSeasons = await apiClient.GetSeasonsAsync(fallback.Item.Id!, cancellationToken).ConfigureAwait(false);
            if (fbSeasons.Count > 0)
            {
                var fbSeriesWords = NormalizeName(fallback.Item.Title ?? "")
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                var fbDistinctive = movieWords.Except(fbSeriesWords).ToHashSet();

                var bestFbSeason = fbSeasons
                    .Where(s => s.NumberOfEpisodes <= 2 && !string.IsNullOrEmpty(s.Title))
                    .Select(s =>
                    {
                        var normS = NormalizeName(s.Title!);
                        int ts = CalculateMatchScore(normalizedMovie, normS);
                        var sw = normS.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                        int dh = fbDistinctive.Count(dw => sw.Any(w => w.Contains(dw) || dw.Contains(w)));
                        int b = fbDistinctive.Count > 0 ? (int)((float)DistinctiveWordBonus * dh / fbDistinctive.Count) : 0;
                        return new { Season = s, Combined = ts + b };
                    })
                    .OrderByDescending(x => x.Combined)
                    .FirstOrDefault();

                if (bestFbSeason != null && bestFbSeason.Combined >= L2SeasonMinScore)
                {
                    return new MovieMatch(fallback.Item.Id!, bestFbSeason.Season.Id, bestFbSeason.Season.Title,
                        bestFbSeason.Season.Description, "L1-Fallback+L2");
                }
            }

            return new MovieMatch(fallback.Item.Id!, null, null, null, "L1-Fallback");
        }

        // ═══ STEP 5: No match ═══
        _logger.LogDebug("[Movie] No match found through any strategy for: {Name}", movieName);
        return null;
    }

    /// <summary>
    /// Fetches movie metadata from a matched Crunchyroll series (and optionally season).
    /// </summary>
    private async Task<MetadataResult<Movie>> FetchMovieMetadataById(
        CrunchyrollApiClient apiClient, string seriesId, string? seasonId, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Movie>();

        var series = await apiClient.GetSeriesAsync(seriesId, cancellationToken).ConfigureAwait(false);
        if (series == null)
        {
            _logger.LogDebug("[Movie] Could not fetch series: {SeriesId}", seriesId);
            return result;
        }

        // If we have a season ID, try to get the season description for a more accurate overview
        string? overview = null;
        string? movieTitle = null;
        if (!string.IsNullOrEmpty(seasonId))
        {
            var seasons = await apiClient.GetSeasonsAsync(seriesId, cancellationToken).ConfigureAwait(false);
            var matchedSeason = seasons.FirstOrDefault(s => s.Id == seasonId);
            if (matchedSeason != null)
            {
                movieTitle = matchedSeason.Title;
                // Use season description if available, fall back to series description
                overview = !string.IsNullOrEmpty(matchedSeason.Description)
                    ? matchedSeason.Description
                    : series.ExtendedDescription ?? series.Description;
            }
        }

        // Fall back to series data
        movieTitle ??= series.Title;
        overview ??= series.ExtendedDescription ?? series.Description;

        result.HasMetadata = true;
        result.Item = new Movie
        {
            Name = movieTitle,
            Overview = overview,
            ProductionYear = series.SeriesLaunchYear,
            OfficialRating = series.MaturityRatings?.FirstOrDefault()
        };

        // Set provider IDs
        result.Item.SetProviderId("CrunchyrollMovie", seriesId);
        if (!string.IsNullOrEmpty(seasonId))
        {
            result.Item.SetProviderId("CrunchyrollMovieSeason", seasonId);
        }

        // Also set base Crunchyroll ID for image provider compatibility
        result.Item.SetProviderId("Crunchyroll", seriesId);

        // Add genres/tags from keywords
        if (series.Keywords != null)
        {
            foreach (var keyword in series.Keywords.Take(10))
            {
                result.Item.AddGenre(keyword);
            }
        }

        _logger.LogInformation("[Movie] Successfully retrieved metadata for: {Name} (series={SeriesId})", movieTitle, seriesId);

        return result;
    }

    // ─── Heuristics ───

    /// <summary>
    /// Determines if a search result looks like a film rather than a regular series.
    /// Films on Crunchyroll typically have very few episodes (1-3) and few seasons.
    /// </summary>
    private static bool IsLikelyFilm(CrunchyrollSearchItem item)
    {
        int seasonCount = item.SeriesMetadata?.SeasonCount ?? 0;
        int episodeCount = item.SeriesMetadata?.EpisodeCount ?? 0;

        // Single episode = definitely a film
        if (seasonCount <= 1 && episodeCount <= 1)
        {
            return true;
        }

        // Multiple "seasons" but very few episodes per season (dub variants)
        if (seasonCount > 1 && episodeCount <= seasonCount * 2)
        {
            return true;
        }

        // Very few episodes total
        if (episodeCount <= 3)
        {
            return true;
        }

        return false;
    }

    // ─── Name Normalization ───

    /// <summary>
    /// Normalizes a name for comparison. Handles slashes, Unicode dashes, parentheses.
    /// </summary>
    internal static string NormalizeName(string name)
    {
        return name
            .ToLowerInvariant()
            .Replace(":", "")
            .Replace("/", " ")
            .Replace("-", " ")       // U+002D Hyphen-Minus
            .Replace("\u2010", " ")  // ‐ Hyphen
            .Replace("\u2011", " ")  // ‑ Non-Breaking Hyphen
            .Replace("\u2013", " ")  // – En Dash
            .Replace("\u2014", " ")  // — Em Dash
            .Replace("(", "")
            .Replace(")", "")
            .Replace("  ", " ")
            .Replace("  ", " ")
            .Trim();
    }

    // ─── Match Scoring ───

    /// <summary>
    /// Calculates a match score between a search query and a candidate title.
    /// Uses containment check and word-overlap scoring.
    /// </summary>
    internal static int CalculateMatchScore(string search, string candidate)
    {
        if (candidate.Contains(search))
        {
            return 100 - (candidate.Length - search.Length);
        }

        if (search.Contains(candidate))
        {
            return 100 - (search.Length - candidate.Length);
        }

        // Word overlap score
        var searchWords = search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var candidateWords = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matchingWords = searchWords.Intersect(candidateWords).Count();

        var similarwords = 0;
        foreach (var word in searchWords.Except(candidateWords).ToArray())
        {
            foreach (var cword in candidateWords.Except(searchWords).ToArray())
            {
                if (cword.Contains(word) || word.Contains(cword))
                {
                    similarwords++;
                    break;
                }
            }
        }

        float score = 100f * (matchingWords + 0.2f * similarwords) / Math.Max(searchWords.Length, candidateWords.Length);
        return (int)score;
    }

    // ─── Search Result Helpers ───

    private static RemoteSearchResult CreateSearchResult(CrunchyrollSeries series)
    {
        var result = new RemoteSearchResult
        {
            Name = series.Title,
            Overview = series.Description,
            ProductionYear = series.SeriesLaunchYear,
            SearchProviderName = "Crunchyroll"
        };

        if (!string.IsNullOrEmpty(series.Id))
        {
            result.SetProviderId("CrunchyrollMovie", series.Id);
        }

        var posterUrl = GetBestImage(series.Images?.PosterTall);
        if (!string.IsNullOrEmpty(posterUrl))
        {
            result.ImageUrl = posterUrl;
        }

        return result;
    }

    private static RemoteSearchResult CreateSearchResultFromItem(CrunchyrollSearchItem item)
    {
        var result = new RemoteSearchResult
        {
            Name = item.Title,
            Overview = item.Description,
            SearchProviderName = "Crunchyroll"
        };

        if (!string.IsNullOrEmpty(item.Id))
        {
            result.SetProviderId("CrunchyrollMovie", item.Id);
        }

        var posterUrl = GetBestImage(item.Images?.PosterTall);
        if (!string.IsNullOrEmpty(posterUrl))
        {
            result.ImageUrl = posterUrl;
        }

        return result;
    }

    private static string? GetBestImage(List<List<CrunchyrollImage>>? images)
    {
        if (images == null || images.Count == 0)
        {
            return null;
        }

        var imageSet = images.FirstOrDefault();
        if (imageSet == null || imageSet.Count == 0)
        {
            return null;
        }

        var bestImage = imageSet
            .OrderByDescending(i => i.Width * i.Height)
            .FirstOrDefault();

        return bestImage?.Source;
    }

    // ─── Internal Models ───

    /// <summary>
    /// Represents a matched movie result from the algorithm.
    /// </summary>
    private record MovieMatch(
        string SeriesId,
        string? SeasonId,
        string? SeasonTitle,
        string? SeasonDescription,
        string Strategy
    );
}
