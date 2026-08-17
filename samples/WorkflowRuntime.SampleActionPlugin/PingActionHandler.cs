using System.Text.Json;
using WorkflowRuntime.ActionSdk;

namespace WorkflowRuntime.SampleActionPlugin;

public sealed class PingActionHandler : IWorkflowActionHandler
{
    public ValueTask<WorkflowActionResult> ExecuteAsync(
        IWorkflowActionContext context,
        IReadOnlyDictionary<string, JsonElement> inputs,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.Log("Pong from interface-only plugin Action.");
        return ValueTask.FromResult(WorkflowActionResult.Success());
    }
}
