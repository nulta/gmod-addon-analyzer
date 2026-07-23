using GmodAddonAnalyzer.Data.Entities;

namespace GmodAddonAnalyzer.Services;

public sealed class AddonAnalyzer
{
    public IEnumerable<AddonFile> IndexTree(string extractedDir)
    {
        if (!Directory.Exists(extractedDir)) yield break;
        foreach (var path in Directory.EnumerateFiles(extractedDir, "*", SearchOption.AllDirectories))
        {
            var info = new FileInfo(path);
            var rel = Path.GetRelativePath(extractedDir, path).Replace('\\', '/');
            var ext = FileCategories.GetExtension(rel);
            var cat = FileCategories.Classify(rel);
            yield return new AddonFile
            {
                RelativePath = rel,
                Extension = ext,
                SizeBytes = info.Length,
                Category = cat,
            };
        }
    }

    public void Accumulate(Addon addon, AddonFile f)
    {
        switch (f.Category)
        {
            case FileCategory.Model:
                addon.ModelCount++; addon.ModelSizeBytes += f.SizeBytes; break;
            case FileCategory.Material:
                addon.MaterialCount++; addon.MaterialSizeBytes += f.SizeBytes; break;
            case FileCategory.Sound:
                addon.SoundCount++; addon.SoundSizeBytes += f.SizeBytes; break;
            case FileCategory.Lua:
                addon.LuaCount++; addon.LuaSizeBytes += f.SizeBytes; break;
            default: break;
        }
    }
}