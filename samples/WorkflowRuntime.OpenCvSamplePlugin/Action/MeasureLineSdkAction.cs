using OpenCvSharp;
using WorkflowDesigner.Contracts;
using WorkflowRuntime.ActionSdk;
using WorkflowRuntime.OpenCvSamplePlugin.Shared;

namespace WorkflowRuntime.OpenCvSamplePlugin;

[WorkflowAction(
    "sample.vision.measureLine",
    "SDK Measure Line",
    ActionId = "f6e2ac7f-495a-4a04-8424-05aa1aa2b780",
    Category = "Vision / SDK Plugin / Measurement",
    Description = "Finds the longest line segment with Canny + probabilistic Hough transform and draws the result.",
    DisplayTemplate = "Measure line {InputImage} → {OutputImage}",
    ActionKind = WorkflowActionKinds.Vision,
    WorkspaceKind = WorkflowWorkspaceKeys.Image,
    DoubleClickEditor = OpenCvDesignerKeys.MeasureLineActionEditor)]
public sealed class MeasureLineSdkAction : WorkflowActionBase
{
    [WorkflowActionInput(DisplayName = "Input image", ValueType = "image", Editor = WorkflowPropertyEditorKeys.Variable,
        DataSource = "methodVariables", AllowCustomValue = false, AllowClear = true, Required = true, Order = 0)]
    public string InputImage { get; set; } = string.Empty;

    [WorkflowActionInput(DisplayName = "Canny lower", ValueType = "number", Editor = WorkflowPropertyEditorKeys.Number,
        Minimum = 0, Required = true, Order = 1)]
    public double Threshold1 { get; set; } = 60;

    [WorkflowActionInput(DisplayName = "Canny upper", ValueType = "number", Editor = WorkflowPropertyEditorKeys.Number,
        Minimum = 0, Required = true, Order = 2)]
    public double Threshold2 { get; set; } = 160;

    [WorkflowActionInput(DisplayName = "Hough votes", ValueType = "integer", Editor = WorkflowPropertyEditorKeys.Number,
        Minimum = 1, Required = true, Order = 3)]
    public int HoughThreshold { get; set; } = 45;

    [WorkflowActionInput(DisplayName = "Minimum line length", ValueType = "number", Editor = WorkflowPropertyEditorKeys.Number,
        Minimum = 1, Required = true, Order = 4)]
    public double MinLineLength { get; set; } = 60;

    [WorkflowActionInput(DisplayName = "Maximum line gap", ValueType = "number", Editor = WorkflowPropertyEditorKeys.Number,
        Minimum = 0, Required = true, Order = 5)]
    public double MaxLineGap { get; set; } = 15;

    [WorkflowActionInput(DisplayName = "Show image preview", ValueType = "boolean", Editor = WorkflowPropertyEditorKeys.Checkbox,
        Required = true, Order = 6)]
    public bool PublishPreview { get; set; } = true;

    [WorkflowActionOutput(DisplayName = "Output image", ValueType = "image", Required = true, Order = 10)]
    public string OutputImage { get; private set; } = string.Empty;
    [WorkflowActionOutput(DisplayName = "Line found", ValueType = "boolean", Required = false, Order = 11)]
    public bool Found { get; private set; }
    [WorkflowActionOutput(DisplayName = "Start X", ValueType = "number", Required = false, Order = 12)]
    public double X1 { get; private set; }
    [WorkflowActionOutput(DisplayName = "Start Y", ValueType = "number", Required = false, Order = 13)]
    public double Y1 { get; private set; }
    [WorkflowActionOutput(DisplayName = "End X", ValueType = "number", Required = false, Order = 14)]
    public double X2 { get; private set; }
    [WorkflowActionOutput(DisplayName = "End Y", ValueType = "number", Required = false, Order = 15)]
    public double Y2 { get; private set; }
    [WorkflowActionOutput(DisplayName = "Length (px)", ValueType = "number", Required = false, Order = 16)]
    public double Length { get; private set; }

    protected override ValueTask ExecuteActionAsync(IWorkflowActionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var vision = OpenCvActionSupport.RequireVision(context);
        var input = OpenCvActionSupport.RequireImage(vision, InputImage);
        using var gray = OpenCvActionSupport.ToGrayClone(input);
        using var edges = new Mat();
        Cv2.Canny(gray, edges, Threshold1, Threshold2);
        var lines = Cv2.HoughLinesP(edges, 1, Math.PI / 180.0, Math.Max(1, HoughThreshold), Math.Max(1, MinLineLength), Math.Max(0, MaxLineGap));
        var annotated = OpenCvActionSupport.ToBgrClone(input);

        if (lines.Length > 0)
        {
            var best = lines.OrderByDescending(LineLength).First();
            Found = true;
            X1 = best.P1.X; Y1 = best.P1.Y; X2 = best.P2.X; Y2 = best.P2.Y; Length = LineLength(best);
            Cv2.Line(annotated, best.P1, best.P2, new Scalar(0, 220, 255), 3, LineTypes.AntiAlias);
            Cv2.Circle(annotated, best.P1, 6, new Scalar(0, 255, 0), -1, LineTypes.AntiAlias);
            Cv2.Circle(annotated, best.P2, 6, new Scalar(0, 255, 0), -1, LineTypes.AntiAlias);
            Cv2.PutText(annotated, $"L={Length:0.0}px", new Point(Math.Max(5, best.P1.X), Math.Max(24, best.P1.Y - 10)),
                HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 220, 255), 2, LineTypes.AntiAlias);
        }
        else
        {
            Found = false;
            X1 = Y1 = X2 = Y2 = Length = 0;
            Cv2.PutText(annotated, "No line found", new Point(18, 34), HersheyFonts.HersheySimplex, 0.8,
                new Scalar(0, 0, 255), 2, LineTypes.AntiAlias);
        }

        OutputImage = vision.StoreImage(annotated, OpenCvActionSupport.Metadata(annotated, "SDK plugin: MeasureLine"), PublishPreview);
        context.Log(Found ? $"Line found: ({X1:0},{Y1:0}) -> ({X2:0},{Y2:0}), length {Length:0.0}px." : "No line segment found.");
        return ValueTask.CompletedTask;
    }

    private static double LineLength(LineSegmentPoint line)
    {
        var dx = line.P2.X - line.P1.X;
        var dy = line.P2.Y - line.P1.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
