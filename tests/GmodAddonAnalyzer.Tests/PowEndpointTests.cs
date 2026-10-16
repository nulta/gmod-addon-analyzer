using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GmodAddonAnalyzer.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace GmodAddonAnalyzer.Tests;

public sealed class PowEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PowEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(b =>
        {
            var dataRoot = Path.Combine(Path.GetTempPath(), "gaa-test-" + Guid.NewGuid().ToString("N")[..8]);
            b.UseSetting("Analyzer:DataRoot", dataRoot);
            b.UseSetting("Analyzer:PowDifficulty", "4");
        });
    }

    [Fact]
    public async Task Challenge_ReturnsSeedAndDifficulty()
    {
        var client = _factory.CreateClient();
        var r = await client.GetAsync("/pow/challenge?addonId=104603291");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var json = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("seed", out var s));
        Assert.True(json.TryGetProperty("difficulty", out var d));
        Assert.True(d.GetInt32() > 0);
        Assert.False(string.IsNullOrEmpty(s.GetString()));
    }

    [Fact]
    public async Task Analyze_WithBadProof_Returns400()
    {
        var client = _factory.CreateClient();
        var r = await client.PostAsJsonAsync("/analyze", new { AddonId = 123L, Seed = "deadbeef", Nonce = 0L });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Analyze_WithValidProof_CreatesRun_And_ResubmitWithinCooldown_RedirectsToSameRun()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // Dummy SteamCMD binary: FetchAsync runs it, it fails to start, the
        // pipeline records FetchFailed — all offline and fast. Without this,
        // the first successful /analyze would actually download SteamCMD.
        var store = _factory.Services.GetRequiredService<ArtifactStore>();
        Directory.CreateDirectory(store.SteamCmdRoot);
        var exeName = OperatingSystem.IsWindows() ? "steamcmd.exe" : "steamcmd.sh";
        await File.WriteAllTextAsync(Path.Combine(store.SteamCmdRoot, exeName), "not a real steamcmd");

        const long addonId = 104_603_291L;

        async Task<(string Seed, int Difficulty)> GetChallengeAsync()
        {
            var r = await client.GetAsync($"/pow/challenge?addonId={addonId}");
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var json = await r.Content.ReadFromJsonAsync<JsonElement>();
            return (json.GetProperty("seed").GetString()!, json.GetProperty("difficulty").GetInt32());
        }

        static long SolvePow(string seed, long id, int difficulty)
        {
            for (var n = 0L; ; n++)
                if (PowService.LeadingZeroBits(PowService.Hash(seed, id, n)) >= difficulty)
                    return n;
        }

        var (seed, difficulty) = await GetChallengeAsync();
        var nonce = SolvePow(seed, addonId, difficulty);
        var r1 = await client.PostAsJsonAsync("/analyze", new { AddonId = addonId, Seed = seed, Nonce = nonce });
        Assert.Equal(HttpStatusCode.Found, r1.StatusCode);
        var location = r1.Headers.Location!.ToString();
        Assert.StartsWith($"/report/{addonId}?run=", location);
        var runId = long.Parse(location.Split('?')[1].Split('=')[1]);
        Assert.True(runId > 0);

        // Resubmitting immediately must not enqueue a second download (dedupe
        // while in flight, cooldown once terminal): same run, same redirect.
        var (seed2, difficulty2) = await GetChallengeAsync();
        var nonce2 = SolvePow(seed2, addonId, difficulty2);
        var r2 = await client.PostAsJsonAsync("/analyze", new { AddonId = addonId, Seed = seed2, Nonce = nonce2 });
        Assert.Equal(HttpStatusCode.Found, r2.StatusCode);
        Assert.Equal(location, r2.Headers.Location!.ToString());
    }
}