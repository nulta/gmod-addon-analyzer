# Garry's Mod Addon Analyzer

> [!warning]
> **This project is entirely written using generative AI.** May contain serve issues or bugs. Use at your own risk.

A simple ASP.NET web application that fetches a Garry's Mod Steam Workshop addon via SteamCMD, extracts it, and analyzes its contents (files, sizes, Lua code structure, security heuristics).

## Prerequisites

- .NET 10 SDK
- SteamCMD -- optional. If not provided, it will be auto-installed.

## How to run

```bash
# 1. Build
dotnet build

# 2. Run (optionally point to your SteamCMD install + lower PoW difficulty for dev)
dotnet run --project src/GmodAddonAnalyzer --launch-profile http
```

The app starts at `http://localhost:5249`.

### Configuration

All settings are optional and can be set via environment variables (`Analyzer__<Key>=value`) or `appsettings.json`:

| Setting                | Default      | Description                                      |
|------------------------|--------------|--------------------------------------------------|
| `Analyzer:DataRoot`    | `./data`     | Where the SQLite DB, artifacts, logs, SteamCMD live |
| `Analyzer:SteamCmdPath`| `null`       | Path to a folder containing `steamcmd.exe`. If null, auto-installs under `DataRoot/steamcmd` |
| `Analyzer:PowDifficulty`| `16`        | SHA-256 leading-zero-bits required. Lower = easier (use `4` for dev) |
| `Analyzer:PowEnabled`  | `true`       | Set `false` to skip proof-of-work entirely        |
| `Analyzer:CleanupTtlDays`| `5`        | Extracted artifacts auto-removed after this many days |
| `Analyzer:CleanupIntervalHours`| `6`  | How often the cleanup sweeper runs               |
| `Analyzer:RunTtlDays`  | `30`         | `AnalysisRun` rows trimmed after this many days  |
| `Analyzer:MaxQueuedAnalyses`| `100`   | Bounded analysis queue depth (extra submits get 503) |
| `Analyzer:MaxPendingAnalysisRuns`| `100` | Cap on non-terminal DB rows (restart backstop) |
| `Analyzer:ResubmitCooldownMinutes`| `30` | Min delay before the same addon is re-analyzed |
| `Analyzer:TrustProxy`  | `false`      | Trust `X-Forwarded-For`/`-Proto` from loopback proxies |
| `Analyzer:KnownProxies`| `[]`         | Additional trusted proxies (IP or CIDR entries)  |

Example with environment variables:

```bash
Analyzer__SteamCmdPath=/path/to/steamcmd \
Analyzer__PowDifficulty=4 \
dotnet run --project src/GmodAddonAnalyzer --launch-profile http
```

## Production deployment

The app does **not** terminate TLS. Run a reverse proxy in front (nginx/Caddy/
traefik) and keep the container port loopback-bound — `docker-compose.yml`
publishes `127.0.0.1:5249:8080` by default for exactly this reason. Exposing
the port publicly serves plaintext HTTP, which exposes the admin password in
login POST bodies.

When running behind that proxy:

- Set `Analyzer__TrustProxy=true` **only** if the proxy sends
  `X-Forwarded-For`/`X-Forwarded-Proto`, and list the proxy explicitly, e.g.
  `Analyzer__KnownProxies__0=172.18.0.5` (single IP) or
  `Analyzer__KnownProxies__0=10.0.0.0/8` (CIDR). Getting this wrong either
  merges all clients into one rate-limit bucket (a self-DoS) or lets clients
  spoof `X-Forwarded-For` to bypass rate limits and admin lockouts.
- Set `Analyzer__AdminPassword`; left empty, the admin page is fail-closed
  (disabled).
- Proof-of-work difficulty is a *speed bump* against casual abuse — a native
  client solves SHA-256 at millions per second, so the per-IP rate limiter is
  the real control. Keep both on.
- Per-run rows are trimmed after `RunTtlDays`; extracted artifacts after
  `CleanupTtlDays`. Addon metadata rows are kept so report permalinks resolve.