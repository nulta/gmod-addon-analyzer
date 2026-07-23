using System.Net.Http.Json;
using System.Text.Json;
using GmodAddonAnalyzer.Configuration;
using Microsoft.Extensions.Logging;

namespace GmodAddonAnalyzer.Services;

public readonly record struct WorkshopMetadata(
    long PublishedFileId,
    string Title,
    DateTime UpdatedAt,
    long FileSize);

public sealed class WorkshopApiException(string message) : Exception(message);

public sealed class WorkshopService
{
    private readonly HttpClient _http;
    private readonly ILogger<WorkshopService> _logger;

    public WorkshopService(IHttpClientFactory httpFactory, ILogger<WorkshopService> logger)
    {
        _http = httpFactory.CreateClient("workshop");
        _logger = logger;
    }

    public async Task<WorkshopMetadata?> GetMetadataAsync(long addonId, CancellationToken ct = default)
    {
        try
        {
            var payload = new { itemcount = 1, publishedfileids = new[] { addonId } };
            var resp = await _http.PostAsJsonAsync(
                "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/",
                FormUrlEncode(payload), ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Steam metadata API returned {Status}", resp.StatusCode);
                return null;
            }
            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("response", out var respEl)) return null;
            if (!respEl.TryGetProperty("publishedfiledetails", out var detailsArr)) return null;
            if (detailsArr.GetArrayLength() == 0) return null;
            var d = detailsArr[0];
            var title = d.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
            var updated = d.TryGetProperty("time_updated", out var tu) ? tu.GetInt64() : 0L;
            var fileSize = d.TryGetProperty("file_size", out var fs) ? fs.GetInt64() : 0L;
            return new WorkshopMetadata(addonId, title,
                DateTimeOffset.FromUnixTimeSeconds(updated).UtcDateTime,
                fileSize);
        }
        catch (Exception ex) when (ex is not WorkshopApiException)
        {
            _logger.LogWarning(ex, "Failed to fetch workshop metadata for {AddonId}", addonId);
            return null;
        }
    }

    private static Dictionary<string, string> FormUrlEncode(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);
        var dict = new Dictionary<string, string>();
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            if (p.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in p.Value.EnumerateArray())
                    dict[p.Name + "[]"] = item.ToString();
            }
            else
            {
                dict[p.Name] = p.Value.ToString();
            }
        }
        return dict;
    }
}