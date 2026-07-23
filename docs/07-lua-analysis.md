# 07 — Lua Analysis

Static, pattern-based analysis of each `.lua` file. **No execution, no
sandboxing, no Lua interpreter.** We scan source text for known signals and
tag the file.

Produced by `LuaAnalyzer`, called once per `AddonFile` where
`Category == Lua`. Result is one `LuaAnalysis` row (see
[04-data-model.md](04-data-model.md)).

## Design rule: **data is separate from logic**

`LuaHeuristics.cs` is a static table of patterns. `LuaAnalyzer.cs` is the
single loop that applies them. Adding a new check = adding a row to the
table, not writing new code. This keeps the analyzer tiny and the surface
for "did we forget to check X" obvious.

## Heuristic table shape

```csharp
public sealed record LuaSignal(
    string Id,                 // stable id, stored in RiskSignals JSON
    string Display,            // human label for the report
    RiskLevel Risk,            // None|Low|Medium|High
    SignalKind Kind,           // Realm|Kind|Hook|Timer|Net|Risk
    string Regex,              // applied to source (Multiline, IgnoreCase where noted)
    string? CaptureGroup);     // if set, captured value is collected into a list
```

## Realm detection (`Realm`)

GMod convention: file path, not contents, decides realm.

| Path pattern                          | Realm   |
|---------------------------------------|---------|
| `lua/autorun/client/*`                | Client |
| `lua/autorun/server/*`                | Server |
| `lua/autorun/*` (not client/server)   | Shared |
| `lua/weapons/*`, `lua/entities/*`, `lua/effects/*` | Shared (loaded by gmod on both) |
| `lua/vgui/*`                          | Client |
| `lua/includes/modules/*`              | Shared |
| anything else                         | Unknown |

Additionally scan for explicit markers as a **secondary** signal:
- contains `if SERVER then` only and no `if CLIENT then` → leans Server
- contains `if CLIENT then` only → leans Client
- both → Shared

Path wins; markers only fill in `Unknown`.

## Kind detection (`Kind`)

| Signal (in source)                        | Kind   |
|-------------------------------------------|--------|
| `DEFINE_BASECLASS`, `"BaseClass"`… + `SWEP` table assignment | Swep |
| `ENT.Type = "anim"` etc. + `ENT.` table | Entity (sent) |
| `ENT.Type` containing `"ai"`             | Snpc   |
| `TOOL.* = ` in `lua/weapons/gmod_tool/`  | Tool   |
| `EFFECT.* =`                             | Effect |
| file path `lua/weapons/*` not gmod_tool  | Swep   |
| none of the above                         | Util/Unknown |

## Risk signals (dynamic execution / unsafe patterns)

The `RiskLevel` of a file = the **max** `Risk` among matched signals.

| Id               | Pattern (illustrative)                         | Risk   |
|------------------|------------------------------------------------|--------|
| `RunString`      | `\bRunString\s*\(`                             | High   |
| `RunScript`      | `\bRunScript\s*\(`                             | High   |
| `CompileString`  | `\bCompileString\s*\(`                         | High   |
| `LoadString`     | `\bloadstring\s*\(`                            | High   |
| `GlobalIndex`    | `_G\s*\[\s*["']`                               | Medium |
| `GlobalIndexVar` | `_G\s*\[\s*[A-Za-z_]`                          | Medium |
| `OsExecute`      | `\bos\.execute\s*\(`                           | High   |
| `IoOpen`         | `\bio\.(?:open|popen|lines)\s*\(`             | High   |
| `RunConsole`     | `\bRunConsoleCommand\s*\(`                    | Low    |
| `HttpPost`       | `\bhttp\.(?:Post|Fetch)\s*\(`                 | Low    |
| `Require`        | `\brequire\s*\(\s*["']`                       | Low    |
| `Detour`         | `local\s+(\w+)\s*=\s*\1\b` (re-assign hook)  | Medium |
| `EvalLua`        | `\b(?:Run|Compile)\b.*\bstring\b` (loose)     | Medium |

Each entry in `LuaHeuristics.cs` carries the **exact** regex; this table is
just documentation. New signal = new row, no logic change.

## Hook tracking

| Field           | Source                                                         |
|-----------------|----------------------------------------------------------------|
| `HooksDeclared` | `hook\.Add\s*\(\s*["']([^"']+)["']` → capture group 1, distinct |
| `HooksCalled`   | `hook\.(?:Call|Run)\s*\(\s*["']([^"']+)["']` → capture group 1 |

We collect **distinct** names (case-sensitive; GMod hooks are
case-sensitive) and store as JSON array. Cap the array at e.g. 200 entries
to prevent pathological files from bloating the column.

## Timers

`TimersCreated` = count of matches of
`timer\.(?:Create|Simple)\s*\(`. Number only — cheap and stable.

## Net messages

- `NetMessagesSent` = distinct captures from
  `util\.AddNetworkString\s*\(\s*["']([^"']+)["']`.
- `NetReceivers` = count of `net\.Receive\s*\(`.

## Lines

`source.Count(c => c == '\n') + 1`. Info-only, cheap.

## Analyzer loop (the only "logic" file)

```
foreach file in addonFiles where Category == Lua:
   source = File.ReadAllText(path)
   analysis = new LuaAnalysis { Lines = … }
   foreach signal in LuaHeuristics.All:
       matches = signal.Regex.Matches(source)
       if matches.Count == 0: continue
       analysis.Apply(signal, matches)    // updates Realm/Kind/Risk/lists
   persist analysis
```

~40 lines of glue. Everything else is data.

## Limitations (stated on the report page, not hidden)

- Regex-based → can be fooled by comments, strings, or obfuscation. This
  is a **triage aid**, not a security verdict.
- No AST → false positives on e.g. `RunString` appearing inside a comment.
  We do a cheap strip: remove `--[[ … ]]` and `-- …` line comments before
  matching. That handles ~95% of cases; deeper is out of scope.
- `RiskLevel` is heuristic. Treat "High" as "look here", not "guilty".

## Test plan

Table-driven tests in `LuaAnalyzerTests.cs`:

```
[Theory]
[InlineData("autorun/client/x.lua",     "hook.Add('Think', …)",     Realm.Client)]
[InlineData("weapons/gmod_tool/foo.lua","TOOL.Name='x'",            Kind.Tool)]
[InlineData("anything.lua",             "RunString('print(1)')",    Risk.High, "RunScript")]
…
```

Plus a few fixture `.lua` files in `tests/Fixtures/` for end-to-end.