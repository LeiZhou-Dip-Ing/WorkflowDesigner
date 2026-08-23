using System.Text.Json;
using WorkflowRuntime.ActionSdk;

namespace WorkflowRuntime.SampleActionPlugin;

[WorkflowAction(
    "sample.jsonEnvelope",
    "JSON Envelope",
    ActionId = "8be225ba-ad99-484f-a086-1c592a740d99",
    Category = "External plugins / Data",
    Description = "Wrap structured JSON in a versioned event envelope.",
    DisplayTemplate = "Wrap {EventName} payload")]
public sealed class JsonEnvelopeAction : WorkflowActionBase
{
    [WorkflowActionProperty(DisplayName = "Event name", Required = true, Placeholder = "orders.created", Order = 0)]
    public string EventName { get; set; } = "sample.created";

    [WorkflowActionProperty(DisplayName = "Schema version", Minimum = 1, Maximum = 100, Step = 1, Order = 1)]
    public int SchemaVersion { get; set; } = 1;

    [WorkflowActionInput(Description = "Any JSON object, array, or scalar value.", Required = true, Editor = "json", Order = 2)]
    public JsonElement Payload { get; set; } = JsonSerializer.SerializeToElement(new { });

    [WorkflowActionOutput(Description = "The complete event envelope.", Required = true, Order = 3)]
    public JsonElement Envelope { get; private set; }

    protected override ValueTask ExecuteActionAsync(IWorkflowActionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(EventName))
        {
            throw new InvalidOperationException("Event name cannot be empty.");
        }

        Envelope = JsonSerializer.SerializeToElement(new
        {
            eventName = EventName.Trim(),
            schemaVersion = SchemaVersion,
            payload = Payload
        });
        context.Log($"JSON envelope created for '{EventName}'.");
        return ValueTask.CompletedTask;
    }
}
