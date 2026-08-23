using OpenCvSharp;
using WorkflowDesigner.Contracts;
using WorkflowRuntime.ActionSdk;
using WorkflowRuntime.VisionSdk;

namespace WorkflowRuntime.OpenCvSamplePlugin;

[WorkflowAction(
    "sample.vision.invert",
    "SDK Invert Image",
    ActionId = "0ae53672-e55a-4537-a00b-2d974dbcf204",
    Category = "Vision / SDK Plugin",
    Description = "Inverts an image through an external Action SDK plugin.",
    DisplayTemplate = "Invert {InputImage} → {OutputImage}",
    ActionKind = WorkflowActionKinds.Vision,
    WorkspaceKind = WorkflowWorkspaceKeys.Image,
    DoubleClickEditor = WorkflowDesigner.Contracts.WorkflowActionEditorKeys.Vision)]
public sealed class InvertImageAction : WorkflowActionBase
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

    [WorkflowActionInput(
        DisplayName = "Show image preview",
        ValueType = "boolean",
        Editor = WorkflowPropertyEditorKeys.Checkbox,
        Required = true,
        Order = 1)]
    public bool PublishPreview { get; set; } = true;

    [WorkflowActionOutput(
        DisplayName = "Output image",
        ValueType = "image",
        Required = true,
        Order = 2)]
    public string OutputImage { get; private set; } = string.Empty;

    protected override ValueTask ExecuteActionAsync(
        IWorkflowActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var vision = RequireVisionContext(context);
        var input = RequireImage(vision, InputImage);

        var output = new Mat();
        Cv2.BitwiseNot(input, output);
        var sourceMetadata = vision.GetImageMetadata(InputImage);
        OutputImage = vision.StoreImage(
            output,
            sourceMetadata with { Source = "SDK plugin: Invert" },
            PublishPreview);
        context.Log($"Inverted {sourceMetadata.Width} x {sourceMetadata.Height} image.");
        return ValueTask.CompletedTask;
    }

    private static IWorkflowVisionActionContext RequireVisionContext(IWorkflowActionContext context)
        => context as IWorkflowVisionActionContext
           ?? throw new InvalidOperationException("The host does not expose the optional Vision Action context.");

    private static Mat RequireImage(IWorkflowVisionActionContext vision, string handle)
    {
        if (!vision.TryGetImage<Mat>(handle, out var input) || input == null)
        {
            throw new KeyNotFoundException($"Image handle '{handle}' was not found.");
        }

        return input;
    }
}
