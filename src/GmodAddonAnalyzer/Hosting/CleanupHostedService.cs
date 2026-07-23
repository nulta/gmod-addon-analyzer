using GmodAddonAnalyzer.Configuration;
using GmodAddonAnalyzer.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GmodAddonAnalyzer.Hosting;

public sealed class CleanupHostedService : BackgroundService
{
    private readonly CleanupService _cleanup;
    private readonly AnalyzerOptions _options;
    private readonly ILogger<CleanupHostedService> _logger;
    private readonly TimeSpan _interval;

    public CleanupHostedService(CleanupService cleanup, AnalyzerOptions options, ILogger<CleanupHostedService> logger)
    {
        _cleanup = cleanup;
        _options = options;
        _logger = logger;
        _interval = TimeSpan.FromHours(Math.Max(1, options.CleanupIntervalHours));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await _cleanup.SweepAsync(stoppingToken); }
        catch (Exception ex) { _logger.LogWarning(ex, "Initial cleanup sweep failed"); }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(_interval, stoppingToken); }
            catch (TaskCanceledException) { return; }
            try { await _cleanup.SweepAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogWarning(ex, "Cleanup sweep failed"); }
        }
    }
}