using GmodAddonAnalyzer.Configuration;
using GmodAddonAnalyzer.Services;

namespace GmodAddonAnalyzer.Tests;

public sealed class PowServiceTests
{
    private static PowService NewSvc(int difficulty = 8) =>
        new(new AnalyzerOptions { PowEnabled = true, PowDifficulty = difficulty, ChallengeTtlMinutes = 5 });

    [Fact]
    public void IssuedChallenge_Verifies_WithValidNonce()
    {
        var svc = NewSvc(difficulty: 8);
        var c = svc.Issue(123);
        var nonce = Solve(c.Seed, c.AddonId, c.Difficulty);
        Assert.True(svc.Verify(c.AddonId, c.Seed, nonce));
    }

    [Fact]
    public void WrongAddonId_Fails()
    {
        var svc = NewSvc(4);
        var c = svc.Issue(1);
        var nonce = Solve(c.Seed, c.AddonId, c.Difficulty);
        Assert.False(svc.Verify(2, c.Seed, nonce));
    }

    [Fact]
    public void ReusedSeed_Fails()
    {
        var svc = NewSvc(4);
        var c = svc.Issue(1);
        var nonce = Solve(c.Seed, c.AddonId, c.Difficulty);
        Assert.True(svc.Verify(c.AddonId, c.Seed, nonce));
        Assert.False(svc.Verify(c.AddonId, c.Seed, nonce));
    }

    [Fact]
    public void LowDifficulty_NonceHasFewerZeroBits_Fails()
    {
        var svc = NewSvc(16);
        var c = svc.Issue(1);
        Assert.False(svc.Verify(c.AddonId, c.Seed, 0));
    }

    private static long Solve(string seed, long addonId, int difficulty)
    {
        for (long n = 0; n < 50_000_000L; n++)
        {
            var h = PowService.Hash(seed, addonId, n);
            if (PowService.LeadingZeroBits(h) >= difficulty) return n;
        }
        throw new InvalidOperationException("no solution found in budget");
    }
}