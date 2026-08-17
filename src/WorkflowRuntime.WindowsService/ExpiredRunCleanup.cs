using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkflowRuntime.Application.Runtime;

namespace WorkflowRuntime.WindowsService;

/// <summary>Removes terminal run records after their configured retention window.</summary>
public sealed class ExpiredRunCleanup : BackgroundService
{
    private readonly IRunRegistry _runRegistry;
    private readonly RunRetentionOptions _options;
    private readonly ILogger<ExpiredRunCleanup> _logger;

    public ExpiredRunCleanup(
        IRunRegistry runRegistry,
        RunRetentionOptions options,
        ILogger<ExpiredRunCleanup> logger)
    {
        _runRegistry = runRegistry ?? throw new ArgumentNullException(nameof(runRegistry));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = _options.CleanupInterval > TimeSpan.Zero
            ? _options.CleanupInterval
            : TimeSpan.FromSeconds(60);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
                var removed = _runRegistry.Cleanup();
                if (removed > 0)
                {
                    _logger.LogDebug("Removed {RunCount} expired workflow run record(s).", removed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Workflow run cleanup failed; the next interval will retry.");
            }
        }
    }
}
