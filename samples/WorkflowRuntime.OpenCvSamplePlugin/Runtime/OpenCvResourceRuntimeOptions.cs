namespace WorkflowRuntime.OpenCvSamplePlugin.Runtime;

public sealed class OpenCvResourceRuntimeOptions
{
    public string PreviewDirectory { get; set; } = Path.Combine(Path.GetTempPath(), "GboWorkflow", "VisionPreview");

    public int PreviewMaxWidth { get; set; } = 1600;

    public int PreviewMaxHeight { get; set; } = 1000;

    public int ResourceRetentionMinutes { get; set; } = 15;

    public int MaximumRetainedImages { get; set; } = 512;
}
