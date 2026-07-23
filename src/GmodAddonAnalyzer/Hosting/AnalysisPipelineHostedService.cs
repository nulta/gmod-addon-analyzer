using System.Threading.Channels;
using GmodAddonAnalyzer.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GmodAddonAnalyzer.Hosting;

public sealed class AnalysisPipelineHostedService : BackgroundService
{
    private readonly Channel<AnalysisRequest> _channel;
    private readonly AnalysisPipeline _pipeline;
    private readonly ILogger<AnalysisPipelineHostedService> _logger;

    public AnalysisPipelineHostedService(Channel<AnalysisRequest> channel, AnalysisPipeline pipeline,
        ILogger<AnalysisPipelineHostedService> logger)
    {
        _channel = channel;
        _pipeline = pipeline;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var req in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await _pipeline.RunAsync(req, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Unhandled pipeline failure for run {Id}", req.RunId);
            }
        }
    }
}