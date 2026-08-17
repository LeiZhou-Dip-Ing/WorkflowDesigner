using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkflowRuntime.Application.Documents;
using WorkflowRuntime.Application.Runtime;
using WorkflowRuntime.Application.Storage;
using WorkflowRuntime.Contracts;

namespace WorkflowRuntime.WindowsService;

/// <summary>Validates explicitly published workflows and starts configured methods.</summary>
public sealed class PublishedWorkflowStartup : IHostedService
{
    private readonly WorkflowRuntimeOptions _options;
    private readonly PublishedWorkflowStore _publishedWorkflows;
    private readonly RuntimeWorkflowValidator _workflowValidator;
    private readonly WorkflowRunLauncher _runLauncher;
    private readonly ILogger<PublishedWorkflowStartup> _logger;

    public PublishedWorkflowStartup(
        WorkflowRuntimeOptions options,
        PublishedWorkflowStore publishedWorkflows,
        RuntimeWorkflowValidator workflowValidator,
        WorkflowRunLauncher runLauncher,
        ILogger<PublishedWorkflowStartup> logger)
    {
        _options = options;
        _publishedWorkflows = publishedWorkflows;
        _workflowValidator = workflowValidator;
        _runLauncher = runLauncher;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var workflowId in _publishedWorkflows.GetWorkflowIds())
        {
            try
            {
                var workflow = await _publishedWorkflows.LoadAsync(workflowId, cancellationToken).ConfigureAwait(false);
                var validation = _workflowValidator.Validate(workflow);
                if (!validation.IsValid)
                {
                    _logger.LogError("Published workflow {WorkflowId} is invalid: {Errors}", workflowId, string.Join("; ", validation.Messages.Select(message => message.Message)));
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Could not load published workflow {WorkflowId}.", workflowId);
            }
        }

        foreach (var autoStart in _options.AutoStart.Where(item => !string.IsNullOrWhiteSpace(item.WorkflowId)))
        {
            try
            {
                var accepted = await _runLauncher.StartPublishedAsync(new WorkflowPublishedRunRequest
                {
                    WorkflowId = autoStart.WorkflowId,
                    MethodName = autoStart.MethodName
                }, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Auto-started workflow {WorkflowId} as run {RunId}.", autoStart.WorkflowId, accepted.RunId);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Could not auto-start workflow {WorkflowId}.", autoStart.WorkflowId);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
