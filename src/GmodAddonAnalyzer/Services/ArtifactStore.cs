using GmodAddonAnalyzer.Configuration;

namespace GmodAddonAnalyzer.Services;

public sealed class ArtifactStore
{
    public string DataRoot { get; }
    public string ArtifactsRoot { get; }
    public string SteamCmdRoot { get; }
    public string LogsRoot { get; }

    public ArtifactStore(AnalyzerOptions options)
    {
        DataRoot = Path.GetFullPath(options.DataRoot);
        ArtifactsRoot = Path.Combine(DataRoot, "artifacts");
        SteamCmdRoot = options.SteamCmdPath is null
            ? Path.Combine(DataRoot, "steamcmd")
            : Path.GetFullPath(options.SteamCmdPath);
        LogsRoot = Path.Combine(DataRoot, "logs");
        Directory.CreateDirectory(ArtifactsRoot);
        Directory.CreateDirectory(LogsRoot);
        Directory.CreateDirectory(SteamCmdRoot);
    }

    public string AddonRoot(long addonId) => Path.Combine(ArtifactsRoot, addonId.ToString());
    public string PackedDir(long addonId) => Path.Combine(AddonRoot(addonId), "packed");
    public string ExtractedDir(long addonId) => Path.Combine(AddonRoot(addonId), "extracted");

    public void PrepareAddon(long addonId)
    {
        Directory.CreateDirectory(PackedDir(addonId));
        Directory.CreateDirectory(ExtractedDir(addonId));
    }

    public string? FindPackedFile(long addonId)
    {
        var dir = PackedDir(addonId);
        if (!Directory.Exists(dir)) return null;
        var files = Directory.GetFiles(dir);
        return files.FirstOrDefault(f =>
            f.EndsWith(".gma", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".legacy.bin", StringComparison.OrdinalIgnoreCase));
    }

    public void DeleteArtifacts(long addonId)
    {
        var root = AddonRoot(addonId);
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    public long DirectorySize(string dir)
    {
        if (!Directory.Exists(dir)) return 0;
        return new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories)
            .Sum(f => f.Length);
    }

    public string LogFile(long addonId, DateTime when) =>
        Path.Combine(LogsRoot, $"{addonId}-{when:yyyyMMddHHmmss}.log");
}