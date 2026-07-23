using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

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
}