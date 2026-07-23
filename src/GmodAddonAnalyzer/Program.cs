using System.Threading.Channels;
using System.Threading.RateLimiting;
using GmodAddonAnalyzer.Configuration;
using GmodAddonAnalyzer.Data;
using GmodAddonAnalyzer.Hosting;
using GmodAddonAnalyzer.Services;
using GmodAddonAnalyzer.Web.Endpoints;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

builder.Services.AddRateLimiter(o =>
{
    o.AddFixedWindowLimiter("pow", w => { w.Window = TimeSpan.FromMinutes(1); w.PermitLimit = 10; });
    o.AddFixedWindowLimiter("analyze", w => { w.Window = TimeSpan.FromMinutes(1); w.PermitLimit = 5; });
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

builder.Services.AddSingleton(Channel.CreateUnbounded<AnalysisRequest>(new UnboundedChannelOptions
{
    SingleReader = true,
    SingleWriter = false,
}));
builder.Services.AddHostedService<AnalysisPipelineHostedService>();
builder.Services.AddHostedService<CleanupHostedService>();

var app = builder.Build();

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

app.UseStatusCodePagesWithRedirects("/NotFound");

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthorization();
app.MapRazorPages();
app.MapStatusEndpoints();

app.Run();

public partial class Program;