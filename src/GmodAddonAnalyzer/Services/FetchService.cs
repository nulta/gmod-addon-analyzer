using System.Diagnostics;
using System.IO.Compression;
using GmodAddonAnalyzer.Configuration;
using Microsoft.Extensions.Logging;

namespace GmodAddonAnalyzer.Services;

public sealed class FetchException(int exitCode, string logTail) : Exception($"SteamCMD exited with code {exitCode}")
{
    public int ExitCode { get; } = exitCode;
    public string LogTail { get; } = logTail;
}

public sealed class FetchService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly ArtifactStore _store;
    private readonly AnalyzerOptions _options;
    private readonly ILogger<FetchService> _logger;
    private int _steamCmdReady;

    public FetchService(ArtifactStore store, AnalyzerOptions options, ILogger<FetchService> logger)
    {
        _store = store;
        _options = options;
        _logger = logger;
    }

    public string SteamCmdExePath => Path.Combine(_store.SteamCmdRoot,
        OperatingSystem.IsWindows() ? "steamcmd.exe" : "steamcmd.sh");

    public async Task<string> FetchAsync(long addonId, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            await EnsureSteamCmdReadyAsync(ct);
            _store.PrepareAddon(addonId);

            var packedDir = _store.PackedDir(addonId);
            if (packedDir.Length > 0) Directory.Delete(packedDir, recursive: true);
            Directory.CreateDirectory(packedDir);

            var logFile = _store.LogFile(addonId, DateTime.UtcNow);
            var scriptPath = Path.Combine(_store.LogsRoot, $"cmd-{addonId}-{DateTime.UtcNow:yyyyMMddHHmmss}.txt");
            await File.WriteAllTextAsync(scriptPath, BuildScript(addonId, packedDir), ct);

            var psi = new ProcessStartInfo
            {
                FileName = SteamCmdExePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = _store.SteamCmdRoot,
            };
            psi.ArgumentList.Add("+runscript");
            psi.ArgumentList.Add(scriptPath);

            using var proc = new Process { StartInfo = psi };
            var logSb = new StringWriter();
            proc.OutputDataReceived += (_, e) => { if (e.Data is not null) { logSb.WriteLine(e.Data); _logger.LogInformation("steamcmd: {Line}", e.Data); } };
            proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) { logSb.WriteLine("[err] " + e.Data); _logger.LogWarning("steamcmd: {Line}", e.Data); } };

            if (!proc.Start()) throw new FetchException(-1, "Failed to start SteamCMD.");
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            await proc.WaitForExitAsync(ct);

            var log = logSb.ToString();
            await File.WriteAllTextAsync(logFile, log, ct);

            if (proc.ExitCode != 0)
                throw new FetchException(proc.ExitCode, ExtractTail(log));

            var packed = _store.FindPackedFile(addonId)
                ?? throw new FetchException(proc.ExitCode, "No .gma/.bin file produced by SteamCMD.");
            return packed;
        }
        finally
        {
            Gate.Release();
        }
    }

    private string BuildScript(long addonId, string packedDir)
    {
        return $"""
        @ShutdownOnFailedCommand 1
        @NoPromptForPassword 1
        force_install_dir {packedDir.Replace('\\', '/')}
        login anonymous
        workshop_download_item {_options.WorkshopAppId} {addonId}
        quit
        """;
    }

    private async Task EnsureSteamCmdReadyAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _steamCmdReady) == 1) return;
        if (File.Exists(SteamCmdExePath)) { Volatile.Write(ref _steamCmdReady, 1); return; }

        _logger.LogInformation("Installing SteamCMD to {Path}", _store.SteamCmdRoot);
        var zipUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";
        var zipPath = Path.Combine(_store.SteamCmdRoot, "steamcmd.zip");
        using (var resp = await s_defaultHttp.GetAsync(zipUrl, ct))
        {
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(zipPath);
            await resp.Content.CopyToAsync(fs, ct);
        }
        ZipFile.ExtractToDirectory(zipPath, _store.SteamCmdRoot, overwriteFiles: true);
        File.Delete(zipPath);

        if (!OperatingSystem.IsWindows())
        {
            var sh = Path.Combine(_store.SteamCmdRoot, "steamcmd.sh");
            if (File.Exists(sh)) TryChmod(sh);
        }

        if (!File.Exists(SteamCmdExePath))
            throw new FetchException(-2, $"SteamCMD executable not found at {SteamCmdExePath} after install.");
        Volatile.Write(ref _steamCmdReady, 1);
    }

    private static void TryChmod(string path)
    {
        try { Process.Start("chmod", $"+x \"{path}\"")?.WaitForExit(2000); } catch { }
    }

    private static string ExtractTail(string log, int lines = 40)
    {
        var all = log.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return string.Join('\n', all.TakeLast(lines));
    }

    private static readonly HttpClient s_defaultHttp = new();
}