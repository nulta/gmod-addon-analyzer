using GmodAddonAnalyzer.Data;
using GmodAddonAnalyzer.Data.Entities;
using GmodAddonAnalyzer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GmodAddonAnalyzer.Pages;

public sealed class ReportModel(IDbContextFactory<AnalyzerDbContext> dbFactory, AdminGuardService guard) : PageModel
{
    public long WorkshopId { get; private set; }
    public long? RunId { get; private set; }
    public AnalysisRun? Run { get; private set; }
    public Addon? Addon { get; private set; }
    public List<LuaAnalysis> TopRiskLua { get; private set; } = [];

    /// <summary>
    /// Run error text can contain server internals (SteamCMD log tails). Matches
    /// the /status endpoint's policy: admin sees the detail, everyone else only
    /// sees the status code. Was previously exposed unauthenticated on this page.
    /// </summary>
    public string? ErrorDetail { get; private set; }

    public async Task OnGetAsync(long id, long? run)
    {
        WorkshopId = id;
        RunId = run;
        await using var db = await dbFactory.CreateDbContextAsync();

        Run = run is not null
            ? await db.AnalysisRuns.FirstOrDefaultAsync(r => r.Id == run)
            : null;

        if (Run is null)
            Run = await db.AnalysisRuns
                .Where(r => r.AddonId == id)
                .OrderByDescending(r => r.SubmittedAt)
                .FirstOrDefaultAsync();

        if (Run is not null
            && !string.IsNullOrEmpty(Run.ErrorMessage)
            && guard.IsConfigured
            && guard.ValidateToken(Request.Cookies[AdminGuardService.CookieName]))
        {
            ErrorDetail = Run.ErrorMessage;
        }

        Addon = await db.Addons.FirstOrDefaultAsync(a => a.WorkshopId == id);
        if (Addon is not null)
        {
            TopRiskLua = await db.LuaAnalyses
                .Where(l => l.AddonFile != null && l.AddonFile.AddonId == Addon.Id
                    && l.RiskLevel != RiskLevel.None)
                .OrderByDescending(l => l.RiskLevel)
                .Take(20)
                .Include(l => l.AddonFile)
                .ToListAsync();
        }
    }
}