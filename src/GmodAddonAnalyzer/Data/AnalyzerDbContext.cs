using GmodAddonAnalyzer.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace GmodAddonAnalyzer.Data;

public sealed class AnalyzerDbContext(DbContextOptions<AnalyzerDbContext> options) : DbContext(options)
{
    public DbSet<AnalysisRun> AnalysisRuns => Set<AnalysisRun>();
    public DbSet<Addon> Addons => Set<Addon>();
    public DbSet<AddonFile> AddonFiles => Set<AddonFile>();
    public DbSet<LuaAnalysis> LuaAnalyses => Set<LuaAnalysis>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AnalysisRun>(e =>
        {
            e.HasIndex(r => r.AddonId);
            e.HasIndex(r => r.SubmittedAt);
            e.Ignore(r => r.Addon);
        });

        b.Entity<Addon>(e =>
        {
            e.HasIndex(a => new { a.WorkshopId, a.UpdatedAt }).IsUnique();
            e.Property(a => a.Title).HasMaxLength(512);
            e.HasMany(a => a.Files)
                .WithOne(f => f.Addon!)
                .HasForeignKey(f => f.AddonId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AddonFile>(e =>
        {
            e.HasIndex(f => new { f.AddonId, f.RelativePath }).IsUnique();
            e.Property(f => f.RelativePath).HasMaxLength(512);
            e.Property(f => f.Extension).HasMaxLength(16);
            e.HasOne(f => f.LuaAnalysis)
                .WithOne(l => l.AddonFile!)
                .HasForeignKey<LuaAnalysis>(l => l.AddonFileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<LuaAnalysis>(e =>
        {
            e.Property(l => l.RiskSignals).HasMaxLength(2048);
            e.Property(l => l.HooksDeclared).HasMaxLength(2048);
            e.Property(l => l.HooksCalled).HasMaxLength(2048);
            e.Property(l => l.NetMessagesSent).HasMaxLength(2048);
        });
    }
}