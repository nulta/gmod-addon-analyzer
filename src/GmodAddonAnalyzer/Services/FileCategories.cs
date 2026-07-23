using GmodAddonAnalyzer.Data.Entities;

namespace GmodAddonAnalyzer.Services;

public static class FileCategories
{
    private static readonly Dictionary<string, FileCategory> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        { "mdl", FileCategory.Model },
        { "vvd", FileCategory.Model },
        { "vtx", FileCategory.Model },
        { "phy", FileCategory.Model },
        { "ani", FileCategory.Model },
        { "sw.vtx", FileCategory.Model },
        { "vmt", FileCategory.Material },
        { "vtf", FileCategory.Material },
        { "wav", FileCategory.Sound },
        { "mp3", FileCategory.Sound },
        { "ogg", FileCategory.Sound },
        { "lua", FileCategory.Lua },
        { "lua.txt", FileCategory.Lua },
        { "bsp", FileCategory.Map },
        { "bsp.gz", FileCategory.Map },
        { "nav", FileCategory.Map },
        { "ain", FileCategory.Map },
    };

    public static FileCategory Classify(string relativePath)
    {
        var ext = GetExtension(relativePath);
        return Map.TryGetValue(ext, out var c) ? c : FileCategory.Other;
    }

    public static string GetExtension(string relativePath)
    {
        var name = Path.GetFileName(relativePath);
        if (name.EndsWith(".lua.txt", StringComparison.OrdinalIgnoreCase))
            return "lua.txt";
        var idx = name.LastIndexOf('.');
        return idx < 0 ? "" : name[(idx + 1)..].ToLowerInvariant();
    }
}