using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using Jellyfin.Plugin.Crunchyroll.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Crunchyroll.Api;

/// <summary>
/// HTTP client for communicating with the Crunchyroll API.
/// </summary>
public class CrunchyrollApiClient : IDisposable
{
    private const string BaseUrl = "https://www.crunchyroll.com";
    private const string ApiBaseUrl = "https://www.crunchyroll.com/content/v2";
    
    /// <summary>
    /// Basic authentication token for anonymous access (base64 of "cr_web:").
    /// </summary>
    private const string AnonymousAuthToken = "Y3Jfd2ViOg==";
    
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly string _locale;
    private bool _disposed;
    
    private string? _accessToken;
    private DateTime _tokenExpiration = DateTime.MinValue;
    private readonly SemaphoreSlim _authLock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="CrunchyrollApiClient"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="locale">The locale for API requests.</param>
    public CrunchyrollApiClient(HttpClient httpClient, ILogger logger, string locale = "pt-BR")
    {
        _httpClient = httpClient;
        _logger = logger;
        _locale = locale;

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", locale);
    }

    /// <summary>
    /// Ensures we have a valid access token, obtaining one if necessary.
    /// </summary>
    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiration)
        {
            return;
        }

        await _authLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring lock
            if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiration)
            {
                return;
            }

            await AuthenticateAnonymousAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _authLock.Release();
        }
    }

    /// <summary>
    /// Performs anonymous authentication with Crunchyroll.
    /// </summary>
    private async Task AuthenticateAnonymousAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Authenticating with Crunchyroll (anonymous)");

        // First, make an initial call to the Crunchyroll page to avoid bot detection
        try
        {
            var initialResponse = await _httpClient.GetAsync(BaseUrl, cancellationToken).ConfigureAwait(false);
            if (!initialResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Initial Crunchyroll request returned {StatusCode}", initialResponse.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to make initial Crunchyroll request");
        }

        // Now perform the anonymous authentication
        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/auth/v1/token");
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", AnonymousAuthToken);
        
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_id")
        });

        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError("Crunchyroll authentication failed with status {StatusCode}: {Error}", 
                response.StatusCode, errorContent);
            throw new HttpRequestException($"Crunchyroll authentication failed: {response.StatusCode}");
        }

        var authResponse = await response.Content.ReadFromJsonAsync<CrunchyrollAuthResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (authResponse == null)
        {
            throw new InvalidOperationException("Failed to deserialize Crunchyroll auth response");
        }

        _accessToken = authResponse.AccessToken;
        // Set expiration with 60 seconds buffer
        _tokenExpiration = DateTime.UtcNow.AddSeconds(authResponse.ExpiresIn - 60);

        _logger.LogDebug("Successfully authenticated with Crunchyroll (country: {Country})", authResponse.Country);
    }

    /// <summary>
    /// Makes an authenticated GET request to the Crunchyroll API.
    /// </summary>
    private async Task<T?> GetAuthenticatedAsync<T>(string url, CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false);

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError("Crunchyroll API request to {Url} failed with status {StatusCode}: {Error}", 
                url, response.StatusCode, errorContent);
            return default;
        }

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Searches for anime series on Crunchyroll.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="limit">Maximum number of results.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of search results.</returns>
    public async Task<List<CrunchyrollSearchItem>> SearchSeriesAsync(string query, int limit = 10, CancellationToken cancellationToken = default)
    {
        try
        {
            var encodedQuery = HttpUtility.UrlEncode(query);
            var url = $"{BaseUrl}/content/v2/discover/search?q={encodedQuery}&n={limit}&type=series&locale={_locale}";
            
            _logger.LogDebug("Searching Crunchyroll for: {Query}", query);
            
            var response = await GetAuthenticatedAsync<CrunchyrollSearchResponse>(url, cancellationToken)
                .ConfigureAwait(false);

            if (response?.Data == null || response.Data.Count == 0)
            {
                _logger.LogDebug("No results found for: {Query}", query);
                return new List<CrunchyrollSearchItem>();
            }

            var seriesResults = response.Data.FirstOrDefault(d => d.Type == "series");
            return seriesResults?.Items ?? new List<CrunchyrollSearchItem>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching Crunchyroll for: {Query}", query);
            return new List<CrunchyrollSearchItem>();
        }
    }

    /// <summary>
    /// Gets detailed information about a series.
    /// </summary>
    /// <param name="seriesId">The Crunchyroll series ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The series information or null if not found.</returns>
    public async Task<CrunchyrollSeries?> GetSeriesAsync(string seriesId, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"{ApiBaseUrl}/cms/series/{seriesId}?locale={_locale}";
            
            _logger.LogDebug("Fetching series: {SeriesId}", seriesId);
            
            var response = await GetAuthenticatedAsync<CrunchyrollResponse<CrunchyrollSeries>>(url, cancellationToken)
                .ConfigureAwait(false);

            return response?.Data?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching series: {SeriesId}", seriesId);
            return null;
        }
    }

    /// <summary>
    /// Gets all seasons for a series.
    /// </summary>
    /// <param name="seriesId">The Crunchyroll series ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of seasons.</returns>
    public async Task<List<CrunchyrollSeason>> GetSeasonsAsync(string seriesId, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"{ApiBaseUrl}/cms/series/{seriesId}/seasons?locale={_locale}";
            
            _logger.LogDebug("Fetching seasons for series: {SeriesId}", seriesId);
            
            var response = await GetAuthenticatedAsync<CrunchyrollResponse<CrunchyrollSeason>>(url, cancellationToken)
                .ConfigureAwait(false);

            return response?.Data ?? new List<CrunchyrollSeason>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching seasons for series: {SeriesId}", seriesId);
            return new List<CrunchyrollSeason>();
        }
    }

    /// <summary>
    /// Gets all episodes for a season.
    /// </summary>
    /// <param name="seasonId">The Crunchyroll season ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of episodes.</returns>
    public async Task<List<CrunchyrollEpisode>> GetEpisodesAsync(string seasonId, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"{ApiBaseUrl}/cms/seasons/{seasonId}/episodes?locale={_locale}";
            
            _logger.LogDebug("Fetching episodes for season: {SeasonId}", seasonId);
            
            var response = await GetAuthenticatedAsync<CrunchyrollResponse<CrunchyrollEpisode>>(url, cancellationToken)
                .ConfigureAwait(false);

            return response?.Data ?? new List<CrunchyrollEpisode>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching episodes for season: {SeasonId}", seasonId);
            return new List<CrunchyrollEpisode>();
        }
    }

    /// <summary>
    /// Gets a specific episode by ID.
    /// </summary>
    /// <param name="episodeId">The Crunchyroll episode ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The episode information or null if not found.</returns>
    public async Task<CrunchyrollEpisode?> GetEpisodeAsync(string episodeId, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"{ApiBaseUrl}/cms/episodes/{episodeId}?locale={_locale}";
            
            _logger.LogDebug("Fetching episode: {EpisodeId}", episodeId);
            
            var response = await GetAuthenticatedAsync<CrunchyrollResponse<CrunchyrollEpisode>>(url, cancellationToken)
                .ConfigureAwait(false);

            return response?.Data?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching episode: {EpisodeId}", episodeId);
            return null;
        }
    }

    /// <summary>
    /// Builds a full Crunchyroll URL for a series.
    /// </summary>
    /// <param name="slugTitle">The slug title of the series.</param>
    /// <returns>The full URL.</returns>
    public static string BuildSeriesUrl(string slugTitle)
    {
        return $"{BaseUrl}/series/{slugTitle}";
    }

    /// <summary>
    /// Builds a full Crunchyroll URL for an episode.
    /// </summary>
    /// <param name="episodeId">The episode ID.</param>
    /// <param name="slugTitle">The slug title of the episode.</param>
    /// <returns>The full URL.</returns>
    public static string BuildEpisodeUrl(string episodeId, string slugTitle)
    {
        return $"{BaseUrl}/watch/{episodeId}/{slugTitle}";
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes of resources.
    /// </summary>
    /// <param name="disposing">Whether to dispose managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _authLock.Dispose();
            _httpClient.Dispose();
        }

        _disposed = true;
    }
}

/// <summary>
/// Wrapper for search response.
/// </summary>
public class CrunchyrollSearchResponse
{
    /// <summary>
    /// Gets or sets the search result data.
    /// </summary>
    public List<CrunchyrollSearchResult>? Data { get; set; }
}
