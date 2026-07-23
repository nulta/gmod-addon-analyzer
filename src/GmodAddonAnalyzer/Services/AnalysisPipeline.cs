using System.Threading.Channels;
using GmodAddonAnalyzer.Data;
using GmodAddonAnalyzer.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace GmodAddonAnalyzer.Services;

public readonly record struct AnalysisRequest(long RunId, long AddonId);

public sealed class AnalysisPipeline
{
    private readonly IDbContextFactory<AnalyzerDbContext> _dbFactory;
    private readonly FetchService _fetch;
    private readonly ExtractService _extract;
    private readonly AddonAnalyzer _addonAnalyzer;
    private readonly LuaAnalyzer _luaAnalyzer;
    private readonly ArtifactStore _store;
    private readonly WorkshopService _workshop;
    private readonly ILogger<AnalysisPipeline> _logger;

    public AnalysisPipeline(
        IDbContextFactory<AnalyzerDbContext> dbFactory,
        FetchService fetch, ExtractService extract,
        AddonAnalyzer addonAnalyzer, LuaAnalyzer luaAnalyzer,
        ArtifactStore store, WorkshopService workshop,
        ILogger<AnalysisPipeline> logger)
    {
        _dbFactory = dbFactory;
        _fetch = fetch;
        _extract = extract;
        _addonAnalyzer = addonAnalyzer;
        _luaAnalyzer = luaAnalyzer;
        _store = store;
        _workshop = workshop;
        _logger = logger;
    }

    public async Task RunAsync(AnalysisRequest req, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var run = await db.AnalysisRuns.FirstOrDefaultAsync(r => r.Id == req.RunId, ct);
        if (run is null) { _logger.LogWarning("Run {Id} not found", req.RunId); return; }

        try
        {
            run.Status = AnalysisStatus.Fetching;
            await db.SaveChangesAsync(ct);

            var meta = await _workshop.GetMetadataAsync(req.AddonId, ct);

            var packed = await _fetch.FetchAsync(req.AddonId, ct);

            run.Status = AnalysisStatus.Extracting;
            await db.SaveChangesAsync(ct);

            var extractedDir = _store.ExtractedDir(req.AddonId);
            if (Directory.Exists(extractedDir)) Directory.Delete(extractedDir, recursive: true);
            await _extract.ExtractAsync(packed, extractedDir, ct);

            run.Status = AnalysisStatus.Analyzing;
            await db.SaveChangesAsync(ct);

            var addon = await BuildAddonAsync(db, req.AddonId, meta, packed, extractedDir, ct);

            run.Status = AnalysisStatus.Done;
            run.Addon = addon;
            run.ErrorMessage = null;
            await db.SaveChangesAsync(ct);
        }
        catch (FetchException ex)
        {
            run.Status = AnalysisStatus.FetchFailed;
            run.ErrorMessage = ex.LogTail;
            await SaveSafe(db, run);
        }
        catch (ExtractException ex)
        {
            run.Status = AnalysisStatus.ExtractFailed;
            run.ErrorMessage = ex.Message;
            await SaveSafe(db, run);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline failure for run {Id}", req.RunId);
            run.Status = AnalysisStatus.FetchFailed;
            run.ErrorMessage = ex.Message;
            await SaveSafe(db, run);
        }
    }

    private async Task<Addon> BuildAddonAsync(AnalyzerDbContext db, long addonId, WorkshopMetadata? meta,
        string packedPath, string extractedDir, CancellationToken ct)
    {
        var existing = meta is not null
            ? await db.Addons.FirstOrDefaultAsync(a => a.WorkshopId == addonId && a.UpdatedAt == meta.Value.UpdatedAt, ct)
            : null;
        if (existing is not null)
        {
            db.AddonFiles.RemoveRange(db.AddonFiles.Where(f => f.AddonId == existing.Id));
            await db.SaveChangesAsync(ct);
            existing.Files.Clear();
        }

        var addon = existing ?? new Addon { WorkshopId = addonId };
        addon.Title = meta?.Title ?? $"Addon {addonId}";
        addon.UpdatedAt = meta?.UpdatedAt ?? DateTime.UtcNow;
        addon.LastFetchedAt = DateTime.UtcNow;
        addon.PackedSizeBytes = new FileInfo(packedPath).Length;
        addon.ExtractedSizeBytes = _store.DirectorySize(extractedDir);
        addon.ModelCount = addon.MaterialCount = addon.SoundCount = addon.LuaCount = 0;
        addon.ModelSizeBytes = addon.MaterialSizeBytes = addon.SoundSizeBytes = addon.LuaSizeBytes = 0;

        if (existing is null) db.Addons.Add(addon);
        await db.SaveChangesAsync(ct);

        var files = _addonAnalyzer.IndexTree(extractedDir).ToList();
        foreach (var f in files)
        {
            f.AddonId = addon.Id;
            _addonAnalyzer.Accumulate(addon, f);
            db.AddonFiles.Add(f);
        }
        await db.SaveChangesAsync(ct);

        foreach (var f in files.Where(f => f.Category == FileCategory.Lua))
        {
            try
            {
                var fullPath = Path.Combine(extractedDir, f.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                var source = await File.ReadAllTextAsync(fullPath, ct);
                f.LuaAnalysis = _luaAnalyzer.Analyze(f.RelativePath, source);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to analyze lua file {Path}", f.RelativePath);
            }
        }
        await db.SaveChangesAsync(ct);
        return addon;
    }

    private static async Task SaveSafe(AnalyzerDbContext db, AnalysisRun run)
    {
        try { await db.SaveChangesAsync(); } catch { /* swallow; run state is best-effort */ }
    }
}