using System.Text.Json.Serialization;
using System.Threading.Channels;
using GmodAddonAnalyzer.Configuration;
using GmodAddonAnalyzer.Data;
using GmodAddonAnalyzer.Data.Entities;
using GmodAddonAnalyzer.Services;
using Microsoft.EntityFrameworkCore;

namespace GmodAddonAnalyzer.Web.Endpoints;

public static class StatusEndpoints
{
    public static void MapStatusEndpoints(this WebApplication app)
    {
        app.MapGet("/pow/challenge", (long addonId, PowService pow, AnalyzerOptions options) =>
        {
            if (addonId <= 0) return Results.BadRequest(new { error = "bad_addon_id" });
            if (!options.PowEnabled)
                return Results.Json(new PowChallengeDto("disabled", addonId, 0));
            var c = pow.Issue(addonId);
            return Results.Json(new PowChallengeDto(c.Seed, c.AddonId, c.Difficulty));
        }).RequireRateLimiting("pow");

        app.MapPost("/analyze", async (AnalyzeRequest req, PowService pow, AnalyzerOptions options,
            IDbContextFactory<AnalyzerDbContext> dbFactory, Channel<AnalysisRequest> channel, HttpContext ctx) =>
        {
            if (req.AddonId <= 0) return Results.BadRequest(new { error = "bad_addon_id" });
            if (options.PowEnabled && !pow.Verify(req.AddonId, req.Seed ?? "", req.Nonce))
                return Results.BadRequest(new { error = "bad_proof" });

            await using var db = await dbFactory.CreateDbContextAsync();

            var existing = await db.AnalysisRuns
                .Where(r => r.AddonId == req.AddonId && r.Status != AnalysisStatus.Done
                                                       && r.Status != AnalysisStatus.FetchFailed
                                                       && r.Status != AnalysisStatus.ExtractFailed)
                .FirstOrDefaultAsync();
            if (existing is not null)
                return Results.Redirect($"/report/{existing.AddonId}?run={existing.Id}");

            var run = new AnalysisRun
            {
                AddonId = req.AddonId,
                SubmittedAt = DateTime.UtcNow,
                Status = AnalysisStatus.Pending,
                SubmittedByIp = TruncateIp(ctx.Connection.RemoteIpAddress?.ToString()),
                PowNonce = req.Nonce,
            };
            db.AnalysisRuns.Add(run);
            await db.SaveChangesAsync();
            await channel.Writer.WriteAsync(new AnalysisRequest(run.Id, run.AddonId));
            return Results.Redirect($"/report/{run.AddonId}?run={run.Id}");
        }).RequireRateLimiting("analyze");

        app.MapGet("/status/{runId:long}", async (long runId, IDbContextFactory<AnalyzerDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var run = await db.AnalysisRuns.FirstOrDefaultAsync(r => r.Id == runId);
            if (run is null) return Results.NotFound();
            return Results.Json(new StatusDto(run.Status.ToString(), run.ErrorMessage));
        });

        app.MapGet("/api/file/{addonId:long}", async (long addonId, string path, IDbContextFactory<AnalyzerDbContext> dbFactory,
            ArtifactStore store) =>
        {
            if (string.IsNullOrEmpty(path)) return Results.BadRequest("missing path");
            await using var db = await dbFactory.CreateDbContextAsync();
            var addon = await db.Addons.FirstOrDefaultAsync(a => a.WorkshopId == addonId);
            if (addon is null) return Results.NotFound();
            var file = await db.AddonFiles.FirstOrDefaultAsync(f => f.AddonId == addon.Id && f.RelativePath == path);
            if (file is null) return Results.NotFound();
            var fullPath = System.IO.Path.Combine(store.ExtractedDir(addonId), path.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (!System.IO.File.Exists(fullPath)) return Results.NotFound("artifacts removed");
            var stream = System.IO.File.OpenRead(fullPath);
            var fileName = System.IO.Path.GetFileName(path);
            return Results.File(stream, contentType: "application/octet-stream", fileDownloadName: fileName);
        });
    }

    private static string? TruncateIp(string? ip)
    {
        if (string.IsNullOrEmpty(ip)) return null;
        var idx = ip.LastIndexOf('.');
        if (idx > 0) return ip[..idx] + ".0";
        var cidx = ip.LastIndexOf(':');
        if (cidx > 0) return ip[..cidx];
        return ip;
    }

    public sealed record AnalyzeRequest(long AddonId, string? Seed, long Nonce);

    public sealed record PowChallengeDto([property: JsonPropertyName("seed")] string Seed,
                                         [property: JsonPropertyName("addonId")] long AddonId,
                                         [property: JsonPropertyName("difficulty")] int Difficulty);

    public sealed record StatusDto([property: JsonPropertyName("status")] string Status,
                                   [property: JsonPropertyName("error")] string? Error);
}