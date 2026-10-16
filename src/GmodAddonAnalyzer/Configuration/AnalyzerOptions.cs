namespace GmodAddonAnalyzer.Configuration;

public sealed class AnalyzerOptions
{
    public const string SectionName = "Analyzer";

    public string DataRoot { get; set; } = "./data";
    public string? SteamCmdPath { get; set; }
    public int WorkshopAppId { get; set; } = 4000;

    /// <summary>
    /// Set true only when running behind a reverse proxy that sets X-Forwarded-For.
    /// Only loopback proxies are trusted by default (KnownProxies); configure
    /// KnownProxies/KnownNetworks explicitly for anything else.
    /// </summary>
    public bool TrustProxy { get; set; } = false;

    public bool PowEnabled { get; set; } = true;
    public int PowDifficulty { get; set; } = 16;
    public int ChallengeTtlMinutes { get; set; } = 5;

    public int CleanupTtlDays { get; set; } = 5;
    public int CleanupIntervalHours { get; set; } = 6;

    /// <summary>Maximum wall-clock time for a single SteamCMD fetch.</summary>
    public int FetchTimeoutMinutes { get; set; } = 15;

    /// <summary>
    /// Bounded queue depth for the analysis pipeline. Unbounded submission lets a
    /// client flood the (serialized, minutes-per-item) pipeline backlog.
    /// </summary>
    public int MaxQueuedAnalyses { get; set; } = 100;

    /// <summary>
    /// Cap on database rows in non-terminal states, backstopping the in-memory
    /// queue across restarts.
    /// </summary>
    public int MaxPendingAnalysisRuns { get; set; } = 100;

    /// <summary>
    /// How soon the same addon may be analyzed again after a run finishes
    /// (successful or failed). The dedupe check only suppresses in-flight runs;
    /// without this threshold, re-submitting an already-analyzed addon always
    /// triggers a full re-download.
    /// </summary>
    public int ResubmitCooldownMinutes { get; set; } = 30;

    /// <summary>
    /// AnalysisRuns are trimmed after this many days (metadata-only Addon rows
    /// are kept forever by design, to back report permalinks).
    /// </summary>
    public int RunTtlDays { get; set; } = 30;

    /// <summary>
    /// Forwarded-header proxies to trust when TrustProxy is enabled. Entries are
    /// single IPs ("10.0.0.5") or CIDR networks ("10.0.0.0/8"). Loopback proxies
    /// are always trusted; anything else must be listed here.
    /// </summary>
    public string[] KnownProxies { get; set; } = [];

    public string AdminPassword { get; set; } = string.Empty;
    public int AdminMaxAttempts { get; set; } = 5;
    public int AdminLockoutMinutes { get; set; } = 15;
    public int AdminSessionMinutes { get; set; } = 720;

    /// <summary>
    /// Global cap on failed admin logins within one lockout window, across all
    /// source IPs. Distributed password guessing from many rotating IPs would
    /// otherwise outrun the per-IP lockout.
    /// </summary>
    public int AdminMaxGlobalAttempts { get; set; } = 100;
}