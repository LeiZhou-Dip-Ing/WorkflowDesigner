using WorkflowRuntime.ActionSdk;

namespace WorkflowRuntime.SampleActionPlugin;

[WorkflowAction(
    "sample.textTransform",
    "Text Transform",
    ActionId = "af824816-da66-4e88-8cca-bd65d9fce250",
    Category = "External plugins / Text",
    Description = "Normalize text with generated select, checkbox, and number editors.",
    DisplayTemplate = "Transform {Text} as {Mode}")]
public sealed class TextTransformAction : WorkflowActionBase
{
    [WorkflowActionProperty(Description = "Letter-case conversion applied after trimming.", Order = 0)]
    public string Mode { get; set; } = "Preserve";

    [WorkflowActionProperty(DisplayName = "Trim whitespace", Description = "Remove whitespace at both ends.", Order = 1)]
    public bool TrimWhitespace { get; set; } = true;

    [WorkflowActionProperty(DisplayName = "Maximum length", Minimum = 1, Maximum = 4096, Step = 1, Order = 2)]
    public int MaximumLength { get; set; } = 256;

    [WorkflowActionInput(Description = "Literal text or a workflow variable expression.", Required = true, Order = 3)]
    public string Text { get; set; } = string.Empty;

    [WorkflowActionOutput(Description = "Normalized text for downstream Actions.", Required = true, Order = 4)]
    public string Result { get; private set; } = string.Empty;

    [WorkflowActionOutput(DisplayName = "Was truncated", Description = "True when the maximum length was applied.", Order = 5)]
    public bool WasTruncated { get; private set; }

    protected override ValueTask ExecuteActionAsync(IWorkflowActionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = TrimWhitespace ? Text.Trim() : Text;
        value = Mode.Trim().ToLowerInvariant() switch
        {
            "uppercase" => value.ToUpperInvariant(),
            "lowercase" => value.ToLowerInvariant(),
            _ => value
        };

        WasTruncated = value.Length > MaximumLength;
        Result = WasTruncated ? value[..MaximumLength] : value;
        context.Log($"Text transformed: length={Result.Length}, truncated={WasTruncated}.");
        return ValueTask.CompletedTask;
    }
}
