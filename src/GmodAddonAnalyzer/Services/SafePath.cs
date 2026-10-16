using System.Buffers;

namespace GmodAddonAnalyzer.Services;

/// <summary>
/// Guards against path traversal ("zip slip") from attacker-controlled names
/// that originate inside GMA files or tar archives, or from query strings.
/// </summary>
public static class SafePath
{
    private static readonly SearchValues<char> Forbidden = SearchValues.Create(":*?\"<>|");

    /// <summary>
    /// Resolves an untrusted, archive-relative (or query-supplied) path safely inside
    /// <paramref name="baseDir"/>. Throws <see cref="PathSafetyException"/> for rooted
    /// paths, traversal segments, or otherwise hostile input.
    /// </summary>
    public static string Resolve(string baseDir, string relative)
    {
        if (string.IsNullOrEmpty(relative))
            throw new PathSafetyException("Empty path.");

        if (relative.Contains('\0'))
            throw new PathSafetyException("NUL byte in path.");

        // Catches "/abs", "\abs", "C:\x", "C:x", UNC shares, etc. on each platform.
        if (Path.IsPathRooted(relative))
            throw new PathSafetyException($"Rooted path rejected: {relative}");

        var normalized = relative.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            throw new PathSafetyException($"Path resolves to nothing: {relative}");

        foreach (var seg in segments)
        {
            if (seg is "." or "..")
                throw new PathSafetyException($"Path traversal rejected: {relative}");
            if (seg.IndexOfAny(Forbidden) >= 0)
                throw new PathSafetyException($"Illegal characters in path: {relative}");
            // Control characters (incl. \n/\r/\t) enable log forging and hostile
            // filenames; nothing in a legitimate addon needs them.
            if (seg.Any(char.IsControl))
                throw new PathSafetyException($"Control characters in path: {relative}");
        }

        var candidate = Path.GetFullPath(Path.Combine(baseDir, string.Join('/', segments)));
        var baseFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDir))
                       + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(baseFull, StringComparison.Ordinal))
            throw new PathSafetyException($"Path escapes base directory: {relative}");

        return candidate;
    }
}

public sealed class PathSafetyException(string message) : Exception(message);