using GmodAddonAnalyzer.Data.Entities;
using GmodAddonAnalyzer.Services;

namespace GmodAddonAnalyzer.Tests;

public sealed class AddonAnalyzerTests
{
    private readonly AddonAnalyzer _analyzer = new();

    [Fact]
    public void IndexTree_EnumeratesAllFiles()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "a.lua"), "print('a')");
        Directory.CreateDirectory(Path.Combine(dir.Path, "models"));
        File.WriteAllBytes(Path.Combine(dir.Path, "models", "x.mdl"), new byte[100]);
        File.WriteAllText(Path.Combine(dir.Path, "b.vmt"), "VertexLitGeneric{}");
        File.WriteAllBytes(Path.Combine(dir.Path, "s.wav"), new byte[50]);

        var files = _analyzer.IndexTree(dir.Path).ToList();
        Assert.Equal(4, files.Count);
        Assert.Contains(files, f => f.RelativePath == "a.lua" && f.Category == FileCategory.Lua);
        Assert.Contains(files, f => f.RelativePath == "models/x.mdl" && f.Category == FileCategory.Model);
        Assert.Contains(files, f => f.RelativePath == "b.vmt" && f.Category == FileCategory.Material);
        Assert.Contains(files, f => f.RelativePath == "s.wav" && f.Category == FileCategory.Sound);
    }

    [Fact]
    public void Accumulate_SumsPerCategory()
    {
        using var dir = new TempDir();
        File.WriteAllBytes(Path.Combine(dir.Path, "m.mdl"), new byte[10]);
        File.WriteAllBytes(Path.Combine(dir.Path, "m2.mdl"), new byte[20]);
        File.WriteAllBytes(Path.Combine(dir.Path, "t.vtf"), new byte[5]);
        var files = _analyzer.IndexTree(dir.Path).ToList();
        var addon = new Addon();
        foreach (var f in files) _analyzer.Accumulate(addon, f);
        Assert.Equal(2, addon.ModelCount);
        Assert.Equal(30, addon.ModelSizeBytes);
        Assert.Equal(1, addon.MaterialCount);
        Assert.Equal(5, addon.MaterialSizeBytes);
    }

    [Fact]
    public void LuaTxt_DualExtension_Detected()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "init.lua.txt"), "--");
        var files = _analyzer.IndexTree(dir.Path).ToList();
        Assert.Equal(FileCategory.Lua, files[0].Category);
        Assert.Equal("lua.txt", files[0].Extension);
    }
}

public sealed class TempDir : IDisposable
{
    public string Path { get; }
    public TempDir() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gaa-" + Guid.NewGuid().ToString("N")[..8]); Directory.CreateDirectory(Path); }
    public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
}