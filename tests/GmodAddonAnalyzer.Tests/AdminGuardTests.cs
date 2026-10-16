using GmodAddonAnalyzer.Configuration;
using GmodAddonAnalyzer.Services;

namespace GmodAddonAnalyzer.Tests;

public sealed class AdminGuardTests
{
    private static AdminGuardService New(string password, int max = 3, int lockoutMin = 15, int globalMax = 1_000_000) =>
        new(new AnalyzerOptions
        {
            AdminPassword = password,
            AdminMaxAttempts = max,
            AdminLockoutMinutes = lockoutMin,
            AdminMaxGlobalAttempts = globalMax,
        });

    [Fact]
    public void CorrectPassword_LogsIn()
    {
        var g = New("secret");
        Assert.True(g.TryLogin("secret", "1.2.3.4"));
    }

    [Fact]
    public void WrongPassword_Fails()
    {
        var g = New("secret");
        Assert.False(g.TryLogin("wrong", "1.2.3.4"));
    }

    [Fact]
    public void RepeatedWrongPassword_LocksOut()
    {
        var g = New("secret", max: 3, lockoutMin: 1);
        for (int i = 0; i < 3; i++) g.TryLogin("no", "1.2.3.4");
        Assert.True(g.IsLockedOut("1.2.3.4", out _));
    }

    [Fact]
    public void LockedOut_EvenCorrectPasswordFails()
    {
        var g = New("secret", max: 2, lockoutMin: 1);
        g.TryLogin("no", "1.2.3.4");
        g.TryLogin("no", "1.2.3.4");
        Assert.True(g.IsLockedOut("1.2.3.4", out _));
        Assert.False(g.TryLogin("secret", "1.2.3.4"));
    }

    [Fact]
    public void CorrectPassword_ClearsFails()
    {
        var g = New("secret", max: 3);
        g.TryLogin("no", "1.2.3.4");
        g.TryLogin("no", "1.2.3.4");
        Assert.True(g.TryLogin("secret", "1.2.3.4"));
        Assert.False(g.IsLockedOut("1.2.3.4", out _));
    }

    [Fact]
    public void DifferentIps_Independent()
    {
        var g = New("secret", max: 2);
        g.TryLogin("no", "1.1.1.1");
        g.TryLogin("no", "1.1.1.1");
        Assert.True(g.IsLockedOut("1.1.1.1", out _));
        Assert.False(g.IsLockedOut("2.2.2.2", out _));
    }

    [Fact]
    public void EmptyPassword_NotConfigured()
    {
        var g = New("");
        Assert.False(g.IsConfigured);
        Assert.False(g.TryLogin("anything", "1.1.1.1"));
    }

    [Fact]
    public void IssuedToken_Validates()
    {
        var g = New("secret");
        var token = g.IssueToken();
        Assert.True(g.ValidateToken(token));
    }

    [Fact]
    public void TamperedToken_Fails()
    {
        var g = New("secret");
        var token = g.IssueToken();
        Assert.False(g.ValidateToken(token + "x"));
        Assert.False(g.ValidateToken(token[..^4]));
        Assert.False(g.ValidateToken("deadbeef.AAAAAA=="));
    }

    [Fact]
    public void TokenFromOtherPassword_Fails()
    {
        var g1 = New("secret-one");
        var g2 = New("secret-two");
        Assert.False(g2.ValidateToken(g1.IssueToken()));
    }

    [Fact]
    public void Token_WhenNotConfigured_Fails()
    {
        var g = New("");
        Assert.False(g.ValidateToken(g.IssueToken()));
        Assert.False(g.ValidateToken(null));
    }

    [Fact]
    public void GlobalFailCap_LocksOutFreshIps()
    {
        // Rotating IPs, each under the per-IP limit; the global budget must stop
        // distributed password guessing. (The global budget is enforced at
        // max(per-IP attempts, global attempts), so it is never looser.)
        var g = New("secret", max: 2, globalMax: 3);
        for (var i = 0; i < 3; i++) g.TryLogin("no", $"10.0.0.{i}");
        Assert.True(g.IsLockedOut("10.99.99.99", out _));
        Assert.False(g.TryLogin("secret", "10.99.99.99"));
    }

    [Fact]
    public void GlobalFailCap_SuccessfulLogin_ResetsBudget()
    {
        var g = New("secret", max: 5, globalMax: 3);
        g.TryLogin("no", "10.0.0.1");
        g.TryLogin("no", "10.0.0.2");
        Assert.True(g.TryLogin("secret", "10.0.0.3")); // success
        Assert.False(g.IsLockedOut("10.99.99.99", out _));
    }
}