using OpenCvSharp;
using WorkflowRuntime.ActionSdk;
using WorkflowRuntime.OpenCvSamplePlugin.Shared;

namespace WorkflowRuntime.OpenCvSamplePlugin;

[WorkflowAction(
    "sample.vision.contourFeatures",
    "SDK Extract Contour Features",
    ActionId = "ac7eb61d-1b49-4a24-a7c8-e14b63ad4e66",
    Category = "Vision / SDK Plugin / Feature Extraction",
    Description = "Extracts contour-based object features from a binary mask and overlays bounding boxes, centroids and areas on the source image.",
    DisplayTemplate = "Features {MaskImage} on {SourceImage} → {OutputImage}",
    WorkspaceKind = OpenCvDesignerKeys.ContourFeaturesWorkspace,
    DoubleClickEditor = OpenCvDesignerKeys.ContourFeaturesActionEditor)]
public sealed class ContourFeaturesSdkAction : WorkflowActionBase
{
    [WorkflowActionInput(DisplayName = "Binary mask", ValueType = "image", Editor = WorkflowPropertyEditorKeys.Variable,
        DataSource = "methodVariables", AllowCustomValue = false, AllowClear = true, Required = true, Order = 0)]
    public string MaskImage { get; set; } = string.Empty;

    [WorkflowActionInput(DisplayName = "Source image", Description = "Optional image used as the annotation background.", ValueType = "image",
        Editor = WorkflowPropertyEditorKeys.Variable, DataSource = "methodVariables", AllowCustomValue = false, AllowClear = true,
        Required = false, Order = 1)]
    public string SourceImage { get; set; } = string.Empty;

    [WorkflowActionInput(DisplayName = "Minimum area", ValueType = "number", Editor = WorkflowPropertyEditorKeys.Number,
        Minimum = 1, Required = true, Order = 2)]
    public double MinimumArea { get; set; } = 900;

    [WorkflowActionInput(DisplayName = "Maximum features", ValueType = "integer", Editor = WorkflowPropertyEditorKeys.Number,
        Minimum = 1, Maximum = 100, Required = true, Order = 3)]
    public int MaximumFeatures { get; set; } = 12;

    [WorkflowActionInput(DisplayName = "Show image preview", ValueType = "boolean", Editor = WorkflowPropertyEditorKeys.Checkbox,
        Required = true, Order = 4)]
    public bool PublishPreview { get; set; } = true;

    [WorkflowActionOutput(DisplayName = "Output image", ValueType = "image", Required = true, Order = 10)]
    public string OutputImage { get; private set; } = string.Empty;

    [WorkflowActionOutput(DisplayName = "Feature count", ValueType = "integer", Required = false, Order = 11)]
    public int FeatureCount { get; private set; }

    [WorkflowActionOutput(DisplayName = "Largest area", ValueType = "number", Required = false, Order = 12)]
    public double LargestArea { get; private set; }

    [WorkflowActionOutput(DisplayName = "Largest center X", ValueType = "number", Required = false, Order = 13)]
    public double LargestCenterX { get; private set; }

    [WorkflowActionOutput(DisplayName = "Largest center Y", ValueType = "number", Required = false, Order = 14)]
    public double LargestCenterY { get; private set; }

    protected override ValueTask ExecuteActionAsync(IWorkflowActionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var vision = OpenCvActionSupport.RequireResources(context);
        var maskInput = OpenCvActionSupport.RequireImage(vision, MaskImage, "Binary mask");
        using var mask = OpenCvActionSupport.ToGrayClone(maskInput);

        Mat annotated;
        if (!string.IsNullOrWhiteSpace(SourceImage) && vision.TryGetResource<Mat>(SourceImage, out var source) && source != null)
        {
            annotated = OpenCvActionSupport.ToBgrClone(source);
        }
        else
        {
            annotated = OpenCvActionSupport.ToBgrClone(mask);
        }

        Cv2.FindContours(mask, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        var features = contours
            .Select(contour => new
            {
                Contour = contour,
                Area = Math.Abs(Cv2.ContourArea(contour)),
                Rect = Cv2.BoundingRect(contour)
            })
            .Where(item => item.Area >= Math.Max(1, MinimumArea))
            .OrderByDescending(item => item.Area)
            .Take(Math.Max(1, MaximumFeatures))
            .ToArray();

        FeatureCount = features.Length;
        LargestArea = 0;
        LargestCenterX = 0;
        LargestCenterY = 0;

        for (var i = 0; i < features.Length; i++)
        {
            var feature = features[i];
            var moments = Cv2.Moments(feature.Contour);
            var centerX = Math.Abs(moments.M00) > double.Epsilon
                ? moments.M10 / moments.M00
                : feature.Rect.X + feature.Rect.Width / 2.0;
            var centerY = Math.Abs(moments.M00) > double.Epsilon
                ? moments.M01 / moments.M00
                : feature.Rect.Y + feature.Rect.Height / 2.0;

            if (i == 0)
            {
                LargestArea = feature.Area;
                LargestCenterX = centerX;
                LargestCenterY = centerY;
            }

            var color = i switch
            {
                0 => new Scalar(0, 255, 0),
                1 => new Scalar(0, 220, 255),
                2 => new Scalar(255, 160, 0),
                _ => new Scalar(255, 80, 210)
            };

            Cv2.DrawContours(annotated, new[] { feature.Contour }, -1, color, 2, LineTypes.AntiAlias);
            Cv2.Rectangle(annotated, feature.Rect, color, 2, LineTypes.AntiAlias);
            var center = new Point((int)Math.Round(centerX), (int)Math.Round(centerY));
            Cv2.Circle(annotated, center, 5, color, -1, LineTypes.AntiAlias);
            Cv2.PutText(
                annotated,
                $"F{i + 1} A={feature.Area:0}",
                new Point(feature.Rect.X, Math.Max(22, feature.Rect.Y - 8)),
                HersheyFonts.HersheySimplex,
                0.58,
                color,
                2,
                LineTypes.AntiAlias);
        }

        if (features.Length == 0)
        {
            Cv2.PutText(annotated, "No contour features found", new Point(18, 34), HersheyFonts.HersheySimplex, 0.75,
                new Scalar(0, 0, 255), 2, LineTypes.AntiAlias);
        }

        Cv2.PutText(
            annotated,
            $"Features: {FeatureCount}",
            new Point(18, annotated.Height - 20),
            HersheyFonts.HersheySimplex,
            0.72,
            new Scalar(255, 255, 255),
            2,
            LineTypes.AntiAlias);

        OutputImage = vision.StoreResource(
            annotated,
            OpenCvActionSupport.Metadata(annotated, $"SDK plugin: Contour Features count={FeatureCount}"),
            PublishPreview);

        context.Log(FeatureCount > 0
            ? $"Extracted {FeatureCount} contour features. Largest area={LargestArea:0.0}, center=({LargestCenterX:0.0},{LargestCenterY:0.0})."
            : "No contour features matched the configured area filter.");

        return ValueTask.CompletedTask;
    }
}
