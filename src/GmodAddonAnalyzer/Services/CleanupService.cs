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
        return stale.Count;
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