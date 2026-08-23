using OpenCvSharp;
using WorkflowRuntime.ActionSdk;
using WorkflowRuntime.OpenCvSamplePlugin.Shared;

namespace WorkflowRuntime.OpenCvSamplePlugin;

[WorkflowAction(
    "sample.vision.threshold",
    "SDK Threshold",
    ActionId = "4bf7d04a-a100-47aa-bcd8-a6829e748914",
    Category = "Vision / SDK Plugin",
    Description = "Applies a grayscale threshold operation similar to the preprocessing tools in the legacy vision project.",
    DisplayTemplate = "Threshold {InputImage} ({Threshold}) → {OutputImage}",
    WorkspaceKind = OpenCvDesignerKeys.ImageWorkspace,
    DoubleClickEditor = OpenCvDesignerKeys.ImageActionEditor)]
public sealed class ThresholdSdkAction : WorkflowActionBase
{
    [WorkflowActionInput(DisplayName = "Input image", ValueType = "resource", Editor = WorkflowPropertyEditorKeys.Variable,
        DataSource = "methodVariables", AllowCustomValue = false, AllowClear = true, Required = true, Order = 0)]
    public string InputImage { get; set; } = string.Empty;

    [WorkflowActionInput(DisplayName = "Threshold", ValueType = "number", Editor = WorkflowPropertyEditorKeys.Number,
        Minimum = 0, Maximum = 255, Required = true, Order = 1)]
    public double Threshold { get; set; } = 120;

    [WorkflowActionInput(DisplayName = "Maximum value", ValueType = "number", Editor = WorkflowPropertyEditorKeys.Number,
        Minimum = 0, Maximum = 255, Required = true, Order = 2)]
    public double MaxValue { get; set; } = 255;

    [WorkflowActionInput(DisplayName = "Threshold mode", ValueType = "string", Editor = WorkflowPropertyEditorKeys.Select,
        EnumValues = new string[] { "Binary", "BinaryInv", "Trunc", "ToZero", "ToZeroInv", "Otsu" }, Required = true, Order = 3)]
    public string ThresholdMode { get; set; } = "Binary";

    [WorkflowActionInput(DisplayName = "Show image preview", ValueType = "boolean", Editor = WorkflowPropertyEditorKeys.Checkbox,
        Required = true, Order = 4)]
    public bool PublishPreview { get; set; } = true;

    [WorkflowActionOutput(DisplayName = "Output image", ValueType = "resource", Required = true, Order = 5)]
    public string OutputImage { get; private set; } = string.Empty;

    protected override ValueTask ExecuteActionAsync(IWorkflowActionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var vision = OpenCvActionSupport.RequireResources(context);
        var input = OpenCvActionSupport.RequireImage(vision, InputImage);
        using var gray = OpenCvActionSupport.ToGrayClone(input);
        var output = new Mat();
        var thresholdType = ParseThresholdType(ThresholdMode);
        Cv2.Threshold(gray, output, Math.Clamp(Threshold, 0, 255), Math.Clamp(MaxValue, 0, 255), thresholdType);
        OutputImage = vision.StoreResource(output, OpenCvActionSupport.Metadata(output, $"SDK plugin: Threshold {ThresholdMode}"), PublishPreview);
        context.Log($"Threshold completed: {ThresholdMode}, value={Threshold:0.##}.");
        return ValueTask.CompletedTask;
    }

    private static ThresholdTypes ParseThresholdType(string? mode)
        => (mode ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "binaryinv" => ThresholdTypes.BinaryInv,
            "trunc" => ThresholdTypes.Trunc,
            "tozero" => ThresholdTypes.Tozero,
            "tozeroinv" => ThresholdTypes.TozeroInv,
            "otsu" => ThresholdTypes.Binary | ThresholdTypes.Otsu,
            _ => ThresholdTypes.Binary
        };
}
