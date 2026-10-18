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

    [Fact]
    public async Task Extracts_RejectsTraversalEntryNames()
    {
        using var dir = new TempDir();
        var gmaPath = Path.Combine(dir.Path, "evil.gma");
        File.WriteAllBytes(gmaPath, BuildGmaWithEntry("..\\..\\evil.lua", "evil\n"));

        var outDir = Path.Combine(dir.Path, "out");
        await Assert.ThrowsAnyAsync<Exception>(() => _extract.ExtractAsync(gmaPath, outDir));
        Assert.False(File.Exists(Path.Combine(dir.Path, "evil.lua")));
    }

    [Fact]
    public async Task Extracts_RejectsRootedEntryNames()
    {
        using var dir = new TempDir();
        var gmaPath = Path.Combine(dir.Path, "evil.gma");
        File.WriteAllBytes(gmaPath, BuildGmaWithEntry("/evil-rooted.lua", "evil\n"));

        var outDir = Path.Combine(dir.Path, "out");
        await Assert.ThrowsAnyAsync<Exception>(() => _extract.ExtractAsync(gmaPath, outDir));
        Assert.Equal(0, Directory.GetFiles(Path.GetPathRoot(Path.GetFullPath(dir.Path))!, "*", SearchOption.TopDirectoryOnly)
            .Count(f => f.EndsWith("evil-rooted.lua", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Extracts_RejectsOversizedEntry()
    {
        using var dir = new TempDir();
        var gmaPath = Path.Combine(dir.Path, "big.gma");
        File.WriteAllBytes(gmaPath, BuildGmaWithEntry("lua/big.lua", "x", declaredSize: ExtractService.MaxEntryBytes + 1));

        var outDir = Path.Combine(dir.Path, "out");
        await Assert.ThrowsAnyAsync<Exception>(() => _extract.ExtractAsync(gmaPath, outDir));
    }

    [Fact]
    public async Task Extracts_RejectsGmaTotalSizeOverflow()
    {
        // Five entries of 1 GiB each: the declared-total check must reject during
        // header parsing (bodies are never read).
        using var dir = new TempDir();
        var gmaPath = Path.Combine(dir.Path, "bomb.gma");
        File.WriteAllBytes(gmaPath, BuildGmaHeaderOnly(
            ("a.lua", ExtractService.MaxEntryBytes),
            ("b.lua", ExtractService.MaxEntryBytes),
            ("c.lua", ExtractService.MaxEntryBytes),
            ("d.lua", ExtractService.MaxEntryBytes),
            ("e.lua", ExtractService.MaxEntryBytes)));

        var outDir = Path.Combine(dir.Path, "out");
        await Assert.ThrowsAnyAsync<Exception>(() => _extract.ExtractAsync(gmaPath, outDir));
    }

    [Fact]
    public async Task Extracts_RejectsTooManyTarEntries()
    {
        // The legacy gzip+tar path must enforce the same entry cap as the GMA
        // path (millions of empty entries would otherwise exhaust inodes).
        using var dir = new TempDir();
        var tarPath = Path.Combine(dir.Path, "bomb.gma"); // gzip magic routes to the tar path
        File.WriteAllBytes(tarPath, BuildGzipTarOfEntries(ExtractService.MaxEntries + 2));

        var outDir = Path.Combine(dir.Path, "out");
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => _extract.ExtractAsync(tarPath, outDir));
        Assert.Contains("Too many", ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Extracts_LzmaWrappedLegacyBin()
    {
        // Regression: SteamCMD ships pre-GMod-13 workshop files as
        // *_legacy.bin — an LZMA-alone stream wrapping a GMA. The analyzer used
        // to fail with "Not a GMA file (magic=]...)" (0x5D = LZMA props byte).
        // Fixture is the real download of workshop item 111158387.
        using var dir = new TempDir();
        var binPath = Path.Combine(dir.Path, "123_legacy.bin");
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "111158387_legacy.bin"),
            binPath);

        var outDir = Path.Combine(dir.Path, "out");
        await _extract.ExtractAsync(binPath, outDir);

        var lua = Path.Combine(outDir, "lua/weapons/gmod_tool/stools/unbreakable.lua");
        Assert.True(File.Exists(lua), "expected the addon's lua file to be extracted");
        Assert.Contains("unbreakable", (await File.ReadAllTextAsync(lua)).ToLowerInvariant(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Extracts_RejectsLzmaSizeBomb()
    {
        // Valid-looking LZMA-alone header whose declared uncompressed size
        // exceeds MaxTotalBytes must be rejected up front.
        using var dir = new TempDir();
        var binPath = Path.Combine(dir.Path, "bomb_legacy.bin");
        File.WriteAllBytes(binPath, new byte[]
        {
            0x5D, 0x00, 0x00, 0x00, 0x02,                    // props + 32 MiB dict
            0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00,  // uncompressed size = 64 GiB > 4 GiB
        });

        var outDir = Path.Combine(dir.Path, "out");
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => _extract.ExtractAsync(binPath, outDir));
        Assert.Contains("over the limit", ex.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds a gzipped tar with <paramref name="count"/> entries type '1'
    /// (hard link) of size 0 — parsed but never written to disk, so the test
    /// exercises the header loop cheaply.
    /// </summary>
    private static byte[] BuildGzipTarOfEntries(int count)
    {
        using var tar = new MemoryStream();
        var header = new byte[512];
        for (int i = 0; i < count; i++)
        {
            Array.Clear(header, 0, 512);
            var name = Encoding.ASCII.GetBytes("d" + i);
            Array.Copy(name, 0, header, 0, name.Length);
            Array.Copy(Encoding.ASCII.GetBytes("0000000\0"), 0, header, 124, 8); // size = 0 (octal)
            header[156] = (byte)'1'; // hard link: counted, but skipped without writes

            Array.Clear(header, 148, 8);
            long sum = 0;
            foreach (var b in header) sum += b;
            var cksum = Encoding.ASCII.GetBytes(Convert.ToString(sum, 8).PadLeft(6, '0') + "\0 ");
            Array.Copy(cksum, 0, header, 148, cksum.Length);

            tar.Write(header, 0, 512);
        }
        tar.Write(new byte[1024], 0, 1024); // two zero blocks terminate the archive
        tar.Position = 0;

        using var outMs = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(outMs, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
        {
            tar.CopyTo(gzip);
        }
        return outMs.ToArray();
    }

    private static byte[] BuildGmaWithEntry(string entryName, string body, long? declaredSize = null)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: false);
        WriteGmaHeader(bw);

        var bodyBytes = Encoding.UTF8.GetBytes(body);
        bw.Write(1u); WriteNullString(bw, entryName);
        bw.Write(declaredSize ?? bodyBytes.Length);
        bw.Write(0u);
        bw.Write(0u); // end of entries

        bw.Write(bodyBytes);
        bw.Write(0u); // trailing CRC32 (we don't verify)
        return ms.ToArray();
    }

    /// <summary>
    /// GMA whose entries only declare sizes (no bodies): used to exercise the
    /// declared-total size cap, which rejects during header parsing.
    /// </summary>
    private static byte[] BuildGmaHeaderOnly(params (string Name, long DeclaredSize)[] entries)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: false);
        WriteGmaHeader(bw);

        for (int i = 0; i < entries.Length; i++)
        {
            bw.Write((uint)(i + 1));
            WriteNullString(bw, entries[i].Name);
            bw.Write(entries[i].DeclaredSize);
            bw.Write(0u);
        }
        bw.Write(0u); // end of entries
        bw.Write(0u); // trailing CRC32 (we don't verify)
        return ms.ToArray();
    }

    private static void WriteGmaHeader(BinaryWriter bw)
    {
        bw.Write(Encoding.ASCII.GetBytes("GMAD"));
        bw.Write((byte)3);
        bw.Write(0L); // steamid
        bw.Write(DateTimeOffset.UtcNow.ToUnixTimeSeconds()); // timestamp
        WriteNullString(bw, ""); // required content list terminator (empty list)
        WriteNullString(bw, "Test Addon");
        WriteNullString(bw, "test addon description");
        WriteNullString(bw, "tester");
        bw.Write(1); // addon version
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