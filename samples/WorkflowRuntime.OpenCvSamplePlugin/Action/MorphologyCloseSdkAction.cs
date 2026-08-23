using OpenCvSharp;
using WorkflowDesigner.Contracts;
using WorkflowRuntime.ActionSdk;
using WorkflowRuntime.OpenCvSamplePlugin.Shared;

namespace WorkflowRuntime.OpenCvSamplePlugin;

[WorkflowAction(
    "sample.vision.morphologyClose",
    "SDK Morphology Close",
    ActionId = "70ff8195-0dc8-4d96-8588-c23e8029caa0",
    Category = "Vision / SDK Plugin / Preprocessing",
    Description = "Closes small gaps in a binary image with OpenCvSharp morphology.",
    DisplayTemplate = "Close {InputImage}, kernel {KernelSize}, iter {Iterations} → {OutputImage}",
    ActionKind = WorkflowActionKinds.Vision,
    WorkspaceKind = WorkflowWorkspaceKeys.Image,
    DoubleClickEditor = WorkflowDesigner.Contracts.WorkflowActionEditorKeys.Vision)]
public sealed class MorphologyCloseSdkAction : WorkflowActionBase
{
    [WorkflowActionInput(DisplayName = "Input image", ValueType = "image", Editor = WorkflowPropertyEditorKeys.Variable,
        DataSource = "methodVariables", AllowCustomValue = false, AllowClear = true, Required = true, Order = 0)]
    public string InputImage { get; set; } = string.Empty;

    [WorkflowActionInput(DisplayName = "Kernel size", ValueType = "integer", Editor = OpenCvDesignerKeys.OddKernelPropertyEditor,
        Minimum = 1, Maximum = 31, Required = true, Order = 1)]
    public int KernelSize { get; set; } = 7;

    [WorkflowActionInput(DisplayName = "Iterations", ValueType = "integer", Editor = WorkflowPropertyEditorKeys.Number,
        Minimum = 1, Maximum = 10, Required = true, Order = 2)]
    public int Iterations { get; set; } = 2;

    [WorkflowActionInput(DisplayName = "Show image preview", ValueType = "boolean", Editor = WorkflowPropertyEditorKeys.Checkbox,
        Required = true, Order = 3)]
    public bool PublishPreview { get; set; } = true;

    [WorkflowActionOutput(DisplayName = "Output image", ValueType = "image", Required = true, Order = 10)]
    public string OutputImage { get; private set; } = string.Empty;

    protected override ValueTask ExecuteActionAsync(IWorkflowActionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var vision = OpenCvActionSupport.RequireVision(context);
        var input = OpenCvActionSupport.RequireImage(vision, InputImage);
        using var gray = OpenCvActionSupport.ToGrayClone(input);

        var k = Math.Max(1, KernelSize);
        if (k % 2 == 0)
        {
            k += 1;
        }

        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(k, k));
        var output = new Mat();
        Cv2.MorphologyEx(gray, output, MorphTypes.Close, kernel, iterations: Math.Max(1, Iterations));

        OutputImage = vision.StoreImage(
            output,
            OpenCvActionSupport.Metadata(output, $"SDK plugin: Morphology Close {k}x{k}, iter={Iterations}"),
            PublishPreview);

        context.Log($"Morphology close completed: kernel={k}x{k}, iterations={Math.Max(1, Iterations)}.");
        return ValueTask.CompletedTask;
    }
}
