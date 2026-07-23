using System.Security.Cryptography;
using GmodAddonAnalyzer.Configuration;

namespace GmodAddonAnalyzer.Services;

public sealed class AdminGuardService
{
    private readonly AnalyzerOptions _options;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, AttemptState> _attempts = new(StringComparer.Ordinal);

    public AdminGuardService(AnalyzerOptions options) => _options = options;

    public bool IsConfigured => !string.IsNullOrEmpty(_options.AdminPassword);

    public bool IsLockedOut(string ip, out DateTime unlockAt)
    {
        unlockAt = DateTime.MinValue;
        if (string.IsNullOrEmpty(ip)) return false;
        lock (_lock)
        {
            if (_attempts.TryGetValue(ip, out var s) && s.LockedUntil > DateTime.UtcNow)
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
            if (_attempts.TryGetValue(ip, out var s) && s.LockedUntil > DateTime.UtcNow)
                return false;
        }

        var ok = ConstantTimeEquals(_options.AdminPassword, password ?? "");
        lock (_lock)
        {
            if (_attempts.TryGetValue(ip, out var s))
            {
                if (ok) _attempts.Remove(ip);
                else
                {
                    s.Fails++;
                    if (s.Fails >= _options.AdminMaxAttempts)
                        s.LockedUntil = DateTime.UtcNow.AddMinutes(_options.AdminLockoutMinutes);
                }
            }
            else if (!ok)
            {
                _attempts[ip] = new AttemptState { Fails = 1 };
            }
        }
        return ok;
    }

    public const string CookieName = "gaa_admin";
    public const string CookieValue = "ok";

    private static bool ConstantTimeEquals(string a, string b)
    {
        var ab = System.Text.Encoding.UTF8.GetBytes(a);
        var bb = System.Text.Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(ab, bb);
    }

    private sealed class AttemptState
    {
        public int Fails;
        public DateTime LockedUntil;
    }
}