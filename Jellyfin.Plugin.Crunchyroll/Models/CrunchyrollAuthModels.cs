using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Crunchyroll.Models;

/// <summary>
/// Response from Crunchyroll authentication endpoint.
/// </summary>
public class CrunchyrollAuthResponse
{
    /// <summary>
    /// Gets or sets the access token for API requests.
    /// </summary>
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    /// <summary>
    /// Gets or sets the token expiration time in seconds.
    /// </summary>
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    /// <summary>
    /// Gets or sets the token type (usually "Bearer").
    /// </summary>
    [JsonPropertyName("token_type")]
    public required string TokenType { get; init; }

    /// <summary>
    /// Gets or sets the scope of the token.
    /// </summary>
    [JsonPropertyName("scope")]
    public required string Scope { get; init; }

    /// <summary>
    /// Gets or sets the country code.
    /// </summary>
    [JsonPropertyName("country")]
    public required string Country { get; init; }
}
