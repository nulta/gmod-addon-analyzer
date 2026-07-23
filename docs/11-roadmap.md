# 11 — Roadmap / Milestones

Build order, smallest-to-largest, each milestone independently runnable.

> **Status as of build:** M0–M7 complete, 51/51 tests green, all page routes
> return 200. Real SteamCMD run remains a manual-verification item (needs
> network + the binary on the host).

## M0 — Skeleton ✅

- `dotnet new razor` → `src/GmodAddonAnalyzer` (net10.0).
- `Program.cs`: register `AnalyzerOptions`, EF Core (SQLite),
  `AnalyzerDbContext`, auto-apply migrations at startup.
- `AnalyzerDbContext` + entities (one `Entities.cs`) + **initial migration**.
- `Index`/`History`/`Admin`/`Report` stubs + `_Layout.cshtml` + hand-rolled
  `site.css` (dark mode via `prefers-color-scheme`).
- `tests/GmodAddonAnalyzer.Tests` with `WebApplicationFactory` smoke test.

**Done:** `dotnet run` serves `/`; `dotnet test` green.

## M1 — Data + PoW ✅

- `PowService` (SHA-256 leading-zero-bits, issue/verify, in-memory challenge
  store with TTL) + `GET /pow/challenge` (rate-limited).
- `POST /analyze` verifies PoW, creates `Pending` `AnalysisRun`, redirects
  to `/report/{id}?run=…`.
- `Index` page: vanilla-JS Web Worker with inlined sync SHA-256 (`pow.js`).
- `History` page listing runs; `Report` polls `/status/{runId}`.

**Done:** user submits an id → pending run appears → History lists it.

## M2 — Fetch + Extract ✅

- `FetchService`: auto-installs SteamCMD, `SemaphoreSlim`-serialized,
  `+runscript` invocation, log capture.
- `ExtractService`: hand-written **async** `.gma` reader (v1–v3, streams
  file bodies without buffering) + gzip→tar legacy path (BCL only).
- `WorkshopService`: keyless `ISteamRemoteStorage/GetPublishedFileDetails`
  metadata probe for the cache key.
- `AnalysisPipeline` + `AnalysisPipelineHostedService` consuming
  `Channel<AnalysisRequest>`; status state machine
  Pending→Fetching→Extracting→Analyzing→Done (or Fetch/ExtractFailed).
- `GET /status/{runId}` for polling; `Report` auto-reloads via JS.

**Done (build):** pipeline compiles + unit-tested against a fixture `.gma`.
Real SteamCMD run is a manual-verification item (needs network + binary).

## M3 — Addon analysis ✅

- `AddonAnalyzer`: single-pass tree walk → `AddonFile` rows +
  denormalized counts/sizes on `Addon`.
- `FileCategories`: extension → category data table (incl. `.lua.txt`).
- `Report` page renders the numbers.

**Done:** Report shows counts/sizes for models/materials/sounds/lua.

## M4 — Lua analysis ✅

- `LuaHeuristics` static table + `LuaAnalyzer` loop (one file, ~150 lines).
- Realm from path; kind from `SWEP`/`ENT`/`TOOL`/`EFFECT` tables;
  risk = max matched signal; comments stripped before matching;
  hooks/timers/net tracked; lists capped at 200.
- `LuaAnalysis` rows written; `Report` shows riskiest-files table.
- 30+ table-driven `LuaAnalyzerTests`.

**Done:** Lua files tagged with realm/kind/risk/signals; tests cover all signals.

## M5 — Explorer + Viewer + permalinks ✅

- `Explorer` page: server-rendered tree (HTML built in C# to avoid Razor
  recursive-helper limits), `<details>`-based collapse.
- `Viewer` page: file metadata + Lua analysis summary + source in `<pre>`
  (highlighting deferred to M7); "Copy permalink" button.
- Permalinks are `GET /viewer/{workshopId}?path=…` and survive artifact
  cleanup (metadata-only render when files are gone).

**Done:** browse + read any file; shareable URL.

## M6 — Maintenance ✅

- `CleanupService`: 5-day TTL sweep + per-addon manual delete; zeros size
  fields but keeps `AddonFile`/`LuaAnalysis` rows so permalinks still resolve.
- `CleanupHostedService`: runs on startup + every 6h.
- `Admin` page: addon list with age/artifact-presence, per-row delete,
  "Run sweep now" button (anti-forgery protected).

**Done:** sweep + manual delete wired and smoke-tested.

## M7 — Hardening ✅ (core items; iterative items remain)

- `PowEnabled` gate honored end-to-end: `/pow/challenge` returns
  `{ seed: "disabled", difficulty: 0 }` when off; `/analyze` skips
  verification; the JS worker short-circuits to direct submit.
- Dedicated `NotFound` page + `UseStatusCodePagesWithRedirects("/NotFound")`.
- Lua syntax highlighting: tiny regex-based lexer (`LuaHighlighter`,
  ~40 lines) wrapping keywords/strings/comments/numbers/identifiers in
  `<span class="lua-*">`; CSS added; HTML escaped. Non-Lua text files
  still render as escaped `<pre>`.
- Empty states already on History/Admin/Explorer/Report/Viewer.
- SteamCMD log tail is already surfaced on `FetchFailed` (in `<pre>`).

### Iterative / optional items still open

- Verify `.lua.txt` handling against real addons (logic is in place;
  untested against real-world dual-extension files).
- PoW difficulty + rate-limit tuning against real traffic.
- `/api/file` raw download with range support.
- Lazy subtree endpoint for addons with >5000 files.
- Real SteamCMD end-to-end run (manual verification).

## Out of scope, explicitly

- User accounts, auth.
- Live Lua execution / sandboxing.
- Multi-addon batch.
- Redis / external queue / separate worker process.
- Docker packaging (a single `dotnet run` is fine; add a Dockerfile later
  if deploying needs it).