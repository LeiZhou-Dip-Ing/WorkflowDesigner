using OpenCvSharp;
using WorkflowRuntime.ActionSdk;
using WorkflowRuntime.OpenCvSamplePlugin.Shared;

namespace WorkflowRuntime.OpenCvSamplePlugin;

[WorkflowAction(
    "sample.vision.measureCircle",
    "SDK Measure Circle",
    ActionId = "9aa8e56e-814c-49ae-a7bd-d8e321e4ba3d",
    Category = "Vision / SDK Plugin / Measurement",
    Description = "Detects a dominant circle, calculates its diameter, and overlays the measured dimension.",
    DisplayTemplate = "Measure circle {InputImage} → {OutputImage}",
    WorkspaceKind = WorkflowWorkspaceKeys.Image,
    DoubleClickEditor = OpenCvDesignerKeys.MeasureCircleActionEditor)]
public sealed class MeasureCircleSdkAction : WorkflowActionBase
{
    [WorkflowActionInput(DisplayName = "Input image", ValueType = "image", Editor = WorkflowPropertyEditorKeys.Variable,
        DataSource = "methodVariables", AllowCustomValue = false, AllowClear = true, Required = true, Order = 0)]
    public string InputImage { get; set; } = string.Empty;

    [WorkflowActionInput(DisplayName = "Minimum center distance", ValueType = "number", Editor = WorkflowPropertyEditorKeys.Number,
        Minimum = 1, Required = true, Order = 1)]
    public double MinDist { get; set; } = 40;

    [WorkflowActionInput(DisplayName = "Edge threshold", ValueType = "number", Editor = WorkflowPropertyEditorKeys.Number,
        Minimum = 1, Required = true, Order = 2)]
    public double EdgeThreshold { get; set; } = 120;

    [WorkflowActionInput(DisplayName = "Circle accumulator", ValueType = "number", Editor = WorkflowPropertyEditorKeys.Number,
        Minimum = 1, Required = true, Order = 3)]
    public double CircleAccumulator { get; set; } = 28;

    [WorkflowActionInput(DisplayName = "Minimum radius", ValueType = "integer", Editor = WorkflowPropertyEditorKeys.Number,
        Minimum = 0, Required = true, Order = 4)]
    public int MinRadius { get; set; } = 15;

    [WorkflowActionInput(DisplayName = "Maximum radius", ValueType = "integer", Editor = WorkflowPropertyEditorKeys.Number,
        Minimum = 0, Required = true, Order = 5)]
    public int MaxRadius { get; set; } = 160;

    [WorkflowActionInput(DisplayName = "Show image preview", ValueType = "boolean", Editor = WorkflowPropertyEditorKeys.Checkbox,
        Required = true, Order = 6)]
    public bool PublishPreview { get; set; } = true;

    [WorkflowActionOutput(DisplayName = "Output image", ValueType = "image", Required = true, Order = 10)]
    public string OutputImage { get; private set; } = string.Empty;
    [WorkflowActionOutput(DisplayName = "Circle found", ValueType = "boolean", Required = false, Order = 11)]
    public bool Found { get; private set; }
    [WorkflowActionOutput(DisplayName = "Center X", ValueType = "number", Required = false, Order = 12)]
    public double CenterX { get; private set; }
    [WorkflowActionOutput(DisplayName = "Center Y", ValueType = "number", Required = false, Order = 13)]
    public double CenterY { get; private set; }
    [WorkflowActionOutput(DisplayName = "Radius (px)", ValueType = "number", Required = false, Order = 14)]
    public double Radius { get; private set; }
    [WorkflowActionOutput(DisplayName = "Diameter (px)", ValueType = "number", Required = false, Order = 15)]
    public double Diameter { get; private set; }

    protected override ValueTask ExecuteActionAsync(IWorkflowActionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var vision = OpenCvActionSupport.RequireResources(context);
        var input = OpenCvActionSupport.RequireImage(vision, InputImage);
        using var gray = OpenCvActionSupport.ToGrayClone(input);
        using var smooth = new Mat();
        Cv2.GaussianBlur(gray, smooth, new Size(9, 9), 2, 2);
        var circles = Cv2.HoughCircles(smooth, HoughModes.Gradient, 1.2, Math.Max(1, MinDist),
            Math.Max(1, EdgeThreshold), Math.Max(1, CircleAccumulator), Math.Max(0, MinRadius), Math.Max(0, MaxRadius));
        var annotated = OpenCvActionSupport.ToBgrClone(input);

        if (circles.Length > 0)
        {
            var best = circles.OrderByDescending(circle => circle.Radius).First();
            Found = true;
            CenterX = best.Center.X;
            CenterY = best.Center.Y;
            Radius = best.Radius;
            Diameter = Radius * 2.0;
            var center = new Point((int)Math.Round(CenterX), (int)Math.Round(CenterY));
            var left = new Point((int)Math.Round(CenterX - Radius), center.Y);
            var right = new Point((int)Math.Round(CenterX + Radius), center.Y);
            Cv2.Circle(annotated, center, (int)Math.Round(Radius), new Scalar(0, 220, 255), 3, LineTypes.AntiAlias);
            Cv2.Circle(annotated, center, 5, new Scalar(0, 255, 0), -1, LineTypes.AntiAlias);
            Cv2.Line(annotated, left, right, new Scalar(0, 255, 0), 2, LineTypes.AntiAlias);
            Cv2.Line(annotated, new Point(left.X, left.Y - 9), new Point(left.X, left.Y + 9), new Scalar(0, 255, 0), 2, LineTypes.AntiAlias);
            Cv2.Line(annotated, new Point(right.X, right.Y - 9), new Point(right.X, right.Y + 9), new Scalar(0, 255, 0), 2, LineTypes.AntiAlias);
            Cv2.PutText(annotated, $"Diameter = {Diameter:0.0} px",
                new Point(Math.Max(5, center.X - 105), Math.Max(28, center.Y - 16)),
                HersheyFonts.HersheySimplex, 0.68, new Scalar(0, 255, 0), 2, LineTypes.AntiAlias);
        }
        else
        {
            Found = false;
            CenterX = CenterY = Radius = Diameter = 0;
            Cv2.PutText(annotated, "No circle found", new Point(18, 34), HersheyFonts.HersheySimplex, 0.8,
                new Scalar(0, 0, 255), 2, LineTypes.AntiAlias);
        }

        OutputImage = vision.StoreResource(annotated, OpenCvActionSupport.Metadata(annotated, "SDK plugin: MeasureCircle"), PublishPreview);
        context.Log(Found
            ? $"Circle found at ({CenterX:0.0},{CenterY:0.0}); diameter {Diameter:0.0}px."
            : "No circle found.");
        return ValueTask.CompletedTask;
    }
}
