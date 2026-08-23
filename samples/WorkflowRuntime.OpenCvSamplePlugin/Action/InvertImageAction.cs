using OpenCvSharp;
using WorkflowRuntime.ActionSdk;
using WorkflowRuntime.ResourceSdk;

namespace WorkflowRuntime.OpenCvSamplePlugin;

[WorkflowAction(
    "sample.vision.invert",
    "SDK Invert Image",
    ActionId = "0ae53672-e55a-4537-a00b-2d974dbcf204",
    Category = "Vision / SDK Plugin",
    Description = "Inverts an image through an external Action SDK plugin.",
    DisplayTemplate = "Invert {InputImage} → {OutputImage}",
    WorkspaceKind = OpenCvDesignerKeys.ImageWorkspace,
    DoubleClickEditor = OpenCvDesignerKeys.ImageActionEditor)]
public sealed class InvertImageAction : WorkflowActionBase
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
        DisplayName = "Show image preview",
        ValueType = "boolean",
        Editor = WorkflowPropertyEditorKeys.Checkbox,
        Required = true,
        Order = 1)]
    public bool PublishPreview { get; set; } = true;

    [WorkflowActionOutput(
        DisplayName = "Output image",
        ValueType = "resource",
        Required = true,
        Order = 2)]
    public string OutputImage { get; private set; } = string.Empty;

    protected override ValueTask ExecuteActionAsync(
        IWorkflowActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resources = RequireResourceContext(context);
        var input = RequireImage(resources, InputImage);

        var output = new Mat();
        Cv2.BitwiseNot(input, output);
        var sourceMetadata = resources.GetResourceMetadata(InputImage);
        OutputImage = resources.StoreResource(
            output,
            sourceMetadata with { Source = "SDK plugin: Invert" },
            PublishPreview);
        context.Log("Inverted image resource.");
        return ValueTask.CompletedTask;
    }

    private static IWorkflowResourceActionContext RequireResourceContext(IWorkflowActionContext context)
        => context as IWorkflowResourceActionContext
           ?? throw new InvalidOperationException("The host does not expose the optional Resource Action context.");

    private static Mat RequireImage(IWorkflowResourceActionContext vision, string handle)
    {
        if (!vision.TryGetResource<Mat>(handle, out var input) || input == null)
        {
            throw new KeyNotFoundException($"Image handle '{handle}' was not found.");
        }

        return input;
    }
}
