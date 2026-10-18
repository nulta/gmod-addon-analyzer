using System.Diagnostics;
using System.Formats.Tar;
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
            // The root filesystem is read-only in production; SteamCMD writes
            // logs/symlinks under $HOME/.steam, so point HOME at the writable
            // data-volume install dir.
            psi.EnvironmentVariables["HOME"] = _store.SteamCmdRoot;
            psi.ArgumentList.Add("+runscript");
            psi.ArgumentList.Add(scriptPath);

            using var proc = new Process { StartInfo = psi };
            var logSb = new StringWriter();
            proc.OutputDataReceived += (_, e) => { if (e.Data is not null) { logSb.WriteLine(e.Data); _logger.LogInformation("steamcmd: {Line}", e.Data); } };
            proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) { logSb.WriteLine("[err] " + e.Data); _logger.LogWarning("steamcmd: {Line}", e.Data); } };

            if (!proc.Start()) throw new FetchException(-1, "Failed to start SteamCMD.");
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            // A hung SteamCMD must not stall the single-reader pipeline forever.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(Math.Max(1, _options.FetchTimeoutMinutes)));
            try
            {
                await proc.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
                throw new FetchException(-3, $"SteamCMD timed out after {_options.FetchTimeoutMinutes} minutes.");
            }

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
        var isWindows = OperatingSystem.IsWindows();
        // steamcmd.zip is the Windows-only installer (steamcmd.exe); Linux ships
        // steamcmd.sh inside a tarball. Extracting the zip on Linux leaves no
        // steamcmd.sh and the executable check below fails.
        var zipUrl = isWindows
            ? "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip"
            : "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz";
        var zipPath = Path.Combine(_store.SteamCmdRoot, isWindows ? "steamcmd.zip" : "steamcmd_linux.tar.gz");
        using (var resp = await s_defaultHttp.GetAsync(zipUrl, ct))
        {
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(zipPath);
            await resp.Content.CopyToAsync(fs, ct);
        }
        if (isWindows)
        {
            ZipFile.ExtractToDirectory(zipPath, _store.SteamCmdRoot, overwriteFiles: true);
        }
        else
        {
            // .NET 10 removed TarFile.ExtractToTarGzFile; decompress the gz
            // stream and hand the plain tar stream to TarFile.
            await using var gz = File.OpenRead(zipPath);
            using var tar = new GZipStream(gz, CompressionMode.Decompress);
            TarFile.ExtractToDirectory(tar, _store.SteamCmdRoot, overwriteFiles: true);
        }
        File.Delete(zipPath);

        if (!isWindows)
        {
            // TarFile does not restore unix permission bits; the wrapper script
            // and the 32-bit binary it execs both need the executable bit.
            TryChmod(Path.Combine(_store.SteamCmdRoot, "steamcmd.sh"));
            TryChmod(Path.Combine(_store.SteamCmdRoot, "linux32", "steamcmd"));
        }

        if (!File.Exists(SteamCmdExePath))
            throw new FetchException(-2, $"SteamCMD executable not found at {SteamCmdExePath} after install.");
        Volatile.Write(ref _steamCmdReady, 1);
    }

    private static void TryChmod(string path)
    {
        try
        {
            var psi = new ProcessStartInfo { FileName = "chmod", UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("+x");
            psi.ArgumentList.Add(path);
            Process.Start(psi)?.WaitForExit(2000);
        }
        catch { }
    }

    private static string ExtractTail(string log, int lines = 40)
    {
        var all = log.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return string.Join('\n', all.TakeLast(lines));
    }

    private static readonly HttpClient s_defaultHttp = new();
}