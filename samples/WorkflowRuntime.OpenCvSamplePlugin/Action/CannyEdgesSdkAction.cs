using OpenCvSharp;
using WorkflowDesigner.Contracts;
using WorkflowRuntime.ActionSdk;
using WorkflowRuntime.OpenCvSamplePlugin.Shared;
using WorkflowRuntime.VisionSdk;

namespace WorkflowRuntime.OpenCvSamplePlugin;

[WorkflowAction(
    "sample.vision.canny",
    "SDK Canny Edges",
    ActionId = "4994a866-3426-4e50-ae77-b6ad7f8f18ba",
    Category = "Vision / SDK Plugin",
    Description = "Detects image edges through an external Action SDK plugin.",
    DisplayTemplate = "Canny {InputImage} ({Threshold1}, {Threshold2}) → {OutputImage}",
    ActionKind = WorkflowActionKinds.Vision,
    WorkspaceKind = WorkflowWorkspaceKeys.Image,
    DoubleClickEditor = OpenCvDesignerKeys.CannyActionEditor)]
public sealed class CannyEdgesSdkAction : WorkflowActionBase
{
    [WorkflowActionInput(
        DisplayName = "Input image",
        ValueType = "image",
        Editor = WorkflowPropertyEditorKeys.Variable,
        DataSource = "methodVariables",
        AllowCustomValue = false,
        AllowClear = true,
        Required = true,
        Order = 0)]
    public string InputImage { get; set; } = string.Empty;

    [WorkflowActionInput(DisplayName = "Lower threshold", ValueType = "number", Editor = WorkflowPropertyEditorKeys.Number, Minimum = 0, Required = true, Order = 1)]
    public double Threshold1 { get; set; } = 100;

    [WorkflowActionInput(DisplayName = "Upper threshold", ValueType = "number", Editor = WorkflowPropertyEditorKeys.Number, Minimum = 0, Required = true, Order = 2)]
    public double Threshold2 { get; set; } = 200;

    [WorkflowActionInput(
        DisplayName = "Show image preview",
        ValueType = "boolean",
        Editor = WorkflowPropertyEditorKeys.Checkbox,
        Required = true,
        Order = 3)]
    public bool PublishPreview { get; set; } = true;

    [WorkflowActionOutput(DisplayName = "Output image", ValueType = "image", Required = true, Order = 4)]
    public string OutputImage { get; private set; } = string.Empty;

    protected override ValueTask ExecuteActionAsync(
        IWorkflowActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var vision = context as IWorkflowVisionActionContext
            ?? throw new InvalidOperationException("The host does not expose the optional Vision Action context.");
        if (!vision.TryGetImage<Mat>(InputImage, out var input) || input == null)
        {
            throw new KeyNotFoundException($"Image handle '{InputImage}' was not found.");
        }

        using var gray = ToGrayClone(input);
        var output = new Mat();
        Cv2.Canny(gray, output, Threshold1, Threshold2);
        var metadata = new VisionImageMetadata
        {
            Width = output.Width,
            Height = output.Height,
            Channels = output.Channels(),
            DepthBits = checked((int)output.ElemSize1() * 8),
            PixelFormat = "Gray",
            Source = "SDK plugin: Canny"
        };
        OutputImage = vision.StoreImage(output, metadata, PublishPreview);
        context.Log($"Canny edge detection completed ({Threshold1}, {Threshold2}).");
        return ValueTask.CompletedTask;
    }

    private static Mat ToGrayClone(Mat source)
    {
        if (source.Channels() == 1)
        {
            return source.Clone();
        }

        var gray = new Mat();
        Cv2.CvtColor(
            source,
            gray,
            source.Channels() == 4 ? ColorConversionCodes.BGRA2GRAY : ColorConversionCodes.BGR2GRAY);
        return gray;
    }
}
