using System.Net;
using System.Threading.Channels;
using System.Threading.RateLimiting;
using GmodAddonAnalyzer.Configuration;
using GmodAddonAnalyzer.Data;
using GmodAddonAnalyzer.Hosting;
using GmodAddonAnalyzer.Services;
using GmodAddonAnalyzer.Web.Endpoints;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages(o =>
{
    // Apply the per-IP "admin" policy to the admin page's endpoints as well.
    o.Conventions.AddPageApplicationModelConvention("/Admin",
        model => model.EndpointMetadata.Add(new EnableRateLimitingAttribute("admin")));
});

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Named policies are partitioned per client IP. The previous (unpartitioned)
    // AddFixedWindowLimiter created one global bucket shared by *all* clients, so
    // a single host could starve every other user of the service.
    o.AddPolicy<string>("pow", ctx => RateLimitPartition.GetFixedWindowLimiter(
        PartitionKey(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy<string>("analyze", ctx => RateLimitPartition.GetFixedWindowLimiter(
        PartitionKey(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy<string>("admin", ctx => RateLimitPartition.GetFixedWindowLimiter(
        PartitionKey(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));

    // Blanket per-IP cap for everything else (pages, static files, admin login...).
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            PartitionKey(ctx),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 240, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddHttpClient("workshop");

var analyzerOptions = builder.Configuration
    .GetSection(AnalyzerOptions.SectionName)
    .Get<AnalyzerOptions>() ?? new AnalyzerOptions();
builder.Services.AddSingleton(analyzerOptions);

var dataRoot = Path.GetFullPath(analyzerOptions.DataRoot, builder.Environment.ContentRootPath);
Directory.CreateDirectory(dataRoot);
var dbPath = Path.Combine(dataRoot, "analyzer.db");
builder.Services.AddDbContextFactory<AnalyzerDbContext>(o =>
    o.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddSingleton<ArtifactStore>();
builder.Services.AddSingleton<PowService>();
builder.Services.AddSingleton<WorkshopService>();
builder.Services.AddSingleton<FetchService>();
builder.Services.AddSingleton<ExtractService>();
builder.Services.AddSingleton<AddonAnalyzer>();
builder.Services.AddSingleton<LuaAnalyzer>();
builder.Services.AddSingleton<AnalysisPipeline>();
builder.Services.AddSingleton<CleanupService>();
builder.Services.AddSingleton<AdminGuardService>();

// Bounded queue: submission cannot flood a fully serialized pipeline backlog.
builder.Services.AddSingleton(Channel.CreateBounded<AnalysisRequest>(
    new BoundedChannelOptions(Math.Max(1, analyzerOptions.MaxQueuedAnalyses))
    {
        SingleReader = true,
        SingleWriter = false,
    }));
builder.Services.AddHostedService<AnalysisPipelineHostedService>();
builder.Services.AddHostedService<CleanupHostedService>();

var app = builder.Build();

// Reconstruct client IPs behind a reverse proxy. Only loopback proxies are trusted
// by default; configure Analyzer:KnownProxies (IPs or CIDRs) before trusting anything else.
if (analyzerOptions.TrustProxy)
{
    var forwardedHeaders = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    };
    foreach (var entry in analyzerOptions.KnownProxies)
    {
        var slash = entry.IndexOf('/');
        if (slash > 0
            && IPAddress.TryParse(entry[..slash], out var prefix)
            && int.TryParse(entry[(slash + 1)..], out var prefixLength))
        {
            forwardedHeaders.KnownIPNetworks.Add(new System.Net.IPNetwork(prefix, prefixLength));
        }
        else if (IPAddress.TryParse(entry, out var ip))
        {
            forwardedHeaders.KnownProxies.Add(ip);
        }
        else
        {
            throw new InvalidOperationException(
                $"Analyzer:KnownProxies entry '{entry}' is not a valid IP address or CIDR network.");
        }
    }
    app.UseForwardedHeaders(forwardedHeaders);
}

app.Use(async (ctx, next) =>
{
    // Cheap defense-in-depth: no sniffer content-types, no framing, no referrer
    // leakage. CSP keeps script-src tight ('self'); the blob: worker source is
    // only used by the proof-of-work client.
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    h["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; worker-src 'self' blob:; " +
        "style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
        "frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    await next();
});

using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AnalyzerDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStatusCodePages(context =>
{
    // Rate-limiter rejections must keep their 429 status (no redirect, no body swap),
    // otherwise clients can't tell they were throttled and abuse the retry.
    if (context.HttpContext.Response.StatusCode == StatusCodes.Status429TooManyRequests)
        return Task.CompletedTask;
    if (context.HttpContext.Request.Path.StartsWithSegments("/api"))
        return Task.CompletedTask;
    // JSON endpoint; a 404 there must not turn into an HTML redirect for pollers.
    if (context.HttpContext.Request.Path.StartsWithSegments("/status"))
        return Task.CompletedTask;
    context.HttpContext.Response.Redirect("/NotFound");
    return Task.CompletedTask;
});

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthorization();
app.MapRazorPages();
app.MapStatusEndpoints();

app.Run();

static string PartitionKey(HttpContext ctx) =>
    ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

public partial class Program;