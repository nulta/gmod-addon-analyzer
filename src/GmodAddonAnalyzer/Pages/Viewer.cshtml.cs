using System.Text;
using GmodAddonAnalyzer.Configuration;
using GmodAddonAnalyzer.Data;
using GmodAddonAnalyzer.Data.Entities;
using GmodAddonAnalyzer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GmodAddonAnalyzer.Pages;

public sealed class ViewerModel(IDbContextFactory<AnalyzerDbContext> dbFactory, ArtifactStore store) : PageModel
{
    public long WorkshopId { get; private set; }
    public string? Path { get; private set; }
    public AddonFile? File { get; private set; }
    public LuaAnalysis? Lua { get; private set; }
    public string? SourceHtml { get; private set; }
    public bool OnDisk { get; private set; }

    public async Task OnGetAsync(long id, string? path)
    {
        WorkshopId = id;
        Path = path;
        if (string.IsNullOrEmpty(path)) return;

        await using var db = await dbFactory.CreateDbContextAsync();
        var addon = await db.Addons.FirstOrDefaultAsync(a => a.WorkshopId == id);
        if (addon is null) return;

        File = await db.AddonFiles.FirstOrDefaultAsync(f => f.AddonId == addon.Id && f.RelativePath == path);
        if (File is null) return;

        Lua = await db.LuaAnalyses.FirstOrDefaultAsync(l => l.AddonFileId == File.Id);

        var fullPath = System.IO.Path.Combine(store.ExtractedDir(id), path.Replace('/', System.IO.Path.DirectorySeparatorChar));
        OnDisk = System.IO.File.Exists(fullPath);
        if (!OnDisk) return;

        if (IsTextLike(File.Extension))
        {
            try
            {
                var source = await System.IO.File.ReadAllTextAsync(fullPath);
                SourceHtml = File.Extension == "lua" || File.Extension == "lua.txt"
                    ? LuaHighlighter.Highlight(source)
                    : Esc(source);
            }
            catch { SourceHtml = "(unreadable)"; }
        }
    }

    private static bool IsTextLike(string ext) =>
        ext is "lua" or "txt" or "vmt" or "json" or "cfg" or "ini" or "lua.txt" or "md";

    private static string Esc(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}