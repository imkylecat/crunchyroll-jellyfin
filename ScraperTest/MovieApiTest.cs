using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

/// <summary>
/// Quick test to check how Crunchyroll API treats anime films:
/// - Do films appear as "movie_listing" type in search?
/// - Or only as seasons with 1 episode inside a series?
/// - What about standalone films vs films bundled with series?
/// </summary>
public class MovieApiTest
{
    private const string BaseUrl = "https://www.crunchyroll.com";
    private const string BasicAuthToken = "bmR0aTZicXlqcm9wNXZnZjF0dnU6elpIcS00SEJJVDlDb2FMcnBPREJjRVRCTUNHai1QNlg=";

    public static async Task RunMovieTest()
    {
        Console.WriteLine("=== Crunchyroll Movie API Test ===\n");

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Crunchyroll/3.50.2");

        // 1. Get anonymous auth token
        Console.WriteLine("[1] Getting anonymous auth token...");
        var token = await GetAnonymousToken(httpClient);
        if (string.IsNullOrEmpty(token))
        {
            Console.WriteLine("ERROR: Could not get auth token. API may be blocked by Cloudflare.");
            Console.WriteLine("Will try without auth...\n");
        }
        else
        {
            Console.WriteLine($"Got token: {token[..20]}...\n");
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        // 2. Search with type=movie_listing
        var movieQueries = new[]
        {
            "Solomon",                    // Fate Solomon (inside Babylonia series)
            "Dragon Ball Super Broly",    // Standalone film
            "Jujutsu Kaisen 0",           // Film prequel
            "One Piece Film Red",         // Standalone film
            "Demon Slayer Mugen Train",   // Film that became a "season"
            "Sword Art Online",           // Has Ordinal Scale movie
        };

        foreach (var query in movieQueries)
        {
            Console.WriteLine($"=== Searching: \"{query}\" ===");
            
            // Search as movie_listing
            await SearchAndPrint(httpClient, query, "movie_listing", token);
            
            // Search as series (to compare)
            await SearchAndPrint(httpClient, query, "series", token);

            // Search with no type filter (show all types)
            await SearchAndPrint(httpClient, query, null, token);
            
            Console.WriteLine();
        }

        // 3. Try direct movie_listing endpoints
        Console.WriteLine("\n=== Direct movie_listing endpoint tests ===");
        
        // Try to get Solomon as movie_listing
        var testIds = new[] 
        { 
            "GRVNC2P85",  // Solomon season ID inside Babylonia
            "GR24JZ886",  // Babylonia series ID
        };

        foreach (var id in testIds)
        {
            Console.WriteLine($"\n--- Testing ID: {id} ---");
            await TryEndpoint(httpClient, $"{BaseUrl}/content/v2/cms/movie_listings/{id}?locale=pt-BR", "movie_listings/{id}");
            await TryEndpoint(httpClient, $"{BaseUrl}/content/v2/cms/series/{id}?locale=pt-BR", "series/{id}");
            await TryEndpoint(httpClient, $"{BaseUrl}/content/v2/cms/series/{id}/seasons?locale=pt-BR", "series/{id}/seasons");
        }
    }

    static async Task<string?> GetAnonymousToken(HttpClient httpClient)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/auth/v1/token");
            request.Headers.TryAddWithoutValidation("Authorization", $"Basic {BasicAuthToken}");
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_id"),
                new KeyValuePair<string, string>("scope", "offline_access"),
            });

            var response = await httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Auth failed: {response.StatusCode}");
                Console.WriteLine($"Response: {json[..Math.Min(200, json.Length)]}");
                return null;
            }

            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("access_token").GetString();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Auth error: {ex.Message}");
            return null;
        }
    }

    static async Task SearchAndPrint(HttpClient httpClient, string query, string? type, string? token)
    {
        try
        {
            var typeParam = type != null ? $"&type={type}" : "";
            var typeLabel = type ?? "ALL";
            var url = $"{BaseUrl}/content/v2/discover/search?q={Uri.EscapeDataString(query)}&n=5{typeParam}&locale=pt-BR";
            
            var response = await httpClient.GetAsync(url);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"  [{typeLabel}] HTTP {response.StatusCode}");
                return;
            }

            using var doc = JsonDocument.Parse(json);
            var data = doc.RootElement.GetProperty("data");
            
            if (data.GetArrayLength() == 0)
            {
                Console.WriteLine($"  [{typeLabel}] No results");
                return;
            }

            foreach (var bucket in data.EnumerateArray())
            {
                var bucketType = bucket.GetProperty("type").GetString();
                var items = bucket.GetProperty("items");
                var count = items.GetArrayLength();
                
                Console.WriteLine($"  [{typeLabel}] type=\"{bucketType}\", count={count}");
                
                foreach (var item in items.EnumerateArray())
                {
                    var id = item.TryGetProperty("id", out var idProp) ? idProp.GetString() : "?";
                    var title = item.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : "?";
                    var slug = item.TryGetProperty("slug_title", out var slugProp) ? slugProp.GetString() : "?";
                    
                    // Check for movie_listing_metadata
                    string extra = "";
                    if (item.TryGetProperty("movie_listing_metadata", out var mlMeta))
                    {
                        var epCount = mlMeta.TryGetProperty("episode_count", out var ec) ? ec.GetInt32() : 0;
                        var duration = mlMeta.TryGetProperty("duration_ms", out var dur) ? dur.GetInt32() : 0;
                        extra = $" [movie_listing: {epCount} eps, {duration/60000}min]";
                    }
                    
                    // Check for series_metadata
                    if (item.TryGetProperty("series_metadata", out var sMeta))
                    {
                        var epCount = sMeta.TryGetProperty("episode_count", out var ec) ? ec.GetInt32() : 0;
                        var seasonCount = sMeta.TryGetProperty("season_count", out var sc) ? sc.GetInt32() : 0;
                        extra = $" [series: {seasonCount} seasons, {epCount} eps]";
                    }

                    Console.WriteLine($"    → {id}: \"{title}\" (slug={slug}){extra}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [{type ?? "ALL"}] Error: {ex.Message}");
        }
    }

    static async Task TryEndpoint(HttpClient httpClient, string url, string label)
    {
        try
        {
            var response = await httpClient.GetAsync(url);
            var json = await response.Content.ReadAsStringAsync();
            
            Console.WriteLine($"  {label}: HTTP {response.StatusCode}");
            
            if (response.IsSuccessStatusCode && json.Length > 0)
            {
                // Pretty print first 500 chars
                var preview = json.Length > 500 ? json[..500] + "..." : json;
                Console.WriteLine($"  Response: {preview}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  {label}: Error: {ex.Message}");
        }
    }
}
