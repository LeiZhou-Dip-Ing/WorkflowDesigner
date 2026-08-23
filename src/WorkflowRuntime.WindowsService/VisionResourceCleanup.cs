using WorkflowRuntime.ResourceSdk;

namespace WorkflowRuntime.WindowsService;

public sealed class VisionResourceCleanup : BackgroundService
{
    private readonly IWorkflowResourceRuntime _visionRuntime;
    private readonly ILogger<VisionResourceCleanup> _logger;

    public VisionResourceCleanup(
        IWorkflowResourceRuntime visionRuntime,
        ILogger<VisionResourceCleanup> logger)
    {
        _visionRuntime = visionRuntime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                var removed = _visionRuntime.CleanupExpired();
                if (removed > 0)
                {
                    _logger.LogDebug("Removed {Count} expired image resources.", removed);
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Vision image cleanup failed.");
            }
        }
    }
}
