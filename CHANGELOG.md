# Changelog

## [2.1.0.0] - 2026-02-07

### 🚀 New Features

- **EnsureBrowserAliveAsync**: Plugin now automatically creates and maintains a FlareSolverr session to keep Chrome alive before any CDP operation. No more "no Chrome port found" errors after FlareSolverr restarts.
- **CDP Episode Fetching**: When the scraping cache misses, `GetEpisodesAsync` now falls back to `TryGetEpisodesViaApiProxyAsync` (CDP proxy) instead of returning empty results. Episodes are fetched through Chrome just like seasons.
- **Chrome CDP URL Config**: New advanced plugin setting to override the auto-detected Chrome DevTools Protocol URL.

### 🐛 Bug Fixes

- **Session cleanup improved**: `DestroySessionAsync` now uses a fresh 5-second `CancellationTokenSource` instead of the caller's token, preventing `TaskCanceledException` warnings during plugin shutdown.
- **Dispose reliability**: `Dispose()` now synchronously awaits session destruction before disposing the HttpClient.
- **Season matching enhanced**: `FindMatchingSeasonByNumber` now has 3-tier fallback: (1) match by `SeasonSequenceNumber`, (2) match by `SeasonNumber`, (3) for single-season series, use the only available season for Season 1 regardless of numbering.

### 📚 Documentation

- **FlareSolverr is now documented as required** — Added prominent notice to both README.md and README.pt-BR.md
- **New [FLARESOLVERR.md](FLARESOLVERR.md)**: Complete installation and configuration guide (bilingual EN/PT-BR) covering Docker setup, Docker Compose, socket permissions, plugin configuration, troubleshooting, and network diagram.

### 🔧 Internal

- Added detailed debug logging for season details in `TryGetSeasonsViaApiProxyAsync` (Id, Title, SeasonNumber, SeasonSequenceNumber, AudioLocales, NumberOfEpisodes)
- CDP session state tracked via static fields (`_cdpSessionActive`, `_cdpSessionId`, `_cdpSessionLock`) with proper thread safety

## [2.0.0.0] - 2026-02-06

### ⚡ Breaking Changes

- **New architecture**: All Crunchyroll API calls now go through Chrome DevTools Protocol (CDP) via FlareSolverr
- **Requires FlareSolverr running as Docker container** (docker exec is used to run scripts inside it)
- **Requires `websocket-client` Python package** inside the FlareSolverr container (`pip install websocket-client`)
- New plugin config option: `DockerContainerName` (default: "flaresolverr")

### 🚀 New Features

- **CDP-based Cloudflare bypass**: Executes `fetch()` inside Chrome's browser context within FlareSolverr, completely bypassing Cloudflare's TLS fingerprinting (JA3/JA4)
- **Anonymous auth via CDP**: Obtains Bearer tokens through Chrome without needing user credentials for metadata access
- **Generic `CdpFetchJsonAsync`**: Reusable method for any authenticated API call through Chrome's context
- **Token caching**: CDP auth tokens cached for 50 minutes (expire at 60min) to minimize overhead

### 🐛 Bug Fixes

- **Critical: Season mapping fix** — `EpisodeMappingService` now uses `season_sequence_number` instead of `season_number` for `JellyfinSeasonNumber`. Crunchyroll sets `season_number=1` for ALL seasons within a series; `season_sequence_number` gives the real order (1, 2, 3...). This fixes Frieren Season 2 being mapped as a duplicate Season 1.
- **Added `SeasonSequenceNumber` to `CrunchyrollEpisode` model** — Episodes now correctly carry the season sequence info from the API
- **Fixed season display names** — Search results now show `Season 2: Title` instead of `Season 1: Title` for second seasons
- **Improved episode offset calculation** — Falls back to all episodes when no episodes match the `SeasonNumber` filter (common with CR's `season_number=1` for all seasons)

### 🏗️ Architecture

- `FlareSolverrClient.cs`: Complete rewrite — added `GetAuthTokenViaCdpAsync()`, `CdpFetchJsonAsync()`, `ExecuteCdpJsAsync()` with embedded Python CDP script
- `CrunchyrollApiClient.cs`: Added `TryAuthenticateViaFlareSolverrAsync()` (CDP-first), `TryGetSeasonsViaApiProxyAsync()`, `TryGetEpisodesViaApiProxyAsync()` — all using CDP fetch
- All providers updated to pass `DockerContainerName` from plugin configuration
- FlareSolverr GET with custom headers confirmed NOT working (Cloudflare rejects document navigation with Bearer tokens) — CDP fetch is the only reliable path

## [1.5.1.2] - 2026-02-06

### Fixed

- Fixed typo in folder name (`SheduledTasks` → `ScheduledTasks`)
- Fixed missing `await` in `SaveItemAsync`
- Restored stable season mapping logic (reverted potentially unstable FlareSolverr scraping changes)

## [1.5.1.1] - 2026-02-05

### Added

- Episode maturity ratings support
- Minimum score threshold (70%) for series matching

### Fixed

- Season matching specific fix using SeasonSequenceNumber
- Episodes now preserve Jellyfin's IndexNumber for compatibility
