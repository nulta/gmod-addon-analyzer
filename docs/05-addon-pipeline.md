# 05 — Addon Pipeline (Fetch & Extract)

The `AnalysisPipeline` service orchestrates: **fetch → extract → analyze**.
It is invoked by the `/analyze` endpoint (via an in-process
`Channel<AnalysisRequest>` + `AnalysisPipelineHostedService`, see
[02-architecture.md](02-architecture.md)) after PoW is verified.

## 0. Metadata probe + cache check (inside the pipeline)

`WorkshopService.GetMetadataAsync` calls the keyless Steam endpoint
`ISteamRemoteStorage/GetPublishedFileDetails/v1/` (POST with
`itemcount=1&publishedfileids[]=N`) to get `title`, `time_updated`, and
`file_size`. This is a tiny HTTP call, much cheaper than SteamCMD, and runs
once at the top of `AnalysisPipeline.RunAsync`.

The returned `UpdatedAt` is the cache key: a unique index on
`(WorkshopId, UpdatedAt)` means re-analyzing an addon whose workshop
`time_updated` hasn't changed reuses the existing `Addon` row (the pipeline
replaces its `AddonFile`/`LuaAnalysis` children in place rather than
re-downloading). If the metadata probe fails (network error, private item),
the pipeline proceeds anyway and synthesizes `UpdatedAt = UtcNow` so a fresh
fetch always happens.

> The original plan proposed running the cache check synchronously on the
> submit request to short-circuit before enqueueing. In practice the probe
> is fast enough that doing it inside the pipeline keeps the request handler
> non-blocking and the cache logic in one place. If latency becomes an
> issue, hoist it back into `/analyze`.

## 1. Fetch — `FetchService`

Responsibilities:
- Ensure SteamCMD is installed under `{DataRoot}/steamcmd/` (download
  `https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip` on first
  run, extract, `chmod +x` on Unix, mark ready via `Volatile`).
- Serialize all calls through `static SemaphoreSlim(1,1)` — SteamCMD is not
  safe to run concurrently against the same working dir.
- Write a SteamCMD `+runscript` file to `{DataRoot}/logs/cmd-…txt`:
  ```
  @ShutdownOnFailedCommand 1
  @NoPromptForPassword 1
  login anonymous
  force_install_dir {DataRoot}/artifacts/{addonId}/packed
  workshop_download_item 4000 {addonId}
  quit
  ```
  (`4000` is Garry's Mod app id.)
- Run SteamCMD with `RedirectStandardOutput/Error`, tee logs to `ILogger`
  and a `{DataRoot}/logs/{addonId}-{timestamp}.log` file.
- On non-zero exit: throw `FetchException(exitCode, tailLog)`; pipeline
  records `Status=FetchFailed`.

Output: a path to the downloaded file under `…/packed/`, typically
`ugc_{addonId}.gma` or a `.legacy.bin`.

> Why anonymous login: GMod workshop addons for a public app id are
> downloadable anonymously. If we ever need a private addon we'd need a
> real account + 2FA flow — out of scope.

## 2. Extract — `ExtractService`

GMod addon formats we handle, in priority order:

### a) `.gma` (Garry's Mod Addon, LZMA-compressed)

The GMA format is documented and small. We implement a **pure-C# reader**
(no native deps):

```
GMAHeader
  magic  "GMAD"
  format version (1 byte, currently 3)
  steamid (8) · timestamp (8) · required content (null-term strings) · name (null-term) ·
  description (null-term) · author (null-term) · addon version (4)
File entries (loop):
  filenum (4)  [0 ⇒ end]
  name (null-term string, e.g. "models/foo.mdl")
  size (8) · crc32 (4)
  body = next `size` bytes (may be LZMA-compressed — see below)
CRC32 footer (4) · addon metadata (JSON, optional trailing)
```

Compression: the file body may be:
- **uncompressed** (just `size` raw bytes), or
- **LZMA-compressed** — detected by `size == 0` plus a following LZMA
  header? In practice current GMAD always stores raw file bytes and only
  compresses the **whole GMA stream** is a myth — each file body is raw.

  → We implement the simple/raw path first and add LZMA-per-file only if
  a real-world addon needs it. Keep it boring.

We stream each entry out to
`{DataRoot}/artifacts/{addonId}/extracted/{entry.name}`.

### b) `.legacy.bin` (old BSP-packaged addons)

These are essentially `gzip` + `tar` (or LZMA-compressed tar) of a legacy
addon folder. Detect by magic bytes:
- `1f 8b` → gzip → unwrap, then inspect inner.
- LZMA header (`5d 00 00 ?? ??`) → LZMA-decode, then treat as tar.

We use `System.IO.Compression.GZipStream` for gzip and a tiny LZMA decoder
(SharpCompress or a 100-line pure decoder) for the LZMA case, then a
minimal tar reader. Tar is a 512-byte-header-per-file format; a ~60-line
reader suffices.

> Pragmatic call: pull in `SharpCompress` for tar+lzma. It's a single
> well-known dep and saves us writing a tar+lzma stack by hand. See
> [10-tech-conventions.md](10-tech-conventions.md).

### c) Unknown / neither

→ `Status=ExtractFailed` with a clear message.

## 3. Index — `ArtifactStore.IndexAsync(addon)`

After extraction, walk the extracted tree once and:

- compute total `ExtractedSizeBytes`,
- per-file: `RelativePath`, `Extension`, `SizeBytes`, `Category`,
- insert `AddonFile` rows,
- call `AddonAnalyzer` and `LuaAnalyzer` (see 06, 07) to fill the rest.

This is a single sequential pass so we don't re-walk the tree N times.

## Pipeline state machine (stored on `AnalysisRun.Status`)

```
Pending ──► Fetching ──► Extracting ──► Analyzing ──► Done
   │           │             │              │
   └───────────┴─────────────┴──────────────┴──► FetchFailed / ExtractFailed
```

The hosted pipeline worker updates `Status` after each stage so the
status-poll endpoint can render progress.

## Idempotency & re-runs

- If a user submits an addon that is mid-flight (any non-terminal `Status`),
  the request attaches to the existing `AnalysisRun` rather than starting
  a new fetch.
- Terminal `FetchFailed`/`ExtractFailed` runs may be retried — that creates
  a **new** `AnalysisRun` (history preserved).
- Re-analyzing a `Done` addon with unchanged `UpdatedAt` is a cache hit:
  new `AnalysisRun(Status=Done)` pointing at the existing `Addon`.