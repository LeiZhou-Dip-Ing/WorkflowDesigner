using OpenCvSharp;
using WorkflowRuntime.ActionSdk;

namespace WorkflowRuntime.OpenCvSamplePlugin;

[WorkflowAction(
    "sample.vision.saveImage",
    "SDK Save Image",
    ActionId = "4365319c-45ee-4d18-a2ce-f264583170fd",
    Category = "Vision / SDK Plugin",
    Description = "Saves a runtime-owned image handle to disk with OpenCV.",
    DisplayTemplate = "Save {InputImage} → {FilePath}",
    ActionKind = WorkflowActionKinds.Vision,
    WorkspaceKind = WorkflowWorkspaceKeys.Image,
    DoubleClickEditor = WorkflowActionEditorKeys.Vision)]
public sealed class SaveImageSdkAction : WorkflowActionBase
{
    [WorkflowActionInput(DisplayName = "Input image", ValueType = "image", Editor = WorkflowPropertyEditorKeys.Variable,
        DataSource = "methodVariables", AllowCustomValue = false, AllowClear = true, Required = true, Order = 0)]
    public string InputImage { get; set; } = string.Empty;

    [WorkflowActionInput(DisplayName = "File path", ValueType = "string", Editor = WorkflowPropertyEditorKeys.Text,
        Required = true, Order = 1)]
    public string FilePath { get; set; } = string.Empty;

    [WorkflowActionInput(DisplayName = "Create directory", ValueType = "boolean", Editor = WorkflowPropertyEditorKeys.Checkbox,
        Required = true, Order = 2)]
    public bool CreateDirectory { get; set; } = true;

    [WorkflowActionInput(DisplayName = "Show image preview", ValueType = "boolean", Editor = WorkflowPropertyEditorKeys.Checkbox,
        Required = true, Order = 3)]
    public bool PublishPreview { get; set; } = true;

    [WorkflowActionOutput(DisplayName = "Saved path", ValueType = "string", Required = false, Order = 4)]
    public string SavedPath { get; private set; } = string.Empty;

    protected override ValueTask ExecuteActionAsync(IWorkflowActionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var vision = OpenCvActionSupport.RequireVision(context);
        var input = OpenCvActionSupport.RequireImage(vision, InputImage);
        var path = Environment.ExpandEnvironmentVariables(FilePath?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("File path is required.");
        }

        path = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(path);
        if (CreateDirectory && !string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!Cv2.ImWrite(path, input))
        {
            throw new IOException($"OpenCV failed to write image '{path}'.");
        }

        SavedPath = path;
        if (PublishPreview)
        {
            vision.PublishPreview(InputImage);
        }
        context.Log($"Saved image to '{path}'.");
        return ValueTask.CompletedTask;
    }
}
