namespace GmodAddonAnalyzer.Data.Entities;

public enum AnalysisStatus
{
    Pending,
    Fetching,
    Extracting,
    Analyzing,
    Done,
    FetchFailed,
    ExtractFailed,
}

public enum FileCategory
{
    Other,
    Model,
    Material,
    Sound,
    Lua,
    Map,
}

public enum Realm
{
    Unknown,
    Client,
    Server,
    Shared,
}

public enum LuaKind
{
    Unknown,
    Entity,
    Swep,
    Snpc,
    Tool,
    Effect,
    Util,
}

public enum RiskLevel
{
    None,
    Low,
    Medium,
    High,
}

public sealed class AnalysisRun
{
    public long Id { get; set; }
    public long AddonId { get; set; }
    public DateTime SubmittedAt { get; set; }
    public AnalysisStatus Status { get; set; } = AnalysisStatus.Pending;
    public string? ErrorMessage { get; set; }
    public string? SubmittedByIp { get; set; }
    public long PowNonce { get; set; }

    public Addon? Addon { get; set; }
}

public sealed class Addon
{
    public long Id { get; set; }
    public long WorkshopId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public DateTime LastFetchedAt { get; set; }

    public long PackedSizeBytes { get; set; }
    public long ExtractedSizeBytes { get; set; }

    public int ModelCount { get; set; }
    public int MaterialCount { get; set; }
    public int SoundCount { get; set; }
    public int LuaCount { get; set; }

    public long ModelSizeBytes { get; set; }
    public long MaterialSizeBytes { get; set; }
    public long SoundSizeBytes { get; set; }
    public long LuaSizeBytes { get; set; }

    public List<AddonFile> Files { get; set; } = [];
}

public sealed class AddonFile
{
    public long Id { get; set; }
    public long AddonId { get; set; }
    public string RelativePath { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public FileCategory Category { get; set; } = FileCategory.Other;

    public Addon? Addon { get; set; }
    public LuaAnalysis? LuaAnalysis { get; set; }
}

public sealed class LuaAnalysis
{
    public long Id { get; set; }
    public long AddonFileId { get; set; }
    public Realm Realm { get; set; } = Realm.Unknown;
    public LuaKind Kind { get; set; } = LuaKind.Unknown;
    public RiskLevel RiskLevel { get; set; } = RiskLevel.None;
    public string RiskSignals { get; set; } = "[]";
    public string HooksDeclared { get; set; } = "[]";
    public string HooksCalled { get; set; } = "[]";
    public int TimersCreated { get; set; }
    public string NetMessagesSent { get; set; } = "[]";
    public int NetReceivers { get; set; }
    public int Lines { get; set; }

    public AddonFile? AddonFile { get; set; }
}