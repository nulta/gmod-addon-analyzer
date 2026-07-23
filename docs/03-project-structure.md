# 03 — Project Structure

One solution, two projects. Boring and small.

```
gmod-addon-analyzer/
├─ GmodAddonAnalyzer.slnx
├─ README.md
├─ .gitignore
├─ docs/                         ← these planning docs
│  └─ 01-overview.md … 11-roadmap.md
├─ src/
│  └─ GmodAddonAnalyzer/
│     ├─ GmodAddonAnalyzer.csproj
│     ├─ Program.cs              ← DI registration, middleware, Kestrel
│     ├─ appsettings.json
│     ├─ appsettings.Development.json
│     │
│     ├─ Data/
│     │  ├─ AnalyzerDbContext.cs
│     │  ├─ Entities/
│     │  │  └─ Entities.cs           (all entities + enums in one file:
│     │  │                              AnalysisRun, Addon, AddonFile,
│     │  │                              LuaAnalysis, AnalysisStatus, ...)
│     │  └─ Migrations/              (EF Core migrations, auto-applied)
│     │
│     ├─ Services/
│     │  ├─ FetchService.cs          (SteamCMD auto-install + download, serialized)
│     │  ├─ ExtractService.cs        (.gma / gzip+tar legacy → tree, async)
│     │  ├─ AddonAnalyzer.cs         (file-type & size statistics)
│     │  ├─ FileCategories.cs        (extension → category data table)
│     │  ├─ LuaAnalyzer.cs           (per-file Lua static analysis + heuristics)
│     │  ├─ ArtifactStore.cs         (disk paths, size, delete)
│     │  ├─ WorkshopService.cs       (Steam metadata probe for cache key)
│     │  ├─ CleanupService.cs        (5-day TTL sweep + manual delete)
│     │  ├─ PowService.cs            (hash PoW issue/verify)
│     │  └─ AnalysisPipeline.cs      (orchestrates fetch→extract→analyze)
│     │
│     ├─ Pages/                      (Razor Pages — no separate Web/ folder)
│     │  ├─ Shared/_Layout.cshtml
│     │  ├─ _ViewImports.cshtml
│     │  ├─ Index.cshtml(.cs)        (home: submit addon ID + PoW)
│     │  ├─ Report.cshtml(.cs)       (addon-level analysis, polls /status)
│     │  ├─ Explorer.cshtml(.cs)     (directory tree, HTML built in C#)
│     │  ├─ Viewer.cshtml(.cs)       (single file viewer; Lua source + metadata)
│     │  ├─ History.cshtml(.cs)      (list of prior submissions)
│     │  ├─ Admin.cshtml(.cs)        (cleanup / delete / sweep)
│     │  └─ Error.cshtml(.cs)
│     │
│     ├─ Web/
│     │  └─ Endpoints/
│     │     └─ StatusEndpoints.cs    (minimal API: /pow/challenge, /analyze, /status)
│     │
│     ├─ Hosting/
│     │  ├─ AnalysisPipelineHostedService.cs   (consumes Channel<AnalysisRequest>)
│     │  └─ CleanupHostedService.cs            (startup + interval TTL sweep)
│     │
│     ├─ Configuration/
│     │  └─ AnalyzerOptions.cs        (strongly-typed settings, no IOptions<T>)
│     │
│     ├─ Properties/launchSettings.json
│     └─ wwwroot/
│        ├─ css/site.css             (hand-rolled, dark mode via prefers-color-scheme)
│        ├─ js/site.js               (placeholder)
│        └─ js/pow.js                (PoW worker + inlined sync SHA-256)
│
└─ tests/
   └─ GmodAddonAnalyzer.Tests/
      ├─ GmodAddonAnalyzer.Tests.csproj
      ├─ SmokeTests.cs               (WebApplicationFactory: every page route)
      ├─ PowServiceTests.cs          (issue/verify/reuse/wrong-id)
      ├─ PowEndpointTests.cs         (/pow/challenge, /analyze bad-proof)
      ├─ AddonAnalyzerTests.cs       (tree index, accumulation, .lua.txt)
      ├─ LuaAnalyzerTests.cs         (realm, kind, risk, hooks, timers, net, lines)
      └─ GmaExtractorTests.cs        (hand-built minimal .gma fixture)
```

### Deviations from the original plan (and why)

- **Pages live in `Pages/` directly, not `Web/Pages/`.** The Razor Pages
  SDK expects them at `Pages/` relative to the project root; nesting under
  `Web/` would require custom `ConfigurePages` + `RazorPagesOptions` and
  gains nothing. `Web/` now only holds `Endpoints/`.
- **All entities live in one file (`Entities.cs`).** They're small (≤15
  properties each) and tightly coupled by navigation. Five tiny files vs.
  one cohesive file — the file is more readable.
- **`LuaHeuristics` is a `static class` inside `LuaAnalyzer.cs`, not a
  separate file.** The heuristics table and the analyzer loop reference
  each other and read best side-by-side.
- **No `IExternalMappers` / no interfaces at all.** Every service is a
  concrete `sealed` class registered in DI. See [10-tech-conventions.md](10-tech-conventions.md).

## Why one web project (not 3+ projects)

- The whole app fits in ~15 service files. Splitting into `Domain`/`Application`/`Infrastructure`/`Web` projects buys us nothing but extra `using`s and a more annoying refactor when something moves.
- **Folders are the boundaries.** `Services` only depends on `Data`; pages depend on `Services`. Enforced by code review + tests, not the compiler. That's fine at this size.
- If the app grows, splitting is a mechanical move later — and the folder structure already maps 1:1 to future projects.

## Why two projects (web + tests)

Tests are genuinely a separate build output and need their own TFM/fixtures. That's worth a project.