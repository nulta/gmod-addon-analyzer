# 06 — Addon Analysis

Whole-addon statistics. Produced by `AddonAnalyzer` in the same single pass
that `ArtifactStore` uses to index the extracted tree (see
[05-addon-pipeline.md](05-addon-pipeline.md)).

## Inputs

- The extracted addon directory: `{DataRoot}/artifacts/{addonId}/extracted/`
- Workshop metadata already fetched for the cache check:
  `Title`, `UpdatedAt`.

## Outputs (written onto the `Addon` entity)

| field              | source                                            |
|--------------------|---------------------------------------------------|
| Title              | workshop metadata                                 |
| UpdatedAt          | workshop metadata                                 |
| PackedSizeBytes    | `FileInfo.Length` of the `.gma`/`.legacy.bin`     |
| ExtractedSizeBytes | recursive sum of all extracted files              |
| ModelCount / Size  | files where `Category == Model`                   |
| MaterialCount/Size | files where `Category == Material`                |
| SoundCount / Size  | files where `Category == Sound`                   |
| LuaCount / Size    | files where `Category == Lua`                     |

## File categorization — single table, data-only

`FileCategories.cs` holds a `Dictionary<string, FileCategory>` mapping
lowercased extension → category. No logic, just data:

| Extension(s)                                | Category  |
|---------------------------------------------|-----------|
| `mdl`, `vvd`, `vtx`, `phy`, `ani`, `sw vtx` | Model     |
| `vmt`, `vtf`                                | Material  |
| `wav`, `mp3`, `ogg`                         | Sound     |
| `lua`, `lua.txt`                            | Lua       |
| `bsp`, `bsp.gz`, `nav`, `ain`               | Map       |
| everything else                             | Other     |

A file with no extension → `Other`. The "extension" is computed once as
`path.Substring(path.LastIndexOf('.')+1).ToLowerInvariant()` with the
`.lua.txt` two-extension case handled explicitly.

## Why denormalize counts on `Addon`?

The report page shows them at the top, always. Aggregating from `AddonFile`
rows every render is a needless join + group-by. The numbers are immutable
for a given `(WorkshopId, UpdatedAt)` so denormalizing is free and correct.

## What this service does **not** do

- It does not open or parse file bodies. Sizes + extension only.
- It does not look at Lua — that's `LuaAnalyzer` (07).
- It does not compute a hash of the addon. Not needed (workshop id +
  UpdatedAt is the identity).

## Test plan (in `tests/`)

- Build a fake extracted tree in a temp dir with known files; assert
  counts/sizes match.
- Edge cases: empty addon, deeply nested dirs, `.lua.txt`, no-extension
  files, files > 2GB (long, not int).