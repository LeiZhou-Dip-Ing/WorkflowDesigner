using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using WorkflowDesigner.WpfSdk;
using WorkflowRuntime.OpenCvSamplePlugin.Contracts;

namespace WorkflowRuntime.OpenCvSamplePlugin.Designer.Wpf;

public sealed class OpenCvSampleDesignerExtension : IWorkflowDesignerExtension
{
    public string PluginId => "sample.opencv.designer";
    public string PluginVersion => "1.7.0";

    public void Register(IWorkflowDesignerRegistry registry)
    {
        var resources = LoadResources();

        registry.RegisterPropertyEditor(
            OpenCvDesignerKeys.OddKernelPropertyEditor,
            (DataTemplate)resources["OddKernelPropertyEditorTemplate"],
            PluginId);

        // Most Vision Actions use the host's clean image-only workspace on single click.
        // Contour Features demonstrates one optional external workspace that is still result-only:
        // it shows the processed image and read-only results, never the dialog's parameter editor.
        registry.RegisterWorkspace(
            OpenCvDesignerKeys.ContourFeaturesWorkspace,
            context => new FeatureResultWorkspaceView
            {
                DataContext = new VisionActionDesignerViewModel(
                    context,
                    "Contour Feature Extraction",
                    "Processed feature result",
                    new[] { "OutputImage", "FeatureCount", "LargestArea", "LargestCenterX", "LargestCenterY" })
            },
            PluginId);

        registry.RegisterActionEditor(
            OpenCvDesignerKeys.CannyActionEditor,
            context => CreateEditor(context, "Canny Edge Detection", "External action-specific editor. Inputs edit the same Action instance used by the generic Property Panel.",
                "InputImage", "Threshold1", "Threshold2", "PublishPreview", "OutputImage"),
            PluginId);


        registry.RegisterActionEditor(
            OpenCvDesignerKeys.ContourFeaturesActionEditor,
            context => CreateEditor(context, "Contour Feature Extraction", "Feature-extraction editor inspired by the legacy vision tool dialogs. It edits the same current Action instance and can run the workflow to refresh this line's preview.",
                "MaskImage", "SourceImage", "MinimumArea", "MaximumFeatures", "PublishPreview",
                "OutputImage", "FeatureCount", "LargestArea", "LargestCenterX", "LargestCenterY"),
            PluginId);

        registry.RegisterActionEditor(
            OpenCvDesignerKeys.MeasureLineActionEditor,
            context => CreateEditor(context, "Measure Line", "Measurement editor inspired by the legacy MeasureLine dialog, implemented with OpenCvSharp.",
                "InputImage", "Threshold1", "Threshold2", "HoughThreshold", "MinLineLength", "MaxLineGap", "PublishPreview", "OutputImage", "Found", "X1", "Y1", "X2", "Y2", "Length"),
            PluginId);

        registry.RegisterActionEditor(
            OpenCvDesignerKeys.MeasureCircleActionEditor,
            context => CreateEditor(context, "Measure Circle", "Measurement editor inspired by the legacy MeasureCircle dialog, implemented with OpenCvSharp.",
                "InputImage", "MinDist", "EdgeThreshold", "CircleAccumulator", "MinRadius", "MaxRadius", "PublishPreview", "OutputImage", "Found", "CenterX", "CenterY", "Radius", "Diameter"),
            PluginId);

        registry.RegisterActionEditor(
            OpenCvDesignerKeys.TemplateMatchActionEditor,
            context => CreateEditor(context, "Template Matching", "Matching editor inspired by the legacy Matching module. Runtime execution stays completely outside WPF.",
                "InputImage", "TemplateImage", "MatchMode", "MinimumScore", "PublishPreview", "OutputImage", "Matched", "Score", "MatchX", "MatchY"),
            PluginId);

        registry.RegisterActionEditor(
            OpenCvDesignerKeys.InteractiveTemplateMatchActionEditor,
            context => new InteractiveTemplateMatchWindow(new InteractiveTemplateMatchViewModel(context)),
            PluginId);
    }

    private static ResourceDictionary LoadResources()
    {
        const string resourceName = "OpenCvDesignerResources.xaml";
        using var stream = typeof(OpenCvSampleDesignerExtension).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded WPF resource '{resourceName}' was not found.");
        return XamlReader.Load(stream) as ResourceDictionary
            ?? throw new InvalidOperationException($"Embedded WPF resource '{resourceName}' is not a ResourceDictionary.");
    }

    private static Window CreateEditor(
        IWorkflowDesignerActionContext context,
        string title,
        string description,
        params string[] fields)
        => new VisionToolEditorWindow(new VisionActionDesignerViewModel(context, title, description, fields));
}
