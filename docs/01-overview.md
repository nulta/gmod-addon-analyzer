# 01 — Overview & Goals

## What it does

A single, small ASP.NET web app that, given a Steam Workshop addon ID (a number),
produces a structured analysis of that Garry's Mod addon:

1. **Fetch** the addon via SteamCMD.
2. **Extract** the downloaded `.gma` (or `.legacy.bin`) into a real addon directory tree.
3. **Analyze the addon** as a whole:
   - last-updated date (from Steam Workshop metadata)
   - packed file size vs. extracted file size
   - counts and per-file sizes for models (`.mdl`), materials (`.vmt`/`.vtf`), sounds (`.wav`/`.mp3`/`.ogg`)
4. **Analyze Lua files** individually:
   - realm tag (client / server / shared)
   - entity / SWEP / SENT / SNPC / tool classification
   - dynamic-execution risk signals (`RunScript`, `_G[...]`, `CompileString`, `loadstring`, `os.execute`, ...)
   - declared hooks (`hook.Add`, `hook.Call`)
   - timers (`timer.Create`, `timer.Simple`)
   - net messages (`util.AddNetworkString`, `net.Receive`)
   - ...etc.
5. **Browse** the extracted addon (directory explorer + Lua source viewer).
6. **Permalink** to any addon / file for sharing.
7. **History** of previously submitted addons.
8. **Self-maintenance**: auto-remove extracted artifacts older than 5 days; manual removal button.
9. **Anti-spam**: hash-based proof-of-work before kicking off a fetch (SteamCMD is expensive).

## Non-goals (explicitly out of scope)

- User accounts / authentication.
- Running or sandboxing the Lua. Static analysis only.
- Bulk/queue of many addons at once (one at a time per submission).
- Modifying addons.
- A heavy SPA framework. Server-rendered Razor pages + a little HTMX/alpine where helpful is enough.

## Principles

- **SRP**: each service/component does one thing. Don't build a god "AddonService".
- **KISS**: no abstractions until a second concrete implementation forces them. No plugin systems.
- **Readability over cleverness**: prefer boring, explicit code. LINQ over custom DSLs.
- **Small surface**: one web project + one test project. No solution-folder ceremony.
- **Dependency-minimal**: use BCL + a few well-known libraries (see [10-tech-conventions.md](10-tech-conventions.md)).

## User-facing flow

```
[Home]
  └─ enter addon ID ─┐
                    ▼
           [PoW challenge] ── hash cash ──┐
                                         ▼
                              [enqueue SteamCMD fetch]
                                         ▼
                                  [extract .gma]
                                         ▼
                              [analyze addon + lua]
                                         ▼
                              [Analysis report page]
                                  ├─ addon explorer
                                  ├─ lua viewer
                                  └─ permalink / share
  └─ [History list] ── re-visit previous analyses
  └─ [Maintenance: cleanup / delete]
```