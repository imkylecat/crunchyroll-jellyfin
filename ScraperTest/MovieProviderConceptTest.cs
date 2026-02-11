using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

/// <summary>
/// Movie Provider Concept Test v3.1: Multi-Candidate + Distinctive Word Scoring
/// 
/// Algorithm:
///   Step 1: L1 Search → score all API results
///   Step 2: L1-Direct → best film-like series with score >= threshold → DONE
///   Step 3: L2-Cascade → for each L1 result (score >= 30):
///           → fetch seasons, score titles + DISTINCTIVE WORD BONUS
///           → collect ALL film-like matches, pick BEST combined score → DONE
///   Step 4: L1-Film-Fallback → best film-like L1 with score >= 30 → DONE
///   Step 5: FAIL
/// 
/// v3.1 improvements over v3:
///   - Distinctive word bonus: words unique to the movie name (not in series title) get
///     extra weight when scoring season titles. Fixes Fate Solomon (prefers season 
///     containing "solomon" over "first order")
///   - Collects ALL L2 matches across all candidates, picks best combined score
///     (v3 stopped at first match, which could be wrong)
///   - Increased L2 candidate limit from 5 to 10 (fixes DB Super HERO)
///   - Normalizes Unicode dashes (U+2010 ‐, U+2013 –, U+2014 —)
/// </summary>
public class MovieProviderConceptTest
{
    private const string BaseUrl = "https://www.crunchyroll.com";
    private const string BasicAuthToken = "bmR0aTZicXlqcm9wNXZnZjF0dnU6elpIcS00SEJJVDlDb2FMcnBPREJjRVRCTUNHai1QNlg=";
    private const int MappingSensitivity = 70;
    private const int L2MinScore = 25;         // Min L1 score to attempt L2 drill-down
    private const int L2SeasonMinScore = 25;   // Min combined season score for L2 match
    private const int L2MaxCandidates = 10;    // Max L1 results to try L2 on (v3 was 5)
    private const int DistinctiveWordBonus = 40; // Max bonus for distinctive word matches

    private enum Expect { Pass, Hard, Fail }

    private record MovieTestCase(
        string FileName,
        string? ExpectedSeriesId,
        string? ExpectedSeasonHint,
        string Description,
        Expect Expectation
    );

    private static readonly List<MovieTestCase> TestCases = new()
    {
        // ─── Pattern A: Standalone film-like series ───
        new("Jujutsu Kaisen 0",
            "GMTE00194450", null,
            "Standalone 1-ep series (JJK ZERO)", Expect.Pass),
        new("JUJUTSU KAISEN 0 The Movie",
            "GMTE00194450", null,
            "Standalone 1-ep series (JJK ZERO)", Expect.Pass),
        new("Demon Slayer Infinity Castle",
            "G8DHV7809", null,
            "Standalone series", Expect.Pass),
        new("Demon Slayer Kimetsu no Yaiba Infinity Castle",
            "G8DHV7809", null,
            "Standalone series (full name)", Expect.Pass),
        new("PSYCHO-PASS Providence",
            "G24H1NWPJ", null,
            "Standalone 1-ep series", Expect.Pass),
        new("SPY x FAMILY Code White",
            "GMTE00335490", null,
            "Standalone 1-ep series (2 eps)", Expect.Pass),
        new("Blue Lock The Movie Episode Nagi",
            "GMTE00347067", null,
            "Standalone 1-ep series", Expect.Pass),

        // ─── Pattern B: Film as 1-ep season inside parent anime ───
        new("Demon Slayer Mugen Train",
            "GY5P48XEY", "Mugen Train",
            "1-ep season inside Demon Slayer main series", Expect.Pass),
        new("Fate Grand Order Solomon",
            "GR24JZ886", "Solomon",
            "1-ep season inside Babylonia series", Expect.Pass),
        new("Sword Art Online Progressive Aria of a Starless Night",
            "GR49G9VP6", "Aria",
            "1-ep season inside SAO", Expect.Pass),
        new("Konosuba Legend of Crimson",
            "GYE5K3GQR", "Crimson",
            "1-ep season inside KONOSUBA", Expect.Pass),
        new("Mob Psycho 100 Reigen",
            "GY190DKQR", "Reigen",
            "1-ep season inside Mob Psycho 100", Expect.Pass),
        new("Bungo Stray Dogs Dead Apple",
            "GR5VXQ8PR", "Dead Apple",
            "1-ep season inside Bungo Stray Dogs", Expect.Pass),
        new("That Time I Got Reincarnated as a Slime Scarlet Bond",
            "GYZJ43JMR", "Scarlet Bond",
            "1-ep season inside Slime series", Expect.Pass),
        new("Kuroko's Basketball The Movie Last Game",
            "G62P48X56", "Last Game",
            "1-ep season inside Kuroko series", Expect.Pass),
        new("Haikyu The Dumpster Battle",
            "GY8VM8MWY", "Dumpster Battle",
            "1-ep season inside Haikyu", Expect.Pass),

        // ─── Pattern C: Film inside dedicated film collection ───
        new("Dragon Ball Super SUPER HERO",
            "GQWH0M1GG", "Super Hero",
            "Season inside Dragon Ball (Filmes) collection", Expect.Pass),
        new("Dragon Ball Z Broly The Legendary Super Saiyan",
            "GQWH0M1GG", "Broly",
            "Season inside Dragon Ball (Filmes) collection", Expect.Hard),

        // ─── Known unavailable (geo-blocked in Brazil) ───
        new("One Piece Film Red",
            null, null,
            "NOT available on CR Brazil", Expect.Fail),
        new("My Hero Academia Two Heroes",
            null, null,
            "NOT available on CR Brazil", Expect.Fail),
        new("Dragon Ball Super Broly",
            null, null,
            "NOT available on CR Brazil (geo-blocked)", Expect.Fail),
    };

    public static async Task RunConceptTest()
    {
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  Movie Provider Concept Test v3.1: Distinctive Word Scoring             ║");
        Console.WriteLine("╠═══════════════════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  Step 2: L1-Direct    → film-like series >= threshold                   ║");
        Console.WriteLine("║  Step 3: L2-Cascade   → drill into series, distinctive word bonus       ║");
        Console.WriteLine("║  Step 4: L1-Fallback  → film-like series below threshold                ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════════════════╝\n");

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Crunchyroll/3.50.2");

        var token = await GetAnonymousToken(httpClient);
        if (string.IsNullOrEmpty(token))
        {
            Console.WriteLine("ERROR: Could not get auth token.");
            return;
        }
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Console.WriteLine("🔑 Auth OK.\n");

        int passed = 0, failed = 0, partial = 0, skipped = 0;

        foreach (var tc in TestCases)
        {
            Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            Console.WriteLine($"📁 Movie: \"{tc.FileName}\"");
            Console.WriteLine($"🎯 Expect: {tc.Description}");
            if (tc.ExpectedSeriesId != null)
                Console.WriteLine($"   series={tc.ExpectedSeriesId}, hint=\"{tc.ExpectedSeasonHint ?? "(direct)"}\"");

            if (tc.Expectation == Expect.Fail)
            {
                Console.WriteLine($"  ⏭️  SKIP — {tc.Description}\n");
                skipped++;
                continue;
            }

            // ═══ STEP 1: L1 Search ═══
            var searchResults = await SearchSeries(httpClient, tc.FileName);
            if (searchResults == null || searchResults.Count == 0)
            {
                Console.WriteLine("  ❌ No search results returned\n");
                failed++;
                continue;
            }

            var normalizedMovie = NormalizeName(tc.FileName);
            var scored = searchResults
                .Where(r => r.Title != null)
                .Select(r => new ScoredResult
                {
                    Item = r,
                    Score = CalculateMatchScore(normalizedMovie, NormalizeName(r.Title!)),
                    IsFilmLike = IsLikelyFilm(r)
                })
                .OrderByDescending(x => x.Score)
                .ToList();

            Console.WriteLine($"\n  ── L1 Results ──");
            Console.WriteLine($"  {"#",-3} {"Scr",-5} {"Film?",-6} {"ID",-16} {"Title",-45} {"S",-3} {"Ep",-5}");
            foreach (var (item, idx) in scored.Take(7).Select((x, i) => (x, i)))
            {
                var mark = tc.ExpectedSeriesId != null && item.Item.Id == tc.ExpectedSeriesId ? " ◄" : "";
                Console.WriteLine($"  {idx + 1,-3} {item.Score,-5} {(item.IsFilmLike ? "YES" : "no"),-6} " +
                    $"{item.Item.Id,-16} {Trunc(item.Item.Title ?? "", 45),-45} " +
                    $"{item.Item.SeasonCount,-3} {item.Item.EpisodeCount,-5}{mark}");
            }

            // ═══ STEP 2: L1-Direct ═══
            var directHit = scored.FirstOrDefault(x => x.Score >= MappingSensitivity && x.IsFilmLike);
            if (directHit != null)
            {
                Console.WriteLine($"\n  🎬 Step 2 (L1-Direct): {directHit.Item.Id} \"{directHit.Item.Title}\" (score={directHit.Score})");
                Evaluate(tc, directHit.Item.Id, null, "L1-Direct", ref passed, ref partial, ref failed);
                Console.WriteLine();
                continue;
            }
            Console.WriteLine($"\n  Step 2: No L1-Direct hit");

            // ═══ STEP 3: L2-Cascade with Distinctive Word Scoring ═══
            var l2Candidates = scored
                .Where(x => x.Score >= L2MinScore)
                .Take(L2MaxCandidates)
                .ToList();

            Console.WriteLine($"\n  ── Step 3: L2-Cascade ({l2Candidates.Count} candidates) ──");

            // Collect ALL film-like season matches across all candidates
            var allMatches = new List<L2Match>();
            var movieWords = normalizedMovie
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet();

            foreach (var cand in l2Candidates)
            {
                Console.WriteLine($"\n    → \"{Trunc(cand.Item.Title ?? "", 50)}\" " +
                    $"({cand.Item.Id}, scr={cand.Score}{(cand.IsFilmLike ? ", FILM" : "")})");

                await Task.Delay(80);
                var seasons = await GetSeasons(httpClient, cand.Item.Id);
                if (seasons == null || seasons.Count == 0)
                {
                    Console.WriteLine("      (no seasons)");
                    continue;
                }

                // Distinctive words: movie words NOT present in this series' title
                var seriesWords = NormalizeName(cand.Item.Title ?? "")
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .ToHashSet();
                var distinctiveWords = movieWords.Except(seriesWords).ToHashSet();

                // Check each film-like season
                var filmSeasons = seasons.Where(s => s.NumberOfEpisodes <= 2).ToList();

                if (filmSeasons.Count == 0)
                {
                    var topSeason = seasons.OrderByDescending(s =>
                        CalculateMatchScore(normalizedMovie, NormalizeName(s.Title))).First();
                    Console.WriteLine($"      No film seasons. Top: \"{Trunc(topSeason.Title, 50)}\" ({topSeason.NumberOfEpisodes} eps)");
                    continue;
                }

                foreach (var season in filmSeasons)
                {
                    var normalizedSeason = NormalizeName(season.Title);
                    int titleScore = CalculateMatchScore(normalizedMovie, normalizedSeason);

                    // Distinctive word bonus: how many distinctive words appear in the season title?
                    var seasonWordSet = normalizedSeason
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .ToHashSet();
                    int distinctiveHits = distinctiveWords.Count(dw =>
                        seasonWordSet.Any(sw => sw.Contains(dw) || dw.Contains(sw)));
                    int bonus = distinctiveWords.Count > 0
                        ? (int)((float)DistinctiveWordBonus * distinctiveHits / distinctiveWords.Count)
                        : 0;

                    int combined = titleScore + bonus;

                    Console.WriteLine($"      ep={season.NumberOfEpisodes} title={titleScore,-3} +dist={bonus,-3} ={combined,-3} " +
                        $"\"{Trunc(season.Title, 55)}\"" +
                        (distinctiveWords.Count > 0 ? $"  [{distinctiveHits}/{distinctiveWords.Count} dist]" : ""));

                    if (combined >= L2SeasonMinScore)
                    {
                        allMatches.Add(new L2Match(cand, season, titleScore, bonus, combined));
                    }
                }
            }

            // Pick BEST L2 match across all candidates
            var bestL2 = allMatches
                .OrderByDescending(m => m.CombinedScore)
                .ThenByDescending(m => m.DistinctiveBonus) // Prefer matches with distinctive words
                .FirstOrDefault();

            if (bestL2 != null)
            {
                Console.WriteLine($"\n  🎬 Step 3 (L2-Cascade): \"{Trunc(bestL2.Season.Title, 60)}\"");
                Console.WriteLine($"     Series={bestL2.Candidate.Item.Id} → Season={bestL2.Season.Id}");
                Console.WriteLine($"     title={bestL2.TitleScore} + dist_bonus={bestL2.DistinctiveBonus} = {bestL2.CombinedScore}");
                if (allMatches.Count > 1)
                {
                    Console.WriteLine($"     ({allMatches.Count} total candidates, picked best combined score)");
                }
                Evaluate(tc, bestL2.Candidate.Item.Id, bestL2.Season.Title, "L2-Cascade", ref passed, ref partial, ref failed);
                Console.WriteLine();
                continue;
            }

            Console.WriteLine($"\n  Step 3: L2-Cascade exhausted ({l2Candidates.Count} series tried)");

            // ═══ STEP 4: L1-Film-Fallback ═══
            var fallback = scored.FirstOrDefault(x => x.IsFilmLike && x.Score >= L2MinScore);
            if (fallback != null)
            {
                Console.WriteLine($"\n  🎬 Step 4 (L1-Fallback): {fallback.Item.Id} \"{fallback.Item.Title}\" (score={fallback.Score})");

                // Try to also find the best season within this fallback series
                var fbSeasons = await GetSeasons(httpClient, fallback.Item.Id);
                if (fbSeasons != null && fbSeasons.Count > 0)
                {
                    var fbSeriesWords = NormalizeName(fallback.Item.Title ?? "")
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                    var fbDistinctive = movieWords.Except(fbSeriesWords).ToHashSet();

                    var bestFbSeason = fbSeasons
                        .Where(s => s.NumberOfEpisodes <= 2)
                        .Select(s =>
                        {
                            var normS = NormalizeName(s.Title);
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
                        Console.WriteLine($"     → Best season: \"{Trunc(bestFbSeason.Season.Title, 55)}\" (combined={bestFbSeason.Combined})");
                        Evaluate(tc, fallback.Item.Id, bestFbSeason.Season.Title, "L1-Fallback+L2", ref passed, ref partial, ref failed);
                        Console.WriteLine();
                        continue;
                    }
                }

                Evaluate(tc, fallback.Item.Id, null, "L1-Fallback", ref passed, ref partial, ref failed);
                Console.WriteLine();
                continue;
            }

            Console.WriteLine($"\n  Step 4: No film-like fallback");
            Console.WriteLine($"  ❌ FAIL — no match through any strategy");
            failed++;
            Console.WriteLine();
        }

        // ═══ Summary ═══
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  📊 RESULTS                                                             ║");
        Console.WriteLine("╠═══════════════════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  ✅ Passed:   {passed,-3}                                                          ║");
        Console.WriteLine($"║  ⚠️  Partial:  {partial,-3}                                                          ║");
        Console.WriteLine($"║  ❌ Failed:   {failed,-3}                                                          ║");
        Console.WriteLine($"║  ⏭️  Skipped:  {skipped,-3}  (geo-blocked)                                           ║");
        Console.WriteLine($"║  Total:     {TestCases.Count,-3}                                                          ║");
        Console.WriteLine("╠═══════════════════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  L1-Direct → L2-Cascade (distinctive words) → L1-Fallback               ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════════════════╝");
    }

    // ─── Evaluation ───

    private static void Evaluate(
        MovieTestCase tc, string foundSeriesId, string? foundSeasonTitle,
        string strategy, ref int passed, ref int partial, ref int failed)
    {
        bool seriesMatch = tc.ExpectedSeriesId != null && foundSeriesId == tc.ExpectedSeriesId;

        bool seasonMatch;
        if (tc.ExpectedSeasonHint == null)
            seasonMatch = true;
        else if (foundSeasonTitle != null)
            seasonMatch = NormalizeName(foundSeasonTitle).Contains(NormalizeName(tc.ExpectedSeasonHint));
        else
            seasonMatch = false;

        if (seriesMatch && seasonMatch)
        {
            Console.WriteLine($"  ✅ CORRECT ({strategy})");
            passed++;
        }
        else if (seriesMatch && !seasonMatch)
        {
            Console.WriteLine($"  ⚠️  Series OK but wrong season ({strategy}) — hint \"{tc.ExpectedSeasonHint}\"");
            partial++;
        }
        else if (tc.ExpectedSeriesId == null)
        {
            Console.WriteLine($"  ✅ Found ({strategy})");
            passed++;
        }
        else
        {
            Console.WriteLine($"  ⚠️  Wrong series: {foundSeriesId} (expected {tc.ExpectedSeriesId}) ({strategy})");
            partial++;
        }
    }

    // ─── API ───

    private static async Task<List<SearchResultItem>?> SearchSeries(HttpClient httpClient, string query)
    {
        try
        {
            var url = $"{BaseUrl}/content/v2/discover/search?q={Uri.EscapeDataString(query)}&n=10&type=series&locale=pt-BR";
            var response = await httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var results = new List<SearchResultItem>();

            if (doc.RootElement.TryGetProperty("data", out var dataArr))
            {
                foreach (var bucket in dataArr.EnumerateArray())
                {
                    if (!bucket.TryGetProperty("items", out var items)) continue;
                    foreach (var item in items.EnumerateArray())
                    {
                        int sc = 0, ec = 0;
                        if (item.TryGetProperty("series_metadata", out var meta))
                        {
                            sc = meta.TryGetProperty("season_count", out var scv) ? scv.GetInt32() : 0;
                            ec = meta.TryGetProperty("episode_count", out var ecv) ? ecv.GetInt32() : 0;
                        }
                        results.Add(new SearchResultItem
                        {
                            Id = item.GetProperty("id").GetString() ?? "",
                            Title = item.TryGetProperty("title", out var t) ? t.GetString() : null,
                            Slug = item.TryGetProperty("slug_title", out var s) ? s.GetString() : null,
                            SeasonCount = sc,
                            EpisodeCount = ec
                        });
                    }
                }
            }
            return results;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Search error: {ex.Message}");
            return null;
        }
    }

    private static async Task<List<SeasonInfo>?> GetSeasons(HttpClient httpClient, string seriesId)
    {
        try
        {
            var url = $"{BaseUrl}/content/v2/cms/series/{seriesId}/seasons?locale=pt-BR";
            var response = await httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var seasons = new List<SeasonInfo>();

            if (doc.RootElement.TryGetProperty("data", out var dataArr))
            {
                foreach (var item in dataArr.EnumerateArray())
                {
                    seasons.Add(new SeasonInfo
                    {
                        Id = item.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                        Title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                        SeasonSequenceNumber = item.TryGetProperty("season_sequence_number", out var ssn) ? ssn.GetInt32() : 0,
                        NumberOfEpisodes = item.TryGetProperty("number_of_episodes", out var ne) ? ne.GetInt32() : 0,
                        SeriesId = seriesId,
                    });
                }
            }
            return seasons;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"      Seasons error: {ex.Message}");
            return null;
        }
    }

    // ─── Heuristics ───

    private static bool IsLikelyFilm(SearchResultItem item)
    {
        if (item.SeasonCount <= 1 && item.EpisodeCount <= 1) return true;
        if (item.SeasonCount > 1 && item.EpisodeCount <= item.SeasonCount * 2) return true;
        if (item.EpisodeCount <= 3) return true;
        return false;
    }

    // ─── Name Normalization (handles /, Unicode dashes, parentheses) ───

    private static string NormalizeName(string name)
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

    // ─── Match Scoring (from CrunchyrollSeriesProvider, PR #8 fix) ───

    private static int CalculateMatchScore(string search, string candidate)
    {
        if (candidate.Contains(search))
            return 100 - (candidate.Length - search.Length);
        if (search.Contains(candidate))
            return 100 - (search.Length - candidate.Length);

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

    // ─── Auth ───

    private static async Task<string?> GetAnonymousToken(HttpClient httpClient)
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
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("access_token").GetString();
        }
        catch { return null; }
    }

    // ─── Helpers ───

    private static string Trunc(string s, int max) =>
        s.Length <= max ? s : s[..(max - 3)] + "...";

    // ─── Models ───

    public class SearchResultItem
    {
        public string Id { get; set; } = "";
        public string? Title { get; set; }
        public string? Slug { get; set; }
        public int SeasonCount { get; set; }
        public int EpisodeCount { get; set; }
    }

    public class SeasonInfo
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public int SeasonSequenceNumber { get; set; }
        public int NumberOfEpisodes { get; set; }
        public string SeriesId { get; set; } = "";
    }

    public class ScoredResult
    {
        public SearchResultItem Item { get; set; } = new();
        public int Score { get; set; }
        public bool IsFilmLike { get; set; }
    }

    public record L2Match(
        ScoredResult Candidate,
        SeasonInfo Season,
        int TitleScore,
        int DistinctiveBonus,
        int CombinedScore
    );
}
