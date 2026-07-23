# 09 — Maintenance & Anti-Spam

## A. Auto-remove extracted artifacts after 5 days

### `CleanupService`

- `CleanupAsync(CancellationToken)`:
  - For each `Addon` whose `ExtractedSizeBytes > 0` and
    `LastFetchedAt < UtcNow - 5 days`:
      - `ArtifactStore.DeleteArtifacts(addon)` → rm the
        `artifacts/{addonId}/packed` and `artifacts/{addonId}/extracted`
        directories.
      - Set `Addon.PackedSizeBytes = 0`, `Addon.ExtractedSizeBytes = 0`,
        and zero out the per-category sizes (counts remain, since those
        are metadata and cheap).
      - Leave `AddonFile` and `LuaAnalysis` rows in place — that's the
        point of permalinks (see 08: a permalink still resolves to
        metadata even after artifacts are gone).
- `DeleteArtifactsAsync(addonId)` — same as above but for a single addon
  (used by the Admin page's manual delete).

### `CleanupHostedService`

- `IHostedService` with a `Timer` running `CleanupAsync` every **6 hours**
  (configurable). Doesn't need to be precise; 5 days is a soft TTL.
- Also runs once on startup (so a long-offline instance catches up).
- Logs how many addons it cleaned.

### Configurability

`AnalyzerOptions.CleanupTtlDays = 5` (default), `CleanupIntervalHours = 6`.
Override via `appsettings.json` or env vars. No UI for it.

## B. Manual removal

- `Admin` page lists every `Addon` with artifact presence + age. Each row
  has a "Delete artifacts" button (`POST /admin/delete/{addonId}`).
- Confirmation via a plain `confirm()` dialog in JS — no need for a
  second page. The POST is CSRF-protected (ASP.NET anti-forgery in Razor
  Pages is on by default).
- A second "Delete all data" button per row removes the `Addon` and its
  `AddonFile`/`LuaAnalysis` rows too (cascade in EF model). For the
  "oops I shouldn't have analyzed this" case.

## C. Anti-spam: hash proof-of-work

SteamCMD is expensive (network, disk, ~seconds-minutes). We don't want
random clients hammering submit. A hash PoW is enough — no accounts, no
rate-limit infra, no captchas.

### `PowService`

```
Issue(addonId: long) -> PowChallenge
   seed   = random 16 hex chars (per challenge)
   target = AnalyzerOptions.PowDifficulty (default: leading zero bits, see below)
   createdAt = now
   return { seed, addonId, difficulty, createdAt }

Verify(addonId, seed, nonce) -> bool
   if seed unknown or expired (5 min window) → false
   if addonId doesn't match seed → false
   h = SHA256($"{seed}:{addonId}:{nonce}")
   return LeadingZeroBits(h) >= difficulty
```

### Difficulty

- Default **16 leading zero bits** (≈ 65k tries average). On a modern
  browser via a Web Worker that's a fraction of a second; on a
  botnet/abuser it's a real cost at scale.
- Configurable via `AnalyzerOptions.PowDifficulty`. We can bump it later
  if abuse appears; it's just a settings change.
- Per-addon-id is bound into the hash so a solved challenge can't be
  reused for a different addon.

### Client side (`wwwroot/js/pow.js`, Web Worker)

```js
// worker receives { seed, addonId, difficulty }
// loops nonce from 0, hashing `${seed}:${addonId}:${nonce}` with a
// hand-rolled synchronous SHA-256 (inlined), checking leading zero bits.
// postMessage({ nonce, attempts }) when found.
```

`crypto.subtle` is async and would force an `await` per nonce — too slow in a
tight PoW loop. Instead the worker uses a self-contained sync SHA-256
implementation (~80 lines, no deps). This keeps the loop tight and the worker
trivial to audit. The worker source is inlined as a `Blob` in `pow.js` so
there's no separate file to serve.

### Flow on `Index`

1. User types addon id.
2. On blur, JS fetches `GET /pow/challenge?addonId=…` → `{ seed, addonId, difficulty }`.
3. "Analyze" button is enabled once a challenge is cached for the current id.
4. Click spawns the worker; UI shows "Solving proof-of-work…".
5. On solution, JS `POST /analyze` with `{ addonId, seed, nonce }`. The
   endpoint verifies, creates a `Pending` `AnalysisRun`, writes to the
   pipeline `Channel`, and redirects to `/report/{addonId}?run={runId}`.
6. The report page polls `/status/{runId}` every 2 s and reloads on
   `Done`/`FetchFailed`/`ExtractFailed`.

### Server-side rate limit (light, optional belt-and-suspenders)

- `GET /pow/challenge` rate-limited per IP via
  `Microsoft.AspNetCore.RateLimiting` (built-in, .NET 7+). E.g. 10
  challenge issuances / minute / IP. PoW itself is the main guard; this
  just stops someone issuing millions of challenges.
- `POST /analyze` also rate-limited per IP (e.g. 5 / minute). PoW makes
  even this overkill, but cheap to keep.

### Failure modes

- Expired/invalid PoW → HTTP 400 `{ error: "bad_proof" }`. JS re-issues
  a challenge and retries once.
- Disabled: `AnalyzerOptions.PowEnabled = false` → `/analyze` skips
  verification. Useful for local dev; **off by default in prod**.