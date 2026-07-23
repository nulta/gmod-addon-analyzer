using GmodAddonAnalyzer.Data;
using GmodAddonAnalyzer.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GmodAddonAnalyzer.Pages;

public sealed class LuaTagsModel(IDbContextFactory<AnalyzerDbContext> dbFactory) : PageModel
{
    public long WorkshopId { get; private set; }
    public List<Entry> Entries { get; private set; } = [];

    public async Task OnGetAsync(long id)
    {
        WorkshopId = id;
        await using var db = await dbFactory.CreateDbContextAsync();
        var addon = await db.Addons.FirstOrDefaultAsync(a => a.WorkshopId == id);
        if (addon is null) return;

        var rows = await db.AddonFiles
            .Where(f => f.AddonId == addon.Id && f.Category == FileCategory.Lua)
            .Select(f => new { f.RelativePath, f.LuaAnalysis })
            .ToListAsync();

        Entries = rows
            .Where(r => r.LuaAnalysis is not null)
            .OrderBy(r => r.RelativePath, StringComparer.Ordinal)
            .Select(r => new Entry(
                r.RelativePath,
                r.LuaAnalysis!.Realm,
                r.LuaAnalysis.Kind,
                r.LuaAnalysis.RiskLevel,
                r.LuaAnalysis.RiskSignals,
                r.LuaAnalysis.HooksDeclared,
                r.LuaAnalysis.HooksCalled,
                r.LuaAnalysis.TimersCreated,
                r.LuaAnalysis.NetMessagesSent,
                r.LuaAnalysis.NetReceivers,
                r.LuaAnalysis.Lines))
            .ToList();
    }

    public sealed record Entry(
        string Path, Realm Realm, LuaKind Kind, RiskLevel RiskLevel,
        string RiskSignals, string HooksDeclared, string HooksCalled,
        int TimersCreated, string NetMessagesSent, int NetReceivers, int Lines);
}