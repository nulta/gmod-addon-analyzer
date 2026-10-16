using System.Security.Cryptography;
using System.Text;
using GmodAddonAnalyzer.Configuration;

namespace GmodAddonAnalyzer.Services;

public sealed class AdminGuardService
{
    // Bounded size so an attacker with many source IPs (e.g. an IPv6 /64)
    // cannot grow this in-memory table without limit.
    private const int MaxTrackedIps = 5000;

    private readonly AnalyzerOptions _options;
    private readonly byte[] _key;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, AttemptState> _attempts = new(StringComparer.Ordinal);

    // Distributed brute force (many rotating IPs, each under the per-IP lockout)
    // is throttled by a global failure budget per lockout window.
    private int _globalFails;
    private DateTime _globalWindowStart = DateTime.MinValue;
    private DateTime _globalLockUntil;

    public const string CookieName = "gaa_admin";

    public AdminGuardService(AnalyzerOptions options)
    {
        _options = options;
        // Derive the session-signing key from the admin password. Rotating the
        // password therefore invalidates every outstanding session token, and a
        // cookie value can never be replayed against another deployment.
        _key = SHA256.HashData(Encoding.UTF8.GetBytes($"gaa_admin_session_v1:{options.AdminPassword}"));
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_options.AdminPassword);

    public bool IsLockedOut(string ip, out DateTime unlockAt)
    {
        unlockAt = DateTime.MinValue;
        lock (_lock)
        {
            if (_globalLockUntil > DateTime.UtcNow)
            {
                unlockAt = _globalLockUntil;
                return true;
            }
            if (!string.IsNullOrEmpty(ip) && _attempts.TryGetValue(ip, out var s) && s.LockedUntil > DateTime.UtcNow)
            {
                unlockAt = s.LockedUntil;
                return true;
            }
        }
        return false;
    }

    public bool TryLogin(string password, string ip)
    {
        if (string.IsNullOrEmpty(_options.AdminPassword))
            return false;

        lock (_lock)
        {
            var now = DateTime.UtcNow;
            if (_globalLockUntil > now) return false;
            if (!string.IsNullOrEmpty(ip) && _attempts.TryGetValue(ip, out var s) && s.LockedUntil > now)
                return false;
        }

        var ok = ConstantTimeEquals(_options.AdminPassword, password ?? "");
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            if (_globalLockUntil > now) return false;

            if (ok)
            {
                _attempts.Remove(ip);
                return true;
            }

            var ipKey = string.IsNullOrEmpty(ip) ? "unknown" : ip;

            // Global failure budget: resets every lockout window. Successful
            // logins never count, so a legitimate admin typing correctly at the
            // same time as an attack is only affected while _globalLockUntil holds.
            if ((now - _globalWindowStart).TotalMinutes >= Math.Max(1, _options.AdminLockoutMinutes))
            {
                _globalWindowStart = now;
                _globalFails = 0;
            }
            _globalFails++;
            var globalMax = Math.Max(_options.AdminMaxAttempts, _options.AdminMaxGlobalAttempts);
            if (_globalFails >= globalMax)
            {
                _globalLockUntil = now.AddMinutes(_options.AdminLockoutMinutes);
                _globalFails = 0;
            }

            if (_attempts.TryGetValue(ip, out var s))
            {
                s.Fails++;
                s.LastFailAt = now;
                if (s.Fails >= _options.AdminMaxAttempts)
                    s.LockedUntil = now.AddMinutes(_options.AdminLockoutMinutes);
            }
            else
            {
                EvictIfNeeded();
                _attempts[ipKey] = new AttemptState { Fails = 1, LastFailAt = now };
            }
        }
        return ok;
    }

    /// <summary>
    /// Issues an expiring, HMAC-signed session token. The previous design used a
    /// static constant cookie value, which was trivially forgeable.
    /// </summary>
    public string IssueToken()
    {
        var expiry = DateTime.UtcNow.AddMinutes(_options.AdminSessionMinutes);
        var payload = $"v1|{expiry.Ticks}";
        var sig = Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
        return $"{sig}.{Convert.ToBase64String(Encoding.UTF8.GetBytes(payload))}";
    }

    public bool ValidateToken(string? token)
    {
        if (!IsConfigured || string.IsNullOrEmpty(token)) return false;

        var dot = token.IndexOf('.');
        if (dot <= 0 || dot == token.Length - 1) return false;

        byte[] given;
        try { given = Convert.FromHexString(token[..dot]); }
        catch (FormatException) { return false; }

        string payload;
        try { payload = Encoding.UTF8.GetString(Convert.FromBase64String(token[(dot + 1)..])); }
        catch (FormatException) { return false; }

        var expected = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload));
        if (!CryptographicOperations.FixedTimeEquals(expected, given)) return false;

        var parts = payload.Split('|');
        if (parts.Length != 2 || parts[0] != "v1" || !long.TryParse(parts[1], out var ticks))
            return false;
        return DateTime.UtcNow < new DateTime(ticks, DateTimeKind.Utc);
    }

    private void EvictIfNeeded()
    {
        if (_attempts.Count < MaxTrackedIps) return;
        var now = DateTime.UtcNow;
        foreach (var key in _attempts
                     .Where(kv => kv.Value.LockedUntil <= now)
                     .Select(kv => kv.Key)
                     .ToList())
        {
            _attempts.Remove(key);
        }
        while (_attempts.Count >= MaxTrackedIps)
        {
            // Drop the least-recently-failed IP rather than an arbitrary one;
            // random eviction would let an IP-churning attacker unwind other
            // users' lockouts.
            var oldest = _attempts.Values.Min(a => a.LastFailAt);
            _attempts.Remove(_attempts.First(kv => kv.Value.LastFailAt == oldest).Key);
        }
    }

    private static bool ConstantTimeEquals(string a, string b)
    {
        var ab = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(ab, bb);
    }

    private sealed class AttemptState
    {
        public int Fails;
        public DateTime LockedUntil;
        public DateTime LastFailAt = DateTime.MinValue;
    }
}