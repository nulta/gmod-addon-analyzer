using System.Security.Cryptography;
using GmodAddonAnalyzer.Configuration;

namespace GmodAddonAnalyzer.Services;

public readonly record struct PowChallenge(string Seed, long AddonId, int Difficulty, DateTime CreatedAt);

public sealed class PowService
{
    private readonly AnalyzerOptions _options;
    private readonly object _lock = new();
    private readonly Dictionary<string, PowChallenge> _challenges = new();

    public PowService(AnalyzerOptions options) => _options = options;

    public PowChallenge Issue(long addonId)
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        var seed = Convert.ToHexString(bytes).ToLowerInvariant();
        var challenge = new PowChallenge(seed, addonId, _options.PowDifficulty, DateTime.UtcNow);
        lock (_lock)
        {
            PurgeExpired();
            _challenges[seed] = challenge;
        }
        return challenge;
    }

    public bool Verify(long addonId, string seed, long nonce)
    {
        if (string.IsNullOrEmpty(seed)) return false;
        PowChallenge challenge;
        lock (_lock)
        {
            if (!_challenges.Remove(seed, out challenge)) return false;
            PurgeExpired();
        }
        if (challenge.AddonId != addonId) return false;
        if ((DateTime.UtcNow - challenge.CreatedAt).TotalMinutes > _options.ChallengeTtlMinutes) return false;

        return LeadingZeroBits(Hash(seed, addonId, nonce)) >= challenge.Difficulty;
    }

    private void PurgeExpired()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-_options.ChallengeTtlMinutes);
        var expired = _challenges.Where(kv => kv.Value.CreatedAt < cutoff).Select(kv => kv.Key).ToList();
        foreach (var k in expired) _challenges.Remove(k);
    }

    internal static byte[] Hash(string seed, long addonId, long nonce)
    {
        var input = System.Text.Encoding.UTF8.GetBytes($"{seed}:{addonId}:{nonce}");
        return SHA256.HashData(input);
    }

    internal static int LeadingZeroBits(byte[] hash)
    {
        var bits = 0;
        foreach (var b in hash)
        {
            if (b == 0) { bits += 8; continue; }
            int z = 0;
            for (int mask = 0x80; mask > 0 && (b & mask) == 0; mask >>= 1) z++;
            bits += z;
            break;
        }
        return bits;
    }
}