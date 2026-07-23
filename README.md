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

Example with environment variables:

```bash
Analyzer__SteamCmdPath=/path/to/steamcmd \
Analyzer__PowDifficulty=4 \
dotnet run --project src/GmodAddonAnalyzer --launch-profile http
```