using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GmodAddonAnalyzer.Tests;

public sealed class SmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SmokeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(b =>
        {
            var dataRoot = Path.Combine(Path.GetTempPath(), "gaa-test-" + Guid.NewGuid().ToString("N")[..8]);
            b.UseSetting("Analyzer:DataRoot", dataRoot);
            b.UseSetting("Analyzer:PowDifficulty", "4");
        });
    }

    [Fact]
    public async Task HomePage_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task HistoryPage_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/History");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task AdminPage_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/Admin");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task ReportPage_ForUnknownAddon_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/report/123");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task NotFoundPage_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/NotFound");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task PowDisabled_DifficultyIsZero()
    {
        var factory = _factory.WithWebHostBuilder(b => b.UseSetting("Analyzer:PowEnabled", "false"));
        var client = factory.CreateClient();
        var r = await client.GetAsync("/pow/challenge?addonId=42");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var json = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("difficulty").GetInt32());
        Assert.Equal("disabled", json.GetProperty("seed").GetString());
    }

    [Fact]
    public async Task LuaTagsPage_ForUnknownAddon_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/luatags/123");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task AdminPage_WhenNoPassword_ShowsNotConfiguredMessage()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/Admin");
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("not configured", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FileDownload_ForMissingPath_Returns404()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/file/123?path=nope.lua");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}