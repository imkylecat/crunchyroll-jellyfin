using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Crunchyroll.Configuration;

/// <summary>
/// Plugin configuration for the Crunchyroll metadata provider.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        PreferredLanguage = "pt-BR";
        FallbackLanguage = "en-US";
        EnableSeasonMapping = true;
        EnableEpisodeOffsetMapping = true;
        CacheExpirationHours = 24;
        FlareSolverrUrl = string.Empty;
        Username = string.Empty;
        Password = string.Empty;
        EnablePremiumCaching = false;
        EnableDebugHtmlLogging = false;
        MappingSensitivity = 70;
    }

    /// <summary>
    /// Gets or sets a value indicating whether to save scraped HTML to files for debugging.
    /// When enabled, HTML responses from FlareSolverr will be saved to the logs directory.
    /// </summary>
    public bool EnableDebugHtmlLogging { get; set; }

    /// <summary>
    /// Gets or sets the mapping sensitivity for matching series names.
    /// </summary>
    public int MappingSensitivity { get; set; }

    /// <summary>
    /// Gets or sets the username for login.
    /// </summary>
    public string Username { get; set; }

    /// <summary>
    /// Gets or sets the password for login.
    /// </summary>
    public string Password { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to enable experimental premium caching features.
    /// </summary>
    public bool EnablePremiumCaching { get; set; }

    /// <summary>
    /// Gets or sets the preferred language for metadata.
    /// </summary>
    public string PreferredLanguage { get; set; }

    /// <summary>
    /// Gets or sets the fallback language when preferred is not available.
    /// </summary>
    public string FallbackLanguage { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to enable automatic season mapping.
    /// When enabled, the plugin will try to match Jellyfin seasons with Crunchyroll seasons.
    /// </summary>
    public bool EnableSeasonMapping { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to enable episode offset mapping.
    /// When enabled, the plugin will handle cases where Crunchyroll episode numbers
    /// don't start at 1 for each season (e.g., Season 2 Episode 1 = Episode 25 on Crunchyroll).
    /// </summary>
    public bool EnableEpisodeOffsetMapping { get; set; }

    /// <summary>
    /// Gets or sets the cache expiration time in hours.
    /// </summary>
    public int CacheExpirationHours { get; set; }

    /// <summary>
    /// Gets or sets the FlareSolverr URL for bypassing Cloudflare protection.
    /// Example: http://localhost:8191
    /// Leave empty to try direct API access (may not work from server IPs).
    /// </summary>
    public string FlareSolverrUrl { get; set; }

    /// <summary>
    /// Gets or sets the Docker container name/ID of FlareSolverr.
    /// Used for CDP-based authentication (docker exec into the container to get auth tokens).
    /// Default: "flaresolverr". Leave empty to disable Docker-based auth.
    /// </summary>
    public string DockerContainerName { get; set; } = "flaresolverr";
}

