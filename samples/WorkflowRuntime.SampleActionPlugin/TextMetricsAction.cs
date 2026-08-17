using System.Text;
using WorkflowRuntime.ActionSdk;

namespace WorkflowRuntime.SampleActionPlugin;

[WorkflowAction(
    "TextMetrics",
    "Text Metrics",
    ActionId = "fe4c0698-a793-4f9a-a982-e61c013a3137",
    ActionVersion = 1,
    Category = "External plugins",
    Description = "Calculate reusable values from input text.",
    DisplayTemplate = "Measure {Text}",
    Aliases = new[] { "sample.textMetrics" })]
public sealed class TextMetricsAction : WorkflowActionBase
{
    [WorkflowActionInput(
        DisplayName = "Text",
        Description = "Literal text or a method variable expression to measure.",
        Required = true,
        Order = 0)]
    public string Text { get; set; } = string.Empty;

    [WorkflowActionOutput(
        DisplayName = "Length",
        Description = "Number of characters in the input text.",
        Required = true,
        Order = 1)]
    public int Length { get; private set; }

    [WorkflowActionOutput(
        DisplayName = "Uppercase text",
        Description = "Uppercase form of the input text.",
        Required = true,
        Order = 2)]
    public string Uppercase { get; private set; } = string.Empty;

    protected override WorkflowActionIcon Icon => new()
    {
        ContentType = "image/svg+xml",
        Content = Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"32\" height=\"32\" viewBox=\"0 0 32 32\"><rect x=\"3\" y=\"3\" width=\"26\" height=\"26\" rx=\"4\" fill=\"none\" stroke=\"#62b915\" stroke-width=\"2\"/><path d=\"M8 10h10M13 10v12M9 22h8M20 18h5M22.5 15.5v5\" fill=\"none\" stroke=\"#62b915\" stroke-width=\"2\" stroke-linecap=\"round\"/></svg>")
    };

    protected override ValueTask ExecuteActionAsync(
        IWorkflowActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Length = Text.Length;
        Uppercase = Text.ToUpperInvariant();
        context.Log($"Text metrics: length={Length}");
        return ValueTask.CompletedTask;
    }
}
