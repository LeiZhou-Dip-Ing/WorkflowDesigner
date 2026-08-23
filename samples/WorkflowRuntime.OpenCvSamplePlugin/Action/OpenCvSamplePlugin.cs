using WorkflowRuntime.ActionSdk;

namespace WorkflowRuntime.OpenCvSamplePlugin;

public sealed class OpenCvSamplePlugin : IWorkflowActionPlugin
{
    public string PluginId => "workflow.sample-opencv-actions";
    public string PluginVersion => "1.6.0";

    public void Register(IWorkflowActionPluginBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddAction<LoadImageSdkAction>();
        builder.AddAction<GrayScaleSdkAction>();
        builder.AddAction<InvertImageAction>();
        builder.AddAction<GaussianBlurSdkAction>();
        builder.AddAction<ThresholdSdkAction>();
        builder.AddAction<CannyEdgesSdkAction>();
        builder.AddAction<MorphologyCloseSdkAction>();
        builder.AddAction<ContourFeaturesSdkAction>();
        builder.AddAction<MeasureLineSdkAction>();
        builder.AddAction<MeasureCircleSdkAction>();
        builder.AddAction<InteractiveTemplateMatchSdkAction>();
        builder.AddAction<TemplateMatchSdkAction>();
        builder.AddAction<SaveImageSdkAction>();
    }
}
