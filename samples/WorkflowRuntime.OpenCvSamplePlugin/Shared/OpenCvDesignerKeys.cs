namespace WorkflowRuntime.OpenCvSamplePlugin.Shared;

/// <summary>
/// Stable plugin-owned protocol keys shared by the Runtime Action assembly and optional Designer UI assembly.
/// </summary>
public static class OpenCvDesignerKeys
{
    public const string OddKernelPropertyEditor = "sample.opencv.property.odd-kernel";

    public const string GrayWorkspace = "sample.opencv.workspace.gray";
    public const string GaussianBlurWorkspace = "sample.opencv.workspace.gaussian-blur";
    public const string ThresholdWorkspace = "sample.opencv.workspace.threshold";
    public const string MorphologyWorkspace = "sample.opencv.workspace.morphology-close";
    public const string ContourFeaturesWorkspace = "sample.opencv.workspace.contour-features";
    public const string MeasureLineWorkspace = "sample.opencv.workspace.measure-line";
    public const string MeasureCircleWorkspace = "sample.opencv.workspace.measure-circle";
    public const string TemplateMatchWorkspace = "sample.opencv.workspace.template-match";

    public const string CannyActionEditor = "sample.opencv.editor.canny";
    public const string MeasureLineActionEditor = "sample.opencv.editor.measure-line";
    public const string MeasureCircleActionEditor = "sample.opencv.editor.measure-circle";
    public const string TemplateMatchActionEditor = "sample.opencv.editor.template-match";
    public const string InteractiveTemplateMatchActionEditor = "sample.opencv.editor.interactive-template-match";
    public const string ContourFeaturesActionEditor = "sample.opencv.editor.contour-features";
}

public static class OpenCvActionFields
{
    public const string InputImage = "InputImage";
    public const string OutputImage = "OutputImage";
    public const string PublishPreview = "PublishPreview";
    public const string KernelSize = "KernelSize";
    public const string SigmaX = "SigmaX";
    public const string Threshold = "Threshold";
    public const string MaxValue = "MaxValue";
    public const string ThresholdMode = "ThresholdMode";
    public const string Threshold1 = "Threshold1";
    public const string Threshold2 = "Threshold2";
    public const string HoughThreshold = "HoughThreshold";
    public const string MinLineLength = "MinLineLength";
    public const string MaxLineGap = "MaxLineGap";
    public const string MinRadius = "MinRadius";
    public const string MaxRadius = "MaxRadius";
    public const string CircleAccumulator = "CircleAccumulator";
    public const string TemplateImage = "TemplateImage";
    public const string MatchMode = "MatchMode";
    public const string MinimumScore = "MinimumScore";
    public const string Iterations = "Iterations";
    public const string MaskImage = "MaskImage";
    public const string SourceImage = "SourceImage";
    public const string MinimumArea = "MinimumArea";
    public const string MaximumFeatures = "MaximumFeatures";
}
