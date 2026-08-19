using WorkflowRuntime.ActionSdk;

namespace WorkflowRuntime.SampleActionPlugin;

[WorkflowAction(
    "sample.delay",
    "Cancelable Delay",
    ActionId = "40f6ae0b-45ca-45db-af04-0ebfd3fa7a56",
    Category = "External plugins / Control",
    Description = "Demonstrate asynchronous Action execution and cooperative cancellation.",
    DisplayTemplate = "Wait {Milliseconds} ms")]
public sealed class DelayAction : WorkflowActionBase
{
    [WorkflowActionInput(Minimum = 0, Maximum = 30000, Step = 100, Order = 0)]
    public int Milliseconds { get; set; } = 500;

    [WorkflowActionOutput(DisplayName = "Completed at UTC", Description = "ISO-8601 completion timestamp.", Order = 1)]
    public string CompletedAtUtc { get; private set; } = string.Empty;

    protected override async ValueTask ExecuteActionAsync(
        IWorkflowActionContext context,
        CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(Milliseconds), cancellationToken).ConfigureAwait(false);
        CompletedAtUtc = DateTimeOffset.UtcNow.ToString("O");
        context.Log($"Cancelable delay completed after {Milliseconds} ms.");
    }
}
