using GmodAddonAnalyzer.Data;
using GmodAddonAnalyzer.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GmodAddonAnalyzer.Pages;

public sealed class HistoryModel(IDbContextFactory<AnalyzerDbContext> dbFactory) : PageModel
{
    public List<AnalysisRun> Runs { get; private set; } = [];

    public async Task OnGetAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        Runs = await db.AnalysisRuns
            .OrderByDescending(r => r.SubmittedAt)
            .Take(50)
            .ToListAsync();
    }
}