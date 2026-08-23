using WorkflowRuntime.ResourceSdk;

namespace WorkflowRuntime.WindowsService;

public sealed class ResourceCleanup : BackgroundService
{
    private readonly IWorkflowResourceRuntime _resources;
    private readonly ILogger<ResourceCleanup> _logger;

    public ResourceCleanup(
        IWorkflowResourceRuntime resources,
        ILogger<ResourceCleanup> logger)
    {
        _resources = resources;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                var removed = _resources.CleanupExpired();
                if (removed > 0)
                {
                    _logger.LogDebug("Removed {Count} expired workflow resources.", removed);
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Workflow resource cleanup failed.");
            }
        }
    }
}
