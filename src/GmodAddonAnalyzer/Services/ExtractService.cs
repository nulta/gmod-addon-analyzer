using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging;
using SharpCompress.Compressors.LZMA;

namespace GmodAddonAnalyzer.Services;

public sealed class ExtractException(string message) : Exception(message);

public sealed class ExtractService
{
    // Hard resource limits: archives come from untrusted Steam Workshop uploads,
    // so unbounded entry counts/names/sizes would allow trivial disk/memory DoS.
    public const int MaxEntries = 100_000;
    public const int MaxNameBytes = 4096;
    public const long MaxEntryBytes = 1L << 30;   // 1 GiB per file
    public const long MaxTotalBytes = 4L << 30;   // 4 GiB per archive

    private readonly ILogger<ExtractService> _logger;

    public ExtractService(ILogger<ExtractService> logger) => _logger = logger;

    public async Task ExtractAsync(string archivePath, string destDir, CancellationToken ct = default)
    {
        Directory.CreateDirectory(destDir);
        await using var fs = File.OpenRead(archivePath);

        // Sniff the container. SteamCMD hands us a plain GMA for modern uploads,
        // gzip+tar for some old uploads, and an LZMA-alone stream for pre-GMod-13
        // files (Steam names them <id>_<hash>_legacy.bin and does NOT decompress
        // them; the payload is an LZMA-wrapped GMA).
        var head = new byte[13];
        var headLen = await ReadExactOptAsync(fs, head, 13, ct);

        if (headLen >= 2 && head[0] == 0x1f && head[1] == 0x8b)
        {
            fs.Position = 0;
            await ExtractLegacyAsync(fs, destDir, ct);
            return;
        }

        if (headLen >= 4 && Encoding.ASCII.GetString(head, 0, 4) == "GMAD")
        {
            fs.Position = 0;
            await ExtractGmaAsync(fs, destDir, ct);
            return;
        }

        if (headLen == 13 && TryReadLzmaAloneHeader(head, out var lzmaProps, out var plainSize))
        {
            if (plainSize >= 0 && plainSize > MaxTotalBytes)
                throw new ExtractException("LZMA payload declares an uncompressed size over the limit.");
            fs.Position = 13;
            // Streaming decode (no full buffering) with the size taken from the
            // header; downstream GMA/tar caps still bound what gets extracted.
            await using Stream lzma = LzmaStream.Create(lzmaProps, fs, fs.Length - 13, plainSize, leaveOpen: true);
            await ExtractGmaAsync(lzma, destDir, ct);
            return;
        }

        throw new ExtractException(
            $"Not a GMA file (magic={Encoding.ASCII.GetString(head, 0, Math.Min(4, headLen))})");
    }

    /// <summary>Parses a GMA whose stream is positioned right at the "GMAD" magic.</summary>
    private async Task ExtractGmaAsync(Stream fs, string destDir, CancellationToken ct)
    {
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
        var declaredTotal = 0L;
        while (true)
        {
            var fileNum = BitConverter.ToUInt32(await ReadExactAsync(fs, 4, ct), 0);
            if (fileNum == 0) break;
            if (entries.Count >= MaxEntries)
                throw new ExtractException($"Too many GMA entries (limit {MaxEntries}).");
            var name = await ReadNullStringAsync(fs, ct);
            var size = BitConverter.ToInt64(await ReadExactAsync(fs, 8, ct), 0);
            await ReadExactAsync(fs, 4, ct); // crc
            _logger.LogInformation("GMA entry #{Num} name='{Name}' size={Size}", fileNum, LogSafe(name), size);
            if (string.IsNullOrEmpty(name))
                throw new ExtractException($"GMA entry #{fileNum} had empty name");
            if (size < 0)
                throw new ExtractException($"GMA entry #{fileNum} declared negative size.");
            if (size > MaxEntryBytes)
                throw new ExtractException($"GMA entry #{fileNum} exceeds the per-file size limit.");
            declaredTotal += size;
            if (declaredTotal > MaxTotalBytes)
                throw new ExtractException("GMA exceeds the total extracted size limit.");
            entries.Add((name, size));
        }

        // Now the file bodies are contiguous starting at the current position.
        foreach (var (name, size) in entries)
        {
            ct.ThrowIfCancellationRequested();
            var outPath = SafePath.Resolve(destDir, name);
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
        // Async 1-byte reads (not ReadByte) keep every read on the same sync/
        // async path — some stream wrappers track that and refuse to mix them.
        var bytes = new List<byte>(32);
        var one = new byte[1];
        while (true)
        {
            var n = await s.ReadAsync(one.AsMemory(0, 1), ct);
            if (n == 0) throw new ExtractException("Unexpected end of stream in string.");
            if (one[0] == 0) break;
            if (bytes.Count >= MaxNameBytes)
                throw new ExtractException("Excessively long string in archive header.");
            bytes.Add(one[0]);
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    /// <summary>
    /// LZMA-alone header sniffing: 1 props byte + 4 dict-size LE bytes + 8
    /// uncompressed-size LE bytes. Strict enough to avoid misclassifying
    /// non-LZMA files (e.g. a raw ZIP fails the lc+lp decoder constraint).
    /// Returns the 5 decoder property bytes and the declared output size
    /// (-1 when the header uses the unknown-size sentinel).
    /// </summary>
    internal static bool TryReadLzmaAloneHeader(ReadOnlySpan<byte> head, out byte[] props, out long plainSize)
    {
        props = [];
        plainSize = 0;
        var p = head[0];
        if (p >= 225) return false;                       // lc/lp/pb encoding invalid
        var lc = p % 9;
        var lp = (p / 9) % 5;
        var pb = (p / 9) / 5;
        if (lc + lp > 4 || pb > 4) return false;          // decoder constraints
        var dict = BitConverter.ToUInt32(head[1..5]);
        if (dict is < 4096 or > 0x40000000u) return false; // plausible dictionary size
        var plain = BitConverter.ToUInt64(head[5..13]);
        if (plain != ulong.MaxValue && plain == 0) return false;
        props = [head[0], head[1], head[2], head[3], head[4]];
        plainSize = plain == ulong.MaxValue ? -1 : unchecked((long)plain);
        return true;
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
        // The GMA path caps entries + total bytes; the legacy path must too, or a
        // gzip bomb decompressing to terabytes of zeros (or millions of empty
        // entries) exhausts disk/inodes before the TTL sweeper can react.
        long entryCount = 0;
        long totalBytes = 0;
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

            entryCount++;
            if (entryCount > MaxEntries)
                throw new ExtractException($"Too many archive entries (limit {MaxEntries}).");

            var sizeStr = Encoding.ASCII.GetString(header, 124, 12).TrimEnd('\0', ' ').Trim();
            long size = sizeStr.Length > 0 ? Convert.ToInt64(sizeStr, 8) : 0;
            if (size < 0) throw new ExtractException($"Tar entry '{name}' declared negative size.");
            if (size > MaxEntryBytes)
                throw new ExtractException($"Tar entry '{name}' exceeds the per-file size limit.");
            totalBytes += size;
            if (totalBytes > MaxTotalBytes)
                throw new ExtractException("Archive exceeds the total extracted size limit.");

            var typeFlag = (char)header[156];

            if (typeFlag == '5')
            {
                Directory.CreateDirectory(SafePath.Resolve(destDir, name));
                continue;
            }

            if (typeFlag is '\0' or '0')
            {
                ct.ThrowIfCancellationRequested();
                var outPath = SafePath.Resolve(destDir, name);
                var outDir = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
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
                // Non-regular entries (links, devices, ...) are never extracted; skip
                // their bytes with a small buffer instead of allocating `size` at once.
                if (size > 0)
                {
                    var skip = (size + 511) / 512 * 512;
                    var discard = new byte[64 * 1024];
                    while (skip > 0)
                    {
                        var n = (int)Math.Min(skip, discard.Length);
                        var r = await ReadExactOptAsync(tar, discard, n, ct);
                        if (r == 0) break;
                        skip -= r;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Untrusted archive names reach the log; strip line breaks before logging so
    /// forged multi-line entries cannot masquerade as other log authors, and cap
    /// the length.
    /// </summary>
    internal static string LogSafe(string s)
    {
        var cleaned = s.Replace("\r", "").Replace("\n", " ");
        return cleaned.Length > 120 ? cleaned[..120] + "…" : cleaned;
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