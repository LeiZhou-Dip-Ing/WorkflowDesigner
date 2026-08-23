using WorkflowRuntime.ActionSdk;

namespace WorkflowRuntime.OpenCvSamplePlugin;

public sealed class OpenCvDesignerCommandHandler : IWorkflowExtensionCommandHandler
{
    public ValueTask<WorkflowExtensionCommandResult> ExecuteAsync(
        WorkflowExtensionCommandContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var data = new Dictionary<string, object?>
        {
            ["commandId"] = context.CommandId,
            ["targetActionId"] = context.TargetActionId,
            ["accepted"] = true
        };
        return ValueTask.FromResult(new WorkflowExtensionCommandResult(
            true,
            $"OpenCV command '{context.CommandId}' accepted.",
            data));
    }
}
