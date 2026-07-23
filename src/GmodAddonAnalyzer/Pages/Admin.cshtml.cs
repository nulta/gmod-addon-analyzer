using GmodAddonAnalyzer.Configuration;
using GmodAddonAnalyzer.Data;
using GmodAddonAnalyzer.Data.Entities;
using GmodAddonAnalyzer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GmodAddonAnalyzer.Pages;

public sealed class AdminModel(IDbContextFactory<AnalyzerDbContext> dbFactory, AnalyzerOptions options, CleanupService cleanup) : PageModel
{
    public int TtlDays { get; } = options.CleanupTtlDays;
    public List<Addon> Addons { get; private set; } = [];

    public async Task OnGetAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        Addons = await db.Addons
            .OrderByDescending(a => a.LastFetchedAt)
            .Take(200)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostDeleteAsync(long id)
    {
        await cleanup.DeleteArtifactsForAsync(id);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSweepAsync()
    {
        await cleanup.SweepAsync();
        return RedirectToPage();
    }
}