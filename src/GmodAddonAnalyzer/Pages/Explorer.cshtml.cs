using System.Text;
using GmodAddonAnalyzer.Data;
using GmodAddonAnalyzer.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GmodAddonAnalyzer.Pages;

public sealed class ExplorerModel(IDbContextFactory<AnalyzerDbContext> dbFactory) : PageModel
{
    public long WorkshopId { get; private set; }
    public Addon? Addon { get; private set; }
    public List<AddonFile> Entries { get; private set; } = [];
    public string TreeHtml { get; private set; } = "";

    public async Task OnGetAsync(long id)
    {
        WorkshopId = id;
        await using var db = await dbFactory.CreateDbContextAsync();
        Addon = await db.Addons.FirstOrDefaultAsync(a => a.WorkshopId == id);
        if (Addon is null) return;
        Entries = await db.AddonFiles.Where(f => f.AddonId == Addon.Id).ToListAsync();
        TreeHtml = BuildTree(Entries, id);
    }

    private static string BuildTree(List<AddonFile> files, long workshopId)
    {
        var root = new TreeNode("", true);
        foreach (var f in files.OrderBy(f => f.RelativePath, StringComparer.Ordinal))
        {
            var segs = f.RelativePath.Split('/');
            var cur = root;
            for (int i = 0; i < segs.Length - 1; i++)
            {
                var seg = segs[i];
                // Dictionary lookup: a linear FirstOrDefault over children made
                // this O(dirs × files) — an addon with tens of thousands of
                // directories pinned server threads long enough to starve the
                // whole request pipeline.
                if (!cur.DirLookup.TryGetValue(seg, out var next))
                {
                    next = new TreeNode(seg, true);
                    cur.Children.Add(next);
                    cur.DirLookup[seg] = next;
                }
                cur = next;
            }
            cur.Children.Add(new TreeNode(segs[^1], false) { Size = f.SizeBytes, RelativePath = f.RelativePath });
        }

        var sb = new StringBuilder();
        RenderNode(sb, root, workshopId);
        return sb.ToString();
    }

    private static void RenderNode(StringBuilder sb, TreeNode node, long workshopId)
    {
        if (node.IsDir)
        {
            sb.Append("<details open><summary>").Append(Esc(node.Name)).Append("/</summary>");
            foreach (var child in node.Children.OrderBy(c => !c.IsDir).ThenBy(c => c.Name))
            {
                sb.Append("<div style=\"margin-left:16px\">");
                RenderNode(sb, child, workshopId);
                sb.Append("</div>");
            }
            sb.Append("</details>");
        }
        else
        {
            var url = $"/viewer/{workshopId}?path={Uri.EscapeDataString(node.RelativePath)}";
            sb.Append("<div class=\"tree-file\"><a href=\"").Append(url).Append("\">")
              .Append(Esc(node.Name)).Append("</a> <span class=\"muted\">")
              .Append(node.Size).Append(" bytes</span></div>");
        }
    }

    private static string Esc(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    public sealed class TreeNode
    {
        public string Name { get; }
        public bool IsDir { get; }
        public long Size { get; set; }
        public string RelativePath { get; set; } = "";
        public List<TreeNode> Children { get; } = [];
        /// <summary>O(1) lookup of directory children by name (see BuildTree).</summary>
        public Dictionary<string, TreeNode> DirLookup { get; } = new(StringComparer.Ordinal);
        public TreeNode(string name, bool isDir) { Name = name; IsDir = isDir; }
    }
}