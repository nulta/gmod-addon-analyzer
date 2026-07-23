using GmodAddonAnalyzer.Configuration;
using GmodAddonAnalyzer.Services;

namespace GmodAddonAnalyzer.Tests;

public sealed class AdminGuardTests
{
    private static AdminGuardService New(string password, int max = 3, int lockoutMin = 15) =>
        new(new AnalyzerOptions { AdminPassword = password, AdminMaxAttempts = max, AdminLockoutMinutes = lockoutMin });

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
}