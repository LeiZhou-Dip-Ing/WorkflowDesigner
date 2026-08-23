using WorkflowRuntime.ActionSdk;
using WorkflowRuntime.OpenCvSamplePlugin.Shared;

namespace WorkflowRuntime.OpenCvSamplePlugin;

public sealed class OpenCvSamplePlugin : IWorkflowActionPlugin
{
    public string PluginId => OpenCvPluginIdentity.Id;
    public string PluginVersion => OpenCvPluginIdentity.Version;
    public string DisplayName => OpenCvPluginIdentity.DisplayName;

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
        builder.AddCommand<OpenCvDesignerCommandHandler>(OpenCvCommandIds.PreviewLearning);
        builder.AddCommand<OpenCvDesignerCommandHandler>(OpenCvCommandIds.Learn);
        builder.AddCommand<OpenCvDesignerCommandHandler>(OpenCvCommandIds.Match);
    }
}
