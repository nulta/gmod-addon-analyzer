# 10 — Tech Choices & Conventions

## Stack

| Concern              | Choice                                              | Why                                          |
|----------------------|-----------------------------------------------------|----------------------------------------------|
| Runtime              | .NET 10 (LTS)                                       | current LTS as of build; `net10.0` TFM       |
| Web framework        | ASP.NET Core, Razor Pages + minimal API endpoints   | server-rendered, simple; no SPA              |
| ORM                  | EF Core 9 (Design + Sqlite packages), **SQLite**     | zero-ops DB, single file; 9.x pins cleanly   |
| Migrations           | `dotnet-ef` 9.0.0 (global tool)                     | standard; auto-applied at startup            |
| Logging              | `ILogger` + console                                 | built-in, no Serilog needed at this size     |
| HTTP (workshop API)  | `HttpClient` via `IHttpClientFactory` ("workshop")  | standard, pooled                              |
| GMA parsing          | **hand-written async reader**, `ExtractService.cs`  | format is tiny; no dep; streams file bodies  |
| tar / gzip (legacy)  | `System.IO.Compression` + hand-rolled tar reader     | BCL only; LZMA-per-file not yet encountered  |
| Syntax highlighting  | plain `<pre>` for now                               | M7 may add ColorCode; not blocking           |
| PoW hashing (server) | `System.Security.Cryptography.SHA256`               | BCL                                           |
| PoW hashing (client) | inlined sync SHA-256 in Web Worker (`pow.js`)        | `crypto.subtle` is async; sync loop is faster|
| CSS                  | hand-rolled `site.css` (~140 lines, dark mode)       | small enough; no framework needed            |
| JS                   | vanilla + Web Worker for PoW                         | no build step, no npm                         |
| Tests                | xUnit + `WebApplicationFactory`                     | idiomatic .NET; no FluentAssertions dep yet  |

Explicitly **not** used: Blazor, SignalR (we poll), MediatR, AutoMapper,
SharpCompress (BCL covers gzip; tar reader is ~60 lines), Hangfire/Quartz
(a `Timer` in a hosted service is enough), Docker compose (single-process),
any SPA framework, any CSS framework.

## Conventions

### Code

- C# 13 (`net10.0` default), `nullable` enabled, `implicit usings` on.
- `file`-scoped classes for small helpers; one type per file otherwise.
- `sealed` by default; open for inheritance only when there's a reason.
- **No interfaces for services** — all services are concrete classes
  registered directly in DI. Tests inject fakes by pointing them at a temp
  directory or by constructing with `NullLogger`/test options. EF tests use
  the real SQLite provider against a per-test temp `DataRoot`.
- File-scoped namespaces. `var` everywhere the type is obvious.
- **No comments** unless something is genuinely non-obvious. The docs in
  `docs/` are where the "why" lives; code shows the "how".
- Async: suffix async methods with `Async` and return `Task`/`Task<T>`.
  No `async void` outside event handlers.
- Primary constructors used where they collapse boilerplate (page models,
  `AnalyzerDbContext`, services with simple dep injection).

### Names

- Services: `XyzService` (concrete class). Methods: verb-noun
  (`FetchAsync`, `IndexAsync`, `AnalyzeFileAsync`).
- Entities: plain nouns (`Addon`, `AddonFile`, `LuaAnalysis`).
- Razor Pages: noun-or-verb per page (`Index`, `Report`, `Viewer`).
- DB columns: PascalCase (SQLite is case-insensitive for ASCII by default,
  so PascalCase columns are fine and keep C# ↔ SQL trivial). Enums stored
  as ints via EF's default enum mapping; rendered as names in the UI.

### Layout / DI

- All DI registration in `Program.cs` — one place, top-down readable.
  No `IXyzConfiguration` bootstrap classes.
- Options pattern: one `AnalyzerOptions` class bound manually from
  `builder.Configuration.GetSection("Analyzer")` and registered as a
  singleton. No `IOptions<T>` indirection — we want the values at construction
  time (e.g. `ArtifactStore` needs `DataRoot` immediately).
- No "Application/Domain/Infrastructure" folders-as-layers; see
  [03-project-structure.md](03-project-structure.md).

### Git

- `.gitignore` for `bin/`, `obj/`, `data/` (runtime artifacts), VS/Rider.
- Commit message: imperative, short subject; match the repo style once
  there is one. No Conventional Commits mandate unless the user wants it.

### Testing

- Unit tests for `LuaAnalyzer` (table-driven — the bulk of the value, 30+ cases).
- Unit tests for `AddonAnalyzer` against a temp-dir fixture (`TempDir` helper).
- Unit test for `ExtractService` against a hand-built minimal `.gma` fixture.
- Unit tests for `PowService` (issue/verify/reuse/wrong-id).
- Integration tests via `WebApplicationFactory<Program>`: smoke tests for
  every page route + PoW challenge/analyze endpoints. Each test class uses a
  unique temp `DataRoot` and a low `PowDifficulty` so PoW is instant.
- No UI tests. Pages are thin; the smoke + endpoint tests cover rendering.
- **44 tests total, all green.**

## Config (`appsettings.json`)

```jsonc
{
  "Analyzer": {
    "DataRoot": "./data",
    "SteamCmdPath": null,            // null → auto-install under DataRoot/steamcmd
    "WorkshopAppId": 4000,
    "WebApiKey": "",                 // reserved; metadata probe is keyless
    "PowEnabled": true,              // (not yet read at runtime — PoW always on)
    "PowDifficulty": 16,
    "CleanupTtlDays": 5,
    "CleanupIntervalHours": 6,
    "ChallengeTtlMinutes": 5
  },
  "Logging": { /* … */ },
  "AllowedHosts": "*"
}
```

All overridable by environment variables (`Analyzer__DataRoot=…`) or per-test
via `WebApplicationFactory.WithWebHostBuilder(b => b.UseSetting("Analyzer:…", …))`.

All overridable by environment variables (`Analyzer__DataRoot=…`).