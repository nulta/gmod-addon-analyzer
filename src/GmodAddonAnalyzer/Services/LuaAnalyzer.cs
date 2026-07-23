using System.Text.RegularExpressions;
using GmodAddonAnalyzer.Data.Entities;

namespace GmodAddonAnalyzer.Services;

public static class LuaHeuristics
{
    public static readonly Regex HookAdd = Compile(@"hook\.Add\s*\(\s*[""']([^""']+)[""']");
    public static readonly Regex HookCall = Compile(@"hook\.(?:Call|Run)\s*\(\s*[""']([^""']+)[""']");
    public static readonly Regex TimerCreate = Compile(@"timer\.(?:Create|Simple)\s*\(");
    public static readonly Regex AddNetString = Compile(@"util\.AddNetworkString\s*\(\s*[""']([^""']+)[""']");
    public static readonly Regex NetReceive = Compile(@"net\.Receive\s*\(");

    public static readonly (string Id, RiskLevel Risk, Regex Re)[] RiskSignals =
    [
        ("RunString",         RiskLevel.High,   Compile(@"\bRunString\s*\(")),
        ("RunScript",         RiskLevel.High,   Compile(@"\bRunScript\s*\(")),
        ("CompileString",     RiskLevel.High,   Compile(@"\bCompileString\s*\(")),
        ("LoadString",       RiskLevel.High,   Compile(@"\bloadstring\s*\(")),
        ("GlobalIndex",      RiskLevel.Medium, Compile(@"_G\s*\[\s*[""']")),
        ("GlobalIndexVar",   RiskLevel.Medium, Compile(@"_G\s*\[\s*[A-Za-z_]")),
        ("OsExecute",        RiskLevel.High,   Compile(@"\bos\.execute\s*\(")),
        ("IoOpen",           RiskLevel.High,   Compile(@"\bio\.(?:open|popen|lines)\s*\(")),
        ("RunConsoleCommand",RiskLevel.Low,    Compile(@"\bRunConsoleCommand\s*\(")),
        ("HttpPost",         RiskLevel.Low,    Compile(@"\bhttp\.(?:Post|Fetch)\s*\(")),
        ("Require",          RiskLevel.Low,    Compile(@"\brequire\s*\(\s*[""']")),
    ];

    private static Regex Compile(string p) => new(p, RegexOptions.Compiled);
}

public sealed class LuaAnalyzer
{
    public LuaAnalysis Analyze(string relativePath, string source)
    {
        var realm = InferRealm(relativePath, source);
        var kind = InferKind(relativePath, source);
        var stripped = StripComments(source);
        var risk = RiskLevel.None;
        var signals = new List<string>();
        foreach (var (id, lvl, re) in LuaHeuristics.RiskSignals)
        {
            if (re.IsMatch(stripped))
            {
                signals.Add(id);
                if (lvl > risk) risk = lvl;
            }
        }
        var hooksDeclared = DistinctCapped(LuaHeuristics.HookAdd.Matches(stripped), 200);
        var hooksCalled = DistinctCapped(LuaHeuristics.HookCall.Matches(stripped), 200);
        var netSent = DistinctCapped(LuaHeuristics.AddNetString.Matches(stripped), 200);
        var timers = LuaHeuristics.TimerCreate.Matches(stripped).Count;
        var netRecv = LuaHeuristics.NetReceive.Matches(stripped).Count;
        var lines = source.Count(c => c == '\n') + 1;

        return new LuaAnalysis
        {
            Realm = realm,
            Kind = kind,
            RiskLevel = risk,
            RiskSignals = ToJsonArray(signals),
            HooksDeclared = ToJsonArray(hooksDeclared),
            HooksCalled = ToJsonArray(hooksCalled),
            TimersCreated = timers,
            NetMessagesSent = ToJsonArray(netSent),
            NetReceivers = netRecv,
            Lines = lines,
        };
    }

    private static Realm InferRealm(string path, string source)
    {
        var p = path.Replace('\\', '/');
        if (p.StartsWith("lua/autorun/client/", Ord)) return Realm.Client;
        if (p.StartsWith("lua/autorun/server/", Ord)) return Realm.Server;
        if (p.StartsWith("lua/autorun/", Ord)) return Realm.Shared;
        if (p.StartsWith("lua/vgui/", Ord)) return Realm.Client;
        if (p.StartsWith("lua/weapons/gmod_tool/", Ord)) return Realm.Shared;
        if (p.StartsWith("lua/weapons/", Ord)) return Realm.Shared;
        if (p.StartsWith("lua/entities/", Ord)) return Realm.Shared;
        if (p.StartsWith("lua/effects/", Ord)) return Realm.Shared;
        if (p.StartsWith("lua/includes/modules/", Ord)) return Realm.Shared;
        if (p.Contains("cl_", Ord) || p.Contains("_cl", Ord) || p.Contains("client/", Ord)) return Realm.Client;
        if (p.Contains("sv_", Ord) || p.Contains("server/", Ord)) return Realm.Server;
        var hasClient = source.Contains("if CLIENT then", Ord) || source.Contains("if (CLIENT) then", Ord);
        var hasServer = source.Contains("if SERVER then", Ord) || source.Contains("if (SERVER) then", Ord);
        if (hasClient && hasServer) return Realm.Shared;
        if (hasClient) return Realm.Client;
        if (hasServer) return Realm.Server;
        return Realm.Unknown;
    }

    private static LuaKind InferKind(string path, string source)
    {
        var p = path.Replace('\\', '/');
        if (p.StartsWith("lua/weapons/gmod_tool/", Ord)) return LuaKind.Tool;
        if (p.StartsWith("lua/weapons/", Ord) && HasTable(source, "SWEP")) return LuaKind.Swep;
        if (p.StartsWith("lua/entities/", Ord) || p.StartsWith("lua/effects/", Ord))
        {
            if (HasTable(source, "ENT"))
            {
                var typeMatch = Regex.Match(source, @"ENT\.Type\s*=\s*[""']([^""']+)[""']");
                if (typeMatch.Success && typeMatch.Groups[1].Value.Contains("ai", Ord))
                    return LuaKind.Snpc;
                return p.Contains("effects/", Ord) ? LuaKind.Effect : LuaKind.Entity;
            }
        }
        if (HasTable(source, "SWEP")) return LuaKind.Swep;
        if (HasTable(source, "TOOL")) return LuaKind.Tool;
        if (HasTable(source, "EFFECT")) return LuaKind.Effect;
        if (HasTable(source, "ENT")) return LuaKind.Entity;
        return LuaKind.Util;
    }

    private static bool HasTable(string source, string name) => source.Contains(name + ".", Ord) || source.Contains(name + "[", Ord);

    private static string StripComments(string src)
    {
        src = Regex.Replace(src, @"--\[\[.*?\]\]", "", RegexOptions.Singleline);
        src = Regex.Replace(src, @"--[^\n]*", "");
        return src;
    }

    private static List<string> DistinctCapped(MatchCollection m, int cap)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match x in m)
        {
            set.Add(x.Groups[1].Value);
            if (set.Count >= cap) break;
        }
        return set.ToList();
    }

    private static string ToJsonArray(List<string> items) =>
        items.Count == 0 ? "[]" : "[\"" + string.Join("\",\"", items.Select(Escape)) + "\"]";

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static readonly StringComparison Ord = StringComparison.Ordinal;
}