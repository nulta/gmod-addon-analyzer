using System.Threading.Channels;
using GmodAddonAnalyzer.Data;
using GmodAddonAnalyzer.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace GmodAddonAnalyzer.Services;

public readonly record struct AnalysisRequest(long RunId, long AddonId);

public sealed class AnalysisPipeline
{
    // Skip "lua" files larger than this; reading arbitrary attacker-sized files
    // into memory would allow an OOM from a single crafted addon.
    public const long MaxLuaSourceBytes = 16L << 20;

    private readonly IDbContextFactory<AnalyzerDbContext> _dbFactory;
    private readonly FetchService _fetch;
    private readonly ExtractService _extract;
    private readonly AddonAnalyzer _addonAnalyzer;
    private readonly LuaAnalyzer _luaAnalyzer;
    private readonly ArtifactStore _store;
    private readonly WorkshopService _workshop;
    private readonly IHostEnvironment _env;
    private readonly ILogger<AnalysisPipeline> _logger;

    public AnalysisPipeline(
        IDbContextFactory<AnalyzerDbContext> dbFactory,
        FetchService fetch, ExtractService extract,
        AddonAnalyzer addonAnalyzer, LuaAnalyzer luaAnalyzer,
        ArtifactStore store, WorkshopService workshop,
        IHostEnvironment env,
        ILogger<AnalysisPipeline> logger)
    {
        _dbFactory = dbFactory;
        _fetch = fetch;
        _extract = extract;
        _addonAnalyzer = addonAnalyzer;
        _luaAnalyzer = luaAnalyzer;
        _store = store;
        _workshop = workshop;
        _env = env;
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
            run.ErrorMessage = Sanitize(ex.LogTail);
            await SaveSafe(db, run);
        }
        catch (ExtractException ex)
        {
            run.Status = AnalysisStatus.ExtractFailed;
            run.ErrorMessage = Sanitize(ex.Message);
            await SaveSafe(db, run);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline failure for run {Id}", req.RunId);
            run.Status = AnalysisStatus.FetchFailed;
            run.ErrorMessage = Sanitize(ex.Message);
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
                // Defense in depth: validate the stored relative path again even
                // though entries were already written through SafePath.
                var fullPath = SafePath.Resolve(extractedDir, f.RelativePath);
                var len = new FileInfo(fullPath).Length;
                if (len > MaxLuaSourceBytes)
                {
                    _logger.LogWarning("Skipping oversized lua file {Path} ({Length} bytes)", f.RelativePath, len);
                    continue;
                }
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

    /// <summary>
    /// Failure messages are shown on the public report/status pages; scrub local
    /// filesystem roots and bound their length before persisting them.
    /// </summary>
    internal string Sanitize(string? message)
    {
        var msg = message ?? "";
        var pairs = new List<(string From, string To)>(6);
        void Add(string? from, string to)
        {
            if (string.IsNullOrEmpty(from)) return;
            try
            {
                var abs = Path.GetFullPath(from);
                if (abs.Length > 2) pairs.Add((abs, to));
            }
            catch { /* best effort */ }
        }

        Add(_store.SteamCmdRoot, "<steamcmd>");
        Add(_store.ArtifactsRoot, "<artifacts>");
        Add(Path.Combine(_store.DataRoot, "logs"), "<logs>");
        Add(_store.DataRoot, "<data>");
        Add(AppContext.BaseDirectory, "<app>");
        Add(_env.ContentRootPath, "<app>");

        foreach (var (from, to) in pairs.OrderByDescending(p => p.From.Length))
            msg = msg.Replace(from, to, StringComparison.OrdinalIgnoreCase);

        msg = msg.Trim();
        if (msg.Length > 4000) msg = msg[..4000] + "\n(truncated)";
        return msg;
    }

    private static async Task SaveSafe(AnalyzerDbContext db, AnalysisRun run)
    {
        try { await db.SaveChangesAsync(); } catch { /* swallow; run state is best-effort */ }
    }
}