using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging;

namespace GmodAddonAnalyzer.Services;

public sealed class ExtractException(string message) : Exception(message);

public sealed class ExtractService
{
    private readonly ILogger<ExtractService> _logger;

    public ExtractService(ILogger<ExtractService> logger) => _logger = logger;

    public async Task ExtractAsync(string archivePath, string destDir, CancellationToken ct = default)
    {
        Directory.CreateDirectory(destDir);
        await using var fs = File.OpenRead(archivePath);

        var first = fs.ReadByte();
        if (first == 0x1f && fs.ReadByte() == 0x8b)
        {
            fs.Position = 0;
            await ExtractLegacyAsync(fs, destDir, ct);
            return;
        }

        fs.Position = 0;
        var magic = await ReadExactAsync(fs, 4, ct);
        if (Encoding.ASCII.GetString(magic) != "GMAD")
            throw new ExtractException($"Not a GMA file (magic={Encoding.ASCII.GetString(magic)})");

        var format = (await ReadExactAsync(fs, 1, ct))[0];
        if (format is < 1 or > 3)
            throw new ExtractException($"Unsupported GMA format version {format}.");

        if (format >= 2) await ReadExactAsync(fs, 8, ct);
        await ReadExactAsync(fs, 8, ct);
        while (true)
        {
            var s = await ReadNullStringAsync(fs, ct);
            if (s.Length == 0) break;
        }
        await ReadNullStringAsync(fs, ct);
        await ReadNullStringAsync(fs, ct);
        await ReadNullStringAsync(fs, ct);
        if (format >= 2) await ReadExactAsync(fs, 4, ct);

        // File entry headers (filenum, name, size, crc) come first, all contiguous.
        // The bodies follow after a filenum=0 terminator. (Per Facepunch/gmad AddonReader.)
        var entries = new List<(string Name, long Size)>();
        while (true)
        {
            var fileNum = BitConverter.ToUInt32(await ReadExactAsync(fs, 4, ct), 0);
            if (fileNum == 0) break;
            var name = await ReadNullStringAsync(fs, ct);
            var size = BitConverter.ToInt64(await ReadExactAsync(fs, 8, ct), 0);
            await ReadExactAsync(fs, 4, ct); // crc
            _logger.LogInformation("GMA entry #{Num} name='{Name}' size={Size}", fileNum, name, size);
            if (string.IsNullOrEmpty(name))
                throw new ExtractException($"GMA entry #{fileNum} had empty name");
            entries.Add((name, size));
        }

        // Now the file bodies are contiguous starting at the current position.
        foreach (var (name, size) in entries)
        {
            var outPath = Path.Combine(destDir, name.Replace('/', Path.DirectorySeparatorChar));
            var outDir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
            await using var outFs = File.Create(outPath);
            await CopyExactAsync(fs, outFs, size, ct);
        }
    }

    private static async Task CopyExactAsync(Stream src, Stream dst, long remaining, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        while (remaining > 0)
        {
            var toRead = (int)Math.Min(buffer.Length, remaining);
            var read = await src.ReadAsync(buffer.AsMemory(0, toRead), ct);
            if (read == 0) throw new ExtractException("Unexpected end of GMA file body.");
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            remaining -= read;
        }
    }

    private static async Task<byte[]> ReadExactAsync(Stream s, int count, CancellationToken ct)
    {
        var buf = new byte[count];
        var total = 0;
        while (total < count)
        {
            var r = await s.ReadAsync(buf.AsMemory(total, count - total), ct);
            if (r == 0) throw new ExtractException("Unexpected end of stream.");
            total += r;
        }
        return buf;
    }

    private static async Task<string> ReadNullStringAsync(Stream s, CancellationToken ct)
    {
        var bytes = new List<byte>(32);
        while (true)
        {
            var b = s.ReadByte();
            if (b < 0) throw new ExtractException("Unexpected end of stream in string.");
            if (b == 0) break;
            bytes.Add((byte)b);
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private async Task ExtractLegacyAsync(Stream input, string destDir, CancellationToken ct)
    {
        Stream inner = new GZipStream(input, CompressionMode.Decompress, leaveOpen: false);
        try
        {
            await ExtractTarAsync(inner, destDir, ct);
        }
        catch (ExtractException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ExtractException($"Failed to extract legacy (.bin/gzip+tar) archive: {ex.Message}");
        }
    }

    private async Task ExtractTarAsync(Stream tar, string destDir, CancellationToken ct)
    {
        var header = new byte[512];
        while (true)
        {
            var read = await ReadExactOptAsync(tar, header, 512, ct);
            if (read == 0) break;
            if (read < 512) throw new ExtractException("Truncated tar header.");
            if (header.All(b => b == 0)) break;

            var nameRaw = header.AsSpan(0, 100).ToArray();
            var nameEnd = Array.IndexOf(nameRaw, (byte)0);
            if (nameEnd < 0) nameEnd = nameRaw.Length;
            var name = Encoding.UTF8.GetString(nameRaw, 0, nameEnd);
            if (string.IsNullOrEmpty(name)) continue;

            var sizeStr = Encoding.ASCII.GetString(header, 124, 12).TrimEnd('\0', ' ').Trim();
            long size = sizeStr.Length > 0 ? Convert.ToInt64(sizeStr, 8) : 0;

            var typeFlag = (char)header[156];

            var outPath = Path.Combine(destDir, name.Replace('/', Path.DirectorySeparatorChar));
            var outDir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);

            if (typeFlag == '5')
            {
                Directory.CreateDirectory(outPath);
                continue;
            }

            if (typeFlag is '\0' or '0')
            {
                await using var outFs = File.Create(outPath);
                var remaining = size;
                var buf = new byte[64 * 1024];
                while (remaining > 0)
                {
                    var toRead = (int)Math.Min(buf.Length, remaining);
                    var r = await tar.ReadAsync(buf.AsMemory(0, toRead), ct);
                    if (r == 0) throw new ExtractException("Truncated tar body.");
                    await outFs.WriteAsync(buf.AsMemory(0, r), ct);
                    remaining -= r;
                }
                var padding = (int)((512 - (size % 512)) % 512);
                if (padding > 0) await ReadExactOptAsync(tar, new byte[padding], padding, ct);
            }
            else
            {
                if (size > 0)
                {
                    var skip = (int)((size + 511) / 512 * 512);
                    await ReadExactOptAsync(tar, new byte[skip], skip, ct);
                }
            }
        }
    }

    private static async Task<int> ReadExactOptAsync(Stream s, byte[] buffer, int count, CancellationToken ct)
    {
        var total = 0;
        while (total < count)
        {
            var r = await s.ReadAsync(buffer.AsMemory(total, count - total), ct);
            if (r == 0) return total;
            total += r;
        }
        return total;
    }
}