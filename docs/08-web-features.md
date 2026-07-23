# 08 — Web Features

Server-rendered Razor Pages. A sprinkle of vanilla JS for: PoW computation
in a Web Worker, tree-expand/collapse, copy-permalink. No SPA framework.

## Pages

### `Index` (home, `/`)

- Big input: addon ID (numeric; validate).
- "Analyze" button → triggers a JS PoW worker (see
  [09-maintenance-antispam.md](09-maintenance-antispam.md)). On success,
  POSTs `{ addonId, nonce, difficulty }` to `/analyze`.
- Below: recent History (last ~10 submissions, server-rendered) so the
  page isn't empty on first load.

### `Analyze` (`/analyze`, POST-only)

Handler:
1. Validate addon id is a positive long.
2. `PowService.Verify(addonId, nonce, difficulty)` → reject if invalid
   (HTTP 400 with "bad proof of work").
3. Cheap workshop metadata probe (Web API). If cache hit → redirect to
   `Report/{addonId}`.
4. Else: enqueue `AnalysisRequest` onto the in-process `Channel`,
   create `AnalysisRun(Status=Pending)`, redirect to
   `/report/{addonId}?run={runId}` (the report page polls while pending).

### `Report` (`/report/{addonId}`)

Top-level analysis for an addon:
- Title, workshop id, UpdatedAt, LastFetchedAt.
- Packed vs. extracted size (humanized: `1.2 MB`).
- Counts/sizes table: Models / Materials / Sounds / Lua / Other.
- Risk summary: # of Lua files at each RiskLevel, top-5 riskiest files
  (link to Viewer).
- Realm breakdown: client/server/shared counts.
- Hooks declared across the addon (top N most common, link to a filter).
- Status banner while `Status != Done` (page polls `/status/{runId}`
  every ~2s and reloads when Done).
- Side rail: "Browse files" → `Explorer/{addonId}`; "Permalink" copy
  button; "Re-analyze" (forces a new run, ignores cache); "Delete
  artifacts" (Admin).

### `Explorer` (`/explorer/{addonId}`)

Directory tree of the extracted addon. Two implementations, pick one:

- **(chosen) Server-rendered tree** via a recursive partial that walks
  `AddonFile` rows grouped by path segments. Each file row links to
  `/viewer/{addonId}?path=…`. Expand/collapse is pure CSS/JS on
  `<details>`-style elements — no JSON needed.
- Lazy load option: if an addon has >5000 files, render only top level
  and fetch subtrees via a minimal API `/api/tree/{addonId}?dir=…`
  returning a small JSON list. Keep this as a future knob; default to
  "render all" unless perf demands otherwise.

### `Viewer` (`/viewer/{addonId}?path=…`)

Single-file viewer.
- Header: relative path, size, category, `LuaAnalysis` summary if Lua.
- For Lua: render source with **server-side syntax highlighting** using
  `ColorCode`-style lib (or a tiny Lua-aware highlighter). Line numbers.
- For text-ish files (.vmt, .txt, .json): plain `<pre>` with line numbers.
- For binary (.mdl/.vtf/.wav): show metadata only (size, no preview).
  Optionally a download link to `/api/file/{addonId}?path=…` (streams
  bytes; gated by same-origin + a per-request token if abuse shows up).
- Permalink: the URL **is** the permalink. A "Copy link" button copies
  `https://{host}/viewer/{addonId}?path={urlencodedPath}`.

### `History` (`/history`)

Paginated list of `AnalysisRun` rows: date, addon id, title, status,
link to Report. 50/page. Simple.

### `Admin` (`/admin`)

- List of all `Addon` rows with extracted artifact presence + age.
- Per-addon: "Delete artifacts" button (calls `ArtifactStore.Delete`,
  sets a flag on `Addon` so the report page shows "artifacts removed;
  metadata retained"). DB rows stay (so History still works); only the
  on-disk extracted tree + packed file are removed.
- "Delete everything" (metadata + artifacts) — secondary confirmation.
- "Run cleanup now" button — triggers `CleanupService` immediately.

## Permalink design

`/viewer/{addonId}?path=models/foo.mdl`

- `{addonId}` is the **Workshop id** (not our internal `Addon.Id`) so
  links are stable across our re-imports and meaningful to outsiders.
  Route constraint `{addonId:long}`.
- `path` is the URL-encoded relative path under `extracted/`.
- Resolution: `AddonFile` by `(Addon.WorkshopId == addonId, RelativePath == path)`.
  If artifacts have been deleted (TTL or manual), the page shows the
  metadata we have + a "artifacts no longer on disk" notice (no source
  body), still 200 — permalinks don't 404 just because we cleaned up.

## Minimal API endpoints (in `Web/Endpoints/`)

- `POST /analyze` — described above.
- `GET  /status/{runId}` → `{ status, stage }` for polling.
- `GET  /api/tree/{addonId}?dir=…` → lazy subtree (optional, see Explorer).
- `GET  /api/file/{addonId}?path=…` → raw bytes for download (optional).
- `POST /admin/delete/{addonId}` → delete artifacts (CSRF token).

## Layout / styling

- One `_Layout.cshtml`, one `site.css`. Minimal, readable, light + dark
  via `prefers-color-scheme`. No CSS framework (or a tiny one like
  Pico.css if we want a head start — single CSS file, classless).
- `<details>`/`<summary>` for collapsibles (tree, risk detail) — no JS
  needed for core functionality.