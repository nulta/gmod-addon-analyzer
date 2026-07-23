# 02 — Architecture

## Shape

A single ASP.NET Core web application (`src/GmodAddonAnalyzer`). It is both the
UI and the backend. Hosting is just `dotnet run`. Everything talks to a local
SQLite database via EF Core.

Three conceptual layers, each a thin folder of services — **not** separate
projects. Keeping them in one project keeps the surface small and avoids
assembly ceremony that a small app doesn't need.

```
┌──────────────────────────────────────────────────────────────────┐
│  Web (Razor Pages + minimal API for viewer JSON)                 │
│   Pages: Home, Analyze, Report, Explorer, Viewer, History, Admin │
└───────────────┬──────────────────────────────────────────────────┘
                │ depends on
┌───────────────▼──────────────────────────────────────────────────┐
│  Services (application layer)                                    │
│   FetchService      — SteamCMD invocation                         │
│   ExtractService    — .gma / .legacy.bin parser                   │
│   AddonAnalyzer     — file-type counts & sizes                    │
│   LuaAnalyzer       — per-Lua-file static analysis                │
│   ArtifactStore     — on-disk extracted addon repository          │
│   CleanupService    — TTL-based deletion + manual delete          │
│   PowService        — hash proof-of-work issuance/verification    │
└───────────────┬──────────────────────────────────────────────────┘
                │ depends on
┌───────────────▼──────────────────────────────────────────────────┐
│  Data (EF Core, SQLite)                                          │
│   AnalyzerDbContext, entities (Addon, AddonFile, LuaAnalysis…)    │
└──────────────────────────────────────────────────────────────────┘
```

## Key design rules

- **Pipelines are services, not controllers.** A Razor Page handler calls a
  service method. Pages never touch SteamCMD or the filesystem directly.
- **Sync boundary = the database.** Services that produce durable data write
  to EF Core entities. Transient large data (extracted files) lives on disk,
  indexed by `AddonId` — the DB holds only metadata, never file contents.
- **One job = one service.** `FetchService` only fetches. `ExtractService`
  only extracts. `AddonAnalyzer` and `LuaAnalyzer` are independent of each
  other and can run in parallel after extraction.
- **Idempotent analysis.** Re-analyzing an already-extracted addon must not
  re-download it. Cache by `(addonId, updatedAt)`.
- **No background worker process.** The fetch+extract+analyze pipeline runs
  in a `HostedService` task spawned by the Analyze page so we don't need a
  separate queue infrastructure. The page polls status via a small endpoint.
  (If this ever gets heavy, swap in a real queue — but not yet, KISS.)

## Concurrency

- SteamCMD invocations are **serialized** behind a single
  `SemaphoreSlim(1,1)` in `FetchService`. SteamCMD is not safe to run
  concurrently against the same working directory.
- Multiple users can still browse/re-analyze already-extracted addons in
  parallel; only the actual fetch is gated.

## Failure handling

- SteamCMD exit code != 0 → record `Addon.Status = "FetchFailed"` with the
  last log lines; surface on the report page.
- Extraction parse error → `Status = "ExtractFailed"`.
- Re-submission of an addon currently being fetched → return "in progress",
  do not start a second fetch.

## Where things live on disk

```
{DataRoot}/
  analyzer.db
  artifacts/
    {addonId}/
      packed/      ← raw .gma / .legacy.bin from SteamCMD
      extracted/   ← addon tree (models/, materials/, lua/, sound/, ...)
  steamcmd/        ← SteamCMD install (auto-downloaded on first run)
  logs/            ← per-fetch SteamCMD logs
```

`DataRoot` defaults to `./data` and is configurable via `DataRoot` setting.