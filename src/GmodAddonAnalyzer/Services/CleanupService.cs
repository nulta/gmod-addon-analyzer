using GmodAddonAnalyzer.Configuration;
using GmodAddonAnalyzer.Data;
using GmodAddonAnalyzer.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GmodAddonAnalyzer.Services;

public sealed class CleanupService
{
    private readonly IDbContextFactory<AnalyzerDbContext> _dbFactory;
    private readonly ArtifactStore _store;
    private readonly AnalyzerOptions _options;
    private readonly ILogger<CleanupService> _logger;

    public CleanupService(IDbContextFactory<AnalyzerDbContext> dbFactory, ArtifactStore store,
        AnalyzerOptions options, ILogger<CleanupService> logger)
    {
        _dbFactory = dbFactory;
        _store = store;
        _options = options;
        _logger = logger;
    }

    public async Task<int> SweepAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-_options.CleanupTtlDays);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var stale = await db.Addons
            .Where(a => a.ExtractedSizeBytes > 0 && a.LastFetchedAt < cutoff)
            .ToListAsync(ct);
        foreach (var a in stale)
        {
            DeleteArtifactsFor(a);
        }
        if (stale.Count > 0) await db.SaveChangesAsync(ct);
        _logger.LogInformation("Cleanup swept {Count} addons", stale.Count);
        SweepOldLogs(cutoff);
        await CloseStalePendingRuns(ct);
        await SweepOldRuns(ct);
        return stale.Count;
    }

    /// <summary>
    /// Runs enqueued to the pipeline but lost to a crash/restart would sit Pending
    /// forever (nobody reads the in-memory channel again). Mark anything still in
    /// a working state long past the fetch timeout as failed.
    /// </summary>
    private async Task CloseStalePendingRuns(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-Math.Max(60, _options.FetchTimeoutMinutes + 15));
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var stale = await db.AnalysisRuns
                .Where(r => (r.Status == AnalysisStatus.Pending || r.Status == AnalysisStatus.Fetching
                          || r.Status == AnalysisStatus.Extracting || r.Status == AnalysisStatus.Analyzing)
                         && r.SubmittedAt < cutoff)
                .ToListAsync(ct);
            if (stale.Count == 0) return;
            foreach (var r in stale)
            {
                r.Status = AnalysisStatus.FetchFailed;
                r.ErrorMessage = "Run was interrupted (server restart or timeout before completion).";
            }
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Closed {Count} stale pending runs", stale.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stale pending run sweep failed");
        }
    }

    /// <summary>
    /// AnalysisRuns previously grew forever (a client-visible queue-flooding vector
    /// for DB storage); trim them past the retention window. Addon metadata rows
    /// are intentionally kept so report permalinks keep resolving.
    /// </summary>
    private async Task SweepOldRuns(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, _options.RunTtlDays));
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var removed = await db.AnalysisRuns
                .Where(r => r.SubmittedAt < cutoff)
                .ExecuteDeleteAsync(ct);
            if (removed > 0) _logger.LogInformation("Removed {Count} old analysis runs", removed);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Old run sweep failed");
        }
    }

    /// <summary>
    /// SteamCMD log files and runscripts under LogsRoot are never referenced by the
    /// app again; the previous sweep only removed addon artifacts, leaking logs forever.
    /// </summary>
    private void SweepOldLogs(DateTime cutoff)
    {
        try
        {
            if (!Directory.Exists(_store.LogsRoot)) return;
            var removed = 0;
            foreach (var f in Directory.EnumerateFiles(_store.LogsRoot))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(f) < cutoff)
                    {
                        File.Delete(f);
                        removed++;
                    }
                }
                catch (Exception) { /* best effort per file */ }
            }
            if (removed > 0) _logger.LogInformation("Cleanup swept {Count} old log files", removed);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Old log sweep failed");
        }
    }

    public async Task DeleteArtifactsForAsync(long workshopId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var addon = await db.Addons.FirstOrDefaultAsync(a => a.WorkshopId == workshopId);
        if (addon is null) return;
        DeleteArtifactsFor(addon);
        await db.SaveChangesAsync();
    }

    private void DeleteArtifactsFor(Addon addon)
    {
        _store.DeleteArtifacts(addon.WorkshopId);
        addon.PackedSizeBytes = 0;
        addon.ExtractedSizeBytes = 0;
        addon.ModelSizeBytes = addon.MaterialSizeBytes = addon.SoundSizeBytes = addon.LuaSizeBytes = 0;
    }
}