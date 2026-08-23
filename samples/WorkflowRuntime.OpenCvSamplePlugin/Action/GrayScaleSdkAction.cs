using OpenCvSharp;
using WorkflowRuntime.ActionSdk;
using WorkflowRuntime.OpenCvSamplePlugin.Shared;

namespace WorkflowRuntime.OpenCvSamplePlugin;

[WorkflowAction(
    "sample.vision.gray",
    "SDK Convert to Gray",
    ActionId = "93d252af-76b8-46d1-8efe-dcc39773c3ad",
    Category = "Vision / SDK Plugin",
    Description = "Converts the input image to grayscale and publishes the processed frame to the Designer.",
    DisplayTemplate = "Gray {InputImage} → {OutputImage}",
    WorkspaceKind = WorkflowWorkspaceKeys.Image,
    DoubleClickEditor = WorkflowActionEditorKeys.Image)]
public sealed class GrayScaleSdkAction : WorkflowActionBase
{
    [WorkflowActionInput(DisplayName = "Input image", ValueType = "image", Editor = WorkflowPropertyEditorKeys.Variable,
        DataSource = "methodVariables", AllowCustomValue = false, AllowClear = true, Required = true, Order = 0)]
    public string InputImage { get; set; } = string.Empty;

    [WorkflowActionInput(DisplayName = "Show image preview", ValueType = "boolean", Editor = WorkflowPropertyEditorKeys.Checkbox,
        Required = true, Order = 1)]
    public bool PublishPreview { get; set; } = true;

    [WorkflowActionOutput(DisplayName = "Output image", ValueType = "image", Required = true, Order = 2)]
    public string OutputImage { get; private set; } = string.Empty;

    protected override ValueTask ExecuteActionAsync(IWorkflowActionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var vision = OpenCvActionSupport.RequireResources(context);
        var input = OpenCvActionSupport.RequireImage(vision, InputImage);
        var output = OpenCvActionSupport.ToGrayClone(input);
        OutputImage = vision.StoreResource(output, OpenCvActionSupport.Metadata(output, "SDK plugin: Gray"), PublishPreview);
        context.Log($"Converted image to grayscale ({output.Width} x {output.Height}).");
        return ValueTask.CompletedTask;
    }
}
