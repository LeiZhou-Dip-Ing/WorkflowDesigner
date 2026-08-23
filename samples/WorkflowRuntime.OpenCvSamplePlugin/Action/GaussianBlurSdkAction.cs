using OpenCvSharp;
using WorkflowRuntime.ActionSdk;
using WorkflowRuntime.OpenCvSamplePlugin.Shared;
using WorkflowRuntime.ResourceSdk;

namespace WorkflowRuntime.OpenCvSamplePlugin;

[WorkflowAction(
    "sample.vision.gaussianBlur",
    "SDK Gaussian Blur",
    ActionId = "0b45736c-9f1e-4d82-8330-a2e78df99fb3",
    Category = "Vision / SDK Plugin",
    Description = "Applies Gaussian blur through an external Action SDK plugin.",
    DisplayTemplate = "Blur {InputImage} ({KernelSize}) → {OutputImage}",
    WorkspaceKind = OpenCvDesignerKeys.ImageWorkspace,
    DoubleClickEditor = OpenCvDesignerKeys.ImageActionEditor)]
public sealed class GaussianBlurSdkAction : WorkflowActionBase
{
    [WorkflowActionInput(
        DisplayName = "Input image",
        ValueType = "resource",
        Editor = WorkflowPropertyEditorKeys.Variable,
        DataSource = "methodVariables",
        AllowCustomValue = false,
        AllowClear = true,
        Required = true,
        Order = 0)]
    public string InputImage { get; set; } = string.Empty;

    [WorkflowActionInput(
        DisplayName = "Kernel size",
        ValueType = "integer",
        Editor = OpenCvDesignerKeys.OddKernelPropertyEditor,
        Minimum = 1,
        Required = true,
        Order = 1)]
    public int KernelSize { get; set; } = 5;

    [WorkflowActionInput(
        DisplayName = "Sigma X",
        ValueType = "number",
        Editor = WorkflowPropertyEditorKeys.Number,
        Minimum = 0,
        Required = true,
        Order = 2)]
    public double SigmaX { get; set; }

    [WorkflowActionInput(
        DisplayName = "Show image preview",
        ValueType = "boolean",
        Editor = WorkflowPropertyEditorKeys.Checkbox,
        Required = true,
        Order = 3)]
    public bool PublishPreview { get; set; } = true;

    [WorkflowActionOutput(
        DisplayName = "Output image",
        ValueType = "resource",
        Required = true,
        Order = 4)]
    public string OutputImage { get; private set; } = string.Empty;

    protected override ValueTask ExecuteActionAsync(
        IWorkflowActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var vision = context as IWorkflowResourceActionContext
            ?? throw new InvalidOperationException("The host does not expose the optional Resource Action context.");
        if (!vision.TryGetResource<Mat>(InputImage, out var input) || input == null)
        {
            throw new KeyNotFoundException($"Image handle '{InputImage}' was not found.");
        }

        var kernel = Math.Max(1, KernelSize);
        if (kernel % 2 == 0)
        {
            kernel++;
        }

        var output = new Mat();
        Cv2.GaussianBlur(input, output, new Size(kernel, kernel), Math.Max(0, SigmaX));
        var metadata = vision.GetResourceMetadata(InputImage);
        OutputImage = vision.StoreResource(
            output,
            metadata with { Source = $"SDK plugin: GaussianBlur {kernel}x{kernel}" },
            PublishPreview);
        context.Log($"Gaussian blur {kernel}x{kernel} completed.");
        return ValueTask.CompletedTask;
    }
}
