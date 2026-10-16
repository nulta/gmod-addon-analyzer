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
    // Serializes the dedupe check + insert so two concurrent /analyze requests
    // cannot create duplicate runs for the same addon.
    private static readonly SemaphoreSlim SubmitGate = new(1, 1);

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

            await SubmitGate.WaitAsync();
            try
            {
                var existing = await db.AnalysisRuns
                    .Where(r => r.AddonId == req.AddonId && r.Status != AnalysisStatus.Done
                                                           && r.Status != AnalysisStatus.FetchFailed
                                                           && r.Status != AnalysisStatus.ExtractFailed)
                    .FirstOrDefaultAsync();
                if (existing is not null)
                    return Results.Redirect($"/report/{existing.AddonId}?run={existing.Id}");

                // Dedupe only suppresses in-flight runs; without a cooldown,
                // re-submitting an already-analyzed addon always re-downloads it,
                // and every submission just queues behind a fully serialized
                // (one SteamCMD at a time) pipeline.
                var latest = await db.AnalysisRuns
                    .Where(r => r.AddonId == req.AddonId)
                    .OrderByDescending(r => r.SubmittedAt)
                    .FirstOrDefaultAsync();
                if (latest is not null
                    && (DateTime.UtcNow - latest.SubmittedAt).TotalMinutes < options.ResubmitCooldownMinutes)
                    return Results.Redirect($"/report/{latest.AddonId}?run={latest.Id}");

                // Backstop for the in-memory queue across restarts: reject when
                // too many rows are already stuck in a non-terminal state.
                var pending = await db.AnalysisRuns
                    .CountAsync(r => r.Status == AnalysisStatus.Pending || r.Status == AnalysisStatus.Fetching
                                  || r.Status == AnalysisStatus.Extracting || r.Status == AnalysisStatus.Analyzing);
                if (pending >= Math.Max(1, options.MaxPendingAnalysisRuns))
                    return Results.Json(new { error = "queue_full" },
                        statusCode: StatusCodes.Status503ServiceUnavailable);

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

                // Bounded channel: enqueue must not orphan a Pending row.
                if (!channel.Writer.TryWrite(new AnalysisRequest(run.Id, run.AddonId)))
                {
                    run.Status = AnalysisStatus.FetchFailed;
                    run.ErrorMessage = "Analysis queue is full; the run was not started. Try again later.";
                    await db.SaveChangesAsync();
                    return Results.Json(new { error = "queue_full" },
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                return Results.Redirect($"/report/{run.AddonId}?run={run.Id}");
            }
            finally
            {
                SubmitGate.Release();
            }
        }).RequireRateLimiting("analyze");

        app.MapGet("/status/{runId:long}", async (long runId, IDbContextFactory<AnalyzerDbContext> dbFactory,
            AdminGuardService guard, HttpContext ctx) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var run = await db.AnalysisRuns.FirstOrDefaultAsync(r => r.Id == runId);
            if (run is null) return Results.NotFound();

            // Error text can contain server internals (paths, logs); only expose
            // the detail to the authenticated admin. Everyone else sees the status only.
            var showDetail = guard.IsConfigured
                && guard.ValidateToken(ctx.Request.Cookies[AdminGuardService.CookieName]);
            return Results.Json(new StatusDto(run.Status.ToString(), showDetail ? run.ErrorMessage : null));
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

            string fullPath;
            try
            {
                // The DB lookup already whitelists known paths; this canonical check
                // is defense in depth against future data issues.
                fullPath = SafePath.Resolve(store.ExtractedDir(addonId), path);
            }
            catch (PathSafetyException)
            {
                return Results.BadRequest("bad path");
            }
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