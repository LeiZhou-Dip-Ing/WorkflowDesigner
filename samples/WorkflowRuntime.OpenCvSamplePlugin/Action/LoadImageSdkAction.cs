using OpenCvSharp;
using WorkflowRuntime.ActionSdk;
using WorkflowRuntime.ResourceSdk;

namespace WorkflowRuntime.OpenCvSamplePlugin;

[WorkflowAction(
    "sample.vision.loadImage",
    "SDK Load Image",
    ActionId = "bb1b6c2a-9384-4c02-b6f1-306577a6ea43",
    Category = "Vision / SDK Plugin",
    Description = "Loads an image through an external Action SDK plugin and publishes a preview.",
    DisplayTemplate = "Load {FilePath} → {OutputImage}",
    WorkspaceKind = WorkflowWorkspaceKeys.Image,
    DoubleClickEditor = WorkflowActionEditorKeys.Image)]
public sealed class LoadImageSdkAction : WorkflowActionBase
{
    [WorkflowActionInput(
        DisplayName = "File path",
        Description = "Absolute image file path on the Runtime host.",
        ValueType = "string",
        Editor = WorkflowPropertyEditorKeys.Text,
        Required = true,
        Order = 0)]
    public string FilePath { get; set; } = string.Empty;

    [WorkflowActionInput(
        DisplayName = "Read mode",
        ValueType = "string",
        Editor = WorkflowPropertyEditorKeys.Select,
        EnumValues = new string[] { "Color", "Grayscale", "Unchanged" },
        Required = true,
        Order = 1)]
    public string ReadMode { get; set; } = "Color";

    [WorkflowActionInput(
        DisplayName = "Show image preview",
        ValueType = "boolean",
        Editor = WorkflowPropertyEditorKeys.Checkbox,
        Required = true,
        Order = 2)]
    public bool PublishPreview { get; set; } = true;

    [WorkflowActionOutput(
        DisplayName = "Output image",
        Description = "Runtime-owned image handle. Bind this output to an image method variable.",
        ValueType = "image",
        Required = true,
        Order = 3)]
    public string OutputImage { get; private set; } = string.Empty;

    protected override ValueTask ExecuteActionAsync(
        IWorkflowActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context is not IWorkflowResourceActionContext vision)
        {
            throw new InvalidOperationException("The host does not expose the optional Resource Action context.");
        }

        var requestedPath = Environment.ExpandEnvironmentVariables(FilePath?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            throw new InvalidOperationException("Image file path is required. Use sample://feature-source, sample://source, sample://template or sample://template-search for the self-contained demo images.");
        }

        Mat image;
        string source;
        if (string.Equals(requestedPath, "sample://feature-source", StringComparison.OrdinalIgnoreCase))
        {
            image = DemoImageFactory.CreateFeaturePipelineSource();
            source = requestedPath;
        }
        else if (string.Equals(requestedPath, "sample://source", StringComparison.OrdinalIgnoreCase))
        {
            image = DemoImageFactory.CreateSource();
            source = requestedPath;
        }
        else if (string.Equals(requestedPath, "sample://template", StringComparison.OrdinalIgnoreCase))
        {
            image = DemoImageFactory.CreateTemplate();
            source = requestedPath;
        }
        else if (string.Equals(requestedPath, "sample://template-search", StringComparison.OrdinalIgnoreCase))
        {
            image = DemoImageFactory.CreateTemplateSearch();
            source = requestedPath;
        }
        else
        {
            var path = Path.GetFullPath(requestedPath);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Image file was not found.", path);
            }

            var mode = ReadMode.Trim().ToLowerInvariant() switch
            {
                "grayscale" or "gray" => ImreadModes.Grayscale,
                "unchanged" => ImreadModes.Unchanged,
                _ => ImreadModes.Color
            };

            image = Cv2.ImRead(path, mode);
            if (image.Empty())
            {
                image.Dispose();
                throw new InvalidOperationException($"OpenCV could not decode image '{path}'.");
            }
            source = path;
        }

        var metadata = CreateMetadata(image, source);
        OutputImage = vision.StoreResource(image, metadata, PublishPreview);
        context.Log($"Loaded {image.Width} x {image.Height} image from '{source}'.");
        return ValueTask.CompletedTask;
    }

    private static WorkflowResourceMetadata CreateMetadata(Mat image, string source)
        => OpenCvActionSupport.Metadata(image, source);
}
