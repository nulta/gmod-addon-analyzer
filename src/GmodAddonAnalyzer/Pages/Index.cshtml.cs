using GmodAddonAnalyzer.Data;
using GmodAddonAnalyzer.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GmodAddonAnalyzer.Pages;

public sealed class IndexModel(IDbContextFactory<AnalyzerDbContext> dbFactory) : PageModel
{
    public List<AnalysisRun> RecentRuns { get; private set; } = [];

    public async Task OnGetAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        RecentRuns = await db.AnalysisRuns
            .OrderByDescending(r => r.SubmittedAt)
            .Take(10)
            .ToListAsync();
    }
}