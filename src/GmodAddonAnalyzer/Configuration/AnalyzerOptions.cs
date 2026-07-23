namespace GmodAddonAnalyzer.Configuration;

public sealed class AnalyzerOptions
{
    public const string SectionName = "Analyzer";

    public string DataRoot { get; set; } = "./data";
    public string? SteamCmdPath { get; set; }
    public int WorkshopAppId { get; set; } = 4000;
    public string WebApiKey { get; set; } = string.Empty;

    public bool PowEnabled { get; set; } = true;
    public int PowDifficulty { get; set; } = 16;
    public int ChallengeTtlMinutes { get; set; } = 5;

    public int CleanupTtlDays { get; set; } = 5;
    public int CleanupIntervalHours { get; set; } = 6;

    public string AdminPassword { get; set; } = string.Empty;
    public int AdminMaxAttempts { get; set; } = 5;
    public int AdminLockoutMinutes { get; set; } = 15;
}