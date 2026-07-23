using System.Text;
using GmodAddonAnalyzer.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GmodAddonAnalyzer.Tests;

public sealed class GmaExtractorTests
{
    private readonly ExtractService _extract = new(NullLogger<ExtractService>.Instance);

    [Fact]
    public async Task Extracts_MinimalGma()
    {
        using var dir = new TempDir();
        var gmaPath = Path.Combine(dir.Path, "test.gma");
        File.WriteAllBytes(gmaPath, BuildMinimalGma());

        var outDir = Path.Combine(dir.Path, "out");
        await _extract.ExtractAsync(gmaPath, outDir);

        Assert.True(File.Exists(Path.Combine(outDir, "lua/init.lua")));
        Assert.Equal("print('hi')\n", await File.ReadAllTextAsync(Path.Combine(outDir, "lua/init.lua")));
        Assert.True(File.Exists(Path.Combine(outDir, "models/foo.mdl")));
        Assert.Equal(3, new FileInfo(Path.Combine(outDir, "models/foo.mdl")).Length);
    }

    private static byte[] BuildMinimalGma()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: false);
        bw.Write(Encoding.ASCII.GetBytes("GMAD"));
        bw.Write((byte)3);
        bw.Write(0L); // steamid
        bw.Write(DateTimeOffset.UtcNow.ToUnixTimeSeconds()); // timestamp
        WriteNullString(bw, ""); // required content list terminator (empty list)
        WriteNullString(bw, "Test Addon");
        WriteNullString(bw, "test addon description");
        WriteNullString(bw, "tester");
        bw.Write(1); // addon version

        // File entry headers come first (filenum, name, size, crc) — bodies follow after filenum=0.
        var body1 = Encoding.UTF8.GetBytes("print('hi')\n");
        var body2 = new byte[] { 1, 2, 3 };

        bw.Write(1u); WriteNullString(bw, "lua/init.lua");  bw.Write((long)body1.Length); bw.Write(0u);
        bw.Write(2u); WriteNullString(bw, "models/foo.mdl"); bw.Write((long)body2.Length); bw.Write(0u);
        bw.Write(0u); // end of entries

        // Now the bodies, contiguous.
        bw.Write(body1);
        bw.Write(body2);

        bw.Write(0u); // trailing CRC32 (we don't verify)
        return ms.ToArray();
    }

    private static void WriteNullString(BinaryWriter bw, string s)
    {
        bw.Write(Encoding.UTF8.GetBytes(s));
        bw.Write((byte)0);
    }
}