# Jellyfin Broadcast

Turns your Jellyfin library into a single always-on virtual TV channel. See `PRD.md` for the
full spec, `CONTEXT.md` for domain terminology, and `docs/adr/` for the architectural decisions
behind this implementation.

## Build

Requires the .NET 8+ SDK (targets `net9.0`, matching Jellyfin 10.11.x).

```sh
dotnet build
dotnet test
```

## Install (manual)

1. `dotnet build -c Release`
2. Copy `src/Jellyfin.Plugin.Broadcast/bin/Release/net9.0/Jellyfin.Plugin.Broadcast.dll` into your
   Jellyfin server's plugin directory, in its own subfolder, e.g.:
   `<jellyfin-data-dir>/plugins/Broadcast_0.1.0.0/Jellyfin.Plugin.Broadcast.dll`
3. Restart Jellyfin.
4. That's it — V1 ships with a default "All Day Movies" block (all libraries, no filters) and needs
   no configuration. Go to **Dashboard → Plugins → Broadcast** to grab the M3U/XMLTV/Stream URLs, or
   click **Regenerate Schedule Now** (it also runs automatically: daily, and on library changes).

## Using the channel

- `GET /Broadcast/Channel/Current` — what's airing now + playback offset
- `GET /Broadcast/Channel/Next` — what's up next
- `GET /Broadcast/Channel/Schedule` — the full generated schedule
- `POST /Broadcast/Channel/Regenerate` — regenerate immediately
- `GET /Broadcast/m3u` — IPTV playlist (paste into a player, or add as a Jellyfin Live TV M3U tuner)
- `GET /Broadcast/xmltv` — EPG guide data for the above

All endpoints require normal Jellyfin authentication (`api_key` query param or `X-Emby-Token`
header) — there's no anonymous access, by design (see PRD non-goals).

## Hardening applied

- Concurrent regeneration (manual button + daily task + auto-trigger) is guarded — a regeneration
  already in progress causes other callers to skip rather than run in parallel.
- A single malformed Programming Block (bad time format, inverted year range, duplicate name,
  negative Cooldown) is rejected and logged, not allowed to crash the whole regeneration.
- An invalid configured TimeZone falls back to UTC (logged) instead of throwing.
- `MovieHistory` is pruned on every regeneration (keeps `max(2x largest configured Cooldown, 90 days)`)
  so it doesn't grow unbounded on a long-running server.
- SQLite runs in WAL mode with a busy-timeout, so concurrent API reads and a background
  regeneration write don't hit "database is locked" errors.
- `/Broadcast/Channel/Stream`'s `api_key` passthrough is URL-encoded (was a header-injection/
  open-redirect vector) and no longer forces `static=true`, so Jellyfin can transcode incompatible
  content instead of failing outright.

## Known limitations (V1)

- **Pinned to Jellyfin 10.11.8.** Jellyfin's plugin loader binds `MediaBrowser.*` assemblies by exact
  version with no redirects, so this plugin only loads on the exact server version it was built
  against. Running a different Jellyfin patch version requires rebuilding with matching
  `Jellyfin.Controller`/`Jellyfin.Model` package versions in the `.csproj`.


- Transitions between Programs are not seamless — see ADR 0002. Best experience is through
  Jellyfin's own apps polling `/Broadcast/Channel/Current`; generic IPTV players using the M3U's
  redirect endpoint work but may show a brief interruption between items.
- Filters: Genre/Tags/Rating/Year-range/Favorite/Library are wired; Studio/Director/Actor/
  Collection/Runtime/Resolution/custom metadata are not yet (see `Scheduling/RulesEngine.cs`).
- `LeastPlayed`/`NeverPlayed`/non-Rating `WeightedRandom` don't apply to *which series* an Episode
  block picks next (falls back to uniform random) — see `Scheduling/ScheduleGenerator.cs`.
- Never run inside a live Jellyfin server — verified against the compiled SDK API surface and unit
  tests only. Run a real smoke test (install, configure a block, regenerate, hit the endpoints)
  before relying on this in production.
- No plugin repository manifest/zip packaging or CI pipeline yet.
