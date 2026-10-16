using GmodAddonAnalyzer.Services;

namespace GmodAddonAnalyzer.Tests;

public sealed class SafePathTests
{
    private static readonly string Base = Path.GetFullPath("/tmp/base");

    private static string Resolve(string relative) => SafePath.Resolve(Base, relative);

    [Theory]
    [InlineData("../evil")]
    [InlineData("..\\evil")]
    [InlineData("..")]
    [InlineData("a/../evil")]
    [InlineData("/abs")]
    [InlineData("C:\\evil")]
    [InlineData("C:evil")]
    [InlineData("a/b:C")]
    [InlineData("")]
    [InlineData("a\0b")]
    [InlineData("a\nb")]
    [InlineData("a\tb")]
    [InlineData("a\rb")]
    public void Hostile_Inputs_AreRejected(string relative)
    {
        Assert.Throws<PathSafetyException>(() => Resolve(relative));
    }

    [Theory]
    [InlineData("lua/init.lua")]
    [InlineData("a\\b\\c.txt")]
    [InlineData("a..b.txt")]
    [InlineData("model.mdl")]
    public void Benign_Inputs_AreAccepted(string relative)
    {
        var result = Resolve(relative);
        Assert.StartsWith(Base, result, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Split('/', '\\', Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            s => s == "..");
    }

    [Fact]
    public void Resolved_Path_IsAlwaysInside_Base()
    {
        // Whatever the platform, a resolved path must stay under the base
        // directory (canonically), even for tricky-but-benign inputs. This base
        // deliberately ends in a directory separator to catch normalization bugs.
        var baseWithTrailingSep = Path.GetTempPath();
        var result = SafePath.Resolve(baseWithTrailingSep, "lua/x.lua");
        var baseFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseWithTrailingSep));
        Assert.StartsWith(baseFull, result, StringComparison.Ordinal);
    }
}