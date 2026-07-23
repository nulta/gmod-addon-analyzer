# 04 — Data Model

SQLite via EF Core. The DB holds **metadata only** — never file contents.
Extracted file bodies live on disk under `artifacts/{addonId}/extracted/`.

> **Implementation note (differs from original plan):** all timestamp columns
> use `DateTime` (UTC) rather than `DateTimeOffset`. The Microsoft.EntityFrameworkCore.Sqlite
> provider cannot translate `DateTimeOffset` in `ORDER BY` clauses, which
> broke the History/Report pages. UTC `DateTime` is unambiguous and orders
> correctly. Stored values are always `DateTime.UtcNow`.

## Entities

### `AnalysisRun` — one submission event (drives History page)

| field            | type           | notes                                              |
|------------------|----------------|----------------------------------------------------|
| Id               | long (PK)      | auto-increment                                      |
| AddonId          | long           | Steam Workshop publishedfileid                      |
| SubmittedAt      | DateTime (UTC) | when the user submitted                             |
| Status           | enum           | `Pending`/`Fetching`/`Extracting`/`Analyzing`/`Done`/`FetchFailed`/`ExtractFailed` |
| ErrorMessage     | string?        | last steamcmd/log lines on failure                  |
| SubmittedByIp    | string?        | for basic abuse audit (last octet zeroed)           |
| PowNonce         | long           | the PoW nonce that was verified                     |
| Addon            | Addon?         | nav (FK Restrict)                                   |

A given `(AddonId, Addon.UpdatedAt)` pair maps to **one** `Addon` row and may
have many `AnalysisRun` rows (one per re-submission). Re-submission of an
already-analyzed-and-current addon short-circuits to the existing `Addon`
without re-fetching.

### `Addon` — the analyzed addon at a point in time

| field              | type           | notes                                          |
|--------------------|----------------|------------------------------------------------|
| Id                 | long (PK)      | auto-increment                                  |
| WorkshopId         | long           | indexed                                          |
| Title              | string(512)    | from workshop metadata                          |
| UpdatedAt          | DateTime (UTC) | workshop "time updated" — the cache key         |
| LastFetchedAt      | DateTime (UTC) | when we last ran SteamCMD                       |
| PackedSizeBytes    | long           | size of the .gma/.legacy.bin                    |
| ExtractedSizeBytes | long           | recursive size of extracted tree                |
| ModelCount         | int            | .mdl                                            |
| MaterialCount      | int            | .vmt + .vtf                                     |
| SoundCount         | int            | .wav/.mp3/.ogg                                  |
| LuaCount           | int            | .lua (also .lua.txt for gmod legacy)            |
| ModelSizeBytes     | long           |                                                 |
| MaterialSizeBytes  | long           |                                                 |
| SoundSizeBytes     | long           |                                                 |
| LuaSizeBytes       | long           |                                                 |
| Files              | List<AddonFile>| nav (one per extracted file)                    |

Composite uniqueness: `(WorkshopId, UpdatedAt)`. Re-fetching an addon whose
workshop `UpdatedAt` hasn't changed is a cache hit.

### `AddonFile` — one file in the extracted tree

| field          | type           | notes                                          |
|----------------|----------------|------------------------------------------------|
| Id             | long (PK)      |                                                |
| AddonId        | long (FK)      | → Addon.Id                                      |
| RelativePath   | string(512)    | path under `extracted/`, normalized `/`-joined  |
| Extension      | string(16)     | lowercased, no dot                              |
| SizeBytes      | long           |                                                |
| Category       | enum FileCategory | `Model`/`Material`/`Sound`/`Lua`/`Map`/`Other`  |
| LuaAnalysis    | LuaAnalysis?   | nav (set only when Category == Lua)             |

Index on `(AddonId, RelativePath)` unique. (Counts are denormalized on `Addon`,
so no separate `(AddonId, Category)` index was needed.)

### `LuaAnalysis` — result of static-analyzing one Lua file (1:1 to AddonFile)

| field              | type           | notes                                          |
|--------------------|----------------|------------------------------------------------|
| Id                 | long (PK)      |                                                |
| AddonFileId        | long (FK, uniq)| → AddonFile.Id                                  |
| Realm              | enum Realm     | `Client`/`Server`/`Shared`/`Unknown`            |
| Kind               | enum LuaKind   | `Entity`/`Swep`/`Snpc`/`Tool`/`Effect`/`Util`/`Unknown` |
| RiskLevel          | enum RiskLevel | `None`/`Low`/`Medium`/`High`                    |
| RiskSignals        | string(2048)   | JSON array of signal ids, e.g. `["RunString","GlobalIndex"]` |
| HooksDeclared      | string(2048)   | JSON array of hook names from `hook.Add`       |
| HooksCalled        | string(2048)   | JSON array of distinct names from `hook.Call`/`hook.Run` |
| TimersCreated      | int            | count of `timer.Create` + `timer.Simple`       |
| NetMessagesSent    | string(2048)   | JSON array of string names from `util.AddNetworkString` |
| NetReceivers       | int            | count of `net.Receive` blocks                  |
| Lines              | int            | source line count (info only)                  |

JSON arrays are stored as `string` columns (hand-rolled `ToJsonArray` in
`LuaAnalyzer`, not `System.Text.Json` — the values are simple string lists and
a few lines of escaping is cheaper than a serializer for this). Capped at 200
distinct entries per list to bound column size.

## Relationships at a glance

```
AnalysisRun ──*──> Addon ──*──> AddonFile ──1──> LuaAnalysis?
```

## Migrations

- One initial migration. New columns later → new migration, never edit.
- `dotnet ef migrations add Initial` after entities are wired up.
- Dev: `EnsureCreated`-style fast path is fine; for prod use real migrations.
  We'll use migrations everywhere to avoid drift.