using OpenCvSharp;
using WorkflowRuntime.ActionSdk;
using WorkflowRuntime.OpenCvSamplePlugin.Shared;

namespace WorkflowRuntime.OpenCvSamplePlugin;

[WorkflowAction(
    "sample.vision.templateMatch",
    "SDK Template Match",
    ActionId = "cd8d3279-8534-4ff4-b40f-562a295d4fb2",
    Category = "Vision / SDK Plugin / Matching",
    Description = "Matches a template image against the input image with OpenCV MatchTemplate and overlays the best result.",
    DisplayTemplate = "Match {TemplateImage} in {InputImage} → {OutputImage}",
    ActionKind = WorkflowActionKinds.Vision,
    WorkspaceKind = WorkflowWorkspaceKeys.Image,
    DoubleClickEditor = OpenCvDesignerKeys.TemplateMatchActionEditor)]
public sealed class TemplateMatchSdkAction : WorkflowActionBase
{
    [WorkflowActionInput(DisplayName = "Input image", ValueType = "image", Editor = WorkflowPropertyEditorKeys.Variable,
        DataSource = "methodVariables", AllowCustomValue = false, AllowClear = true, Required = true, Order = 0)]
    public string InputImage { get; set; } = string.Empty;

    [WorkflowActionInput(DisplayName = "Template image", ValueType = "image", Editor = WorkflowPropertyEditorKeys.Variable,
        DataSource = "methodVariables", AllowCustomValue = false, AllowClear = true, Required = true, Order = 1)]
    public string TemplateImage { get; set; } = string.Empty;

    [WorkflowActionInput(DisplayName = "Match mode", ValueType = "string", Editor = WorkflowPropertyEditorKeys.Select,
        EnumValues = new string[] { "CCoeffNormed", "CCorrNormed", "SqDiffNormed" }, Required = true, Order = 2)]
    public string MatchMode { get; set; } = "CCoeffNormed";

    [WorkflowActionInput(DisplayName = "Minimum score", ValueType = "number", Editor = WorkflowPropertyEditorKeys.Number,
        Minimum = 0, Maximum = 1, Required = true, Order = 3)]
    public double MinimumScore { get; set; } = 0.8;

    [WorkflowActionInput(DisplayName = "Show image preview", ValueType = "boolean", Editor = WorkflowPropertyEditorKeys.Checkbox,
        Required = true, Order = 4)]
    public bool PublishPreview { get; set; } = true;

    [WorkflowActionOutput(DisplayName = "Output image", ValueType = "image", Required = true, Order = 10)]
    public string OutputImage { get; private set; } = string.Empty;
    [WorkflowActionOutput(DisplayName = "Matched", ValueType = "boolean", Required = false, Order = 11)]
    public bool Matched { get; private set; }
    [WorkflowActionOutput(DisplayName = "Score", ValueType = "number", Required = false, Order = 12)]
    public double Score { get; private set; }
    [WorkflowActionOutput(DisplayName = "Match X", ValueType = "number", Required = false, Order = 13)]
    public double MatchX { get; private set; }
    [WorkflowActionOutput(DisplayName = "Match Y", ValueType = "number", Required = false, Order = 14)]
    public double MatchY { get; private set; }

    protected override ValueTask ExecuteActionAsync(IWorkflowActionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var vision = OpenCvActionSupport.RequireVision(context);
        var input = OpenCvActionSupport.RequireImage(vision, InputImage);
        var template = OpenCvActionSupport.RequireImage(vision, TemplateImage, "Template image");
        if (template.Width > input.Width || template.Height > input.Height)
        {
            throw new InvalidOperationException("Template image must not be larger than the input image.");
        }

        using var inputGray = OpenCvActionSupport.ToGrayClone(input);
        using var templateGray = OpenCvActionSupport.ToGrayClone(template);
        using var result = new Mat();
        var mode = ParseMode(MatchMode);
        Cv2.MatchTemplate(inputGray, templateGray, result, mode);
        Cv2.MinMaxLoc(result, out var minValue, out var maxValue, out var minLocation, out var maxLocation);
        var isSqDiff = mode is TemplateMatchModes.SqDiff or TemplateMatchModes.SqDiffNormed;
        Score = isSqDiff ? 1.0 - minValue : maxValue;
        var location = isSqDiff ? minLocation : maxLocation;
        MatchX = location.X;
        MatchY = location.Y;
        Matched = Score >= Math.Clamp(MinimumScore, 0, 1);

        var annotated = OpenCvActionSupport.ToBgrClone(input);
        var color = Matched ? new Scalar(0, 220, 0) : new Scalar(0, 0, 255);
        Cv2.Rectangle(annotated, new Rect(location.X, location.Y, template.Width, template.Height), color, 3, LineTypes.AntiAlias);
        Cv2.PutText(annotated, $"Score={Score:0.000}", new Point(Math.Max(5, location.X), Math.Max(25, location.Y - 10)),
            HersheyFonts.HersheySimplex, 0.7, color, 2, LineTypes.AntiAlias);

        OutputImage = vision.StoreResource(annotated, OpenCvActionSupport.Metadata(annotated, "SDK plugin: TemplateMatch"), PublishPreview);
        context.Log($"Template match score {Score:0.000} at ({MatchX:0},{MatchY:0}); accepted={Matched}.");
        return ValueTask.CompletedTask;
    }

    private static TemplateMatchModes ParseMode(string? mode)
        => (mode ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "ccorrnormed" => TemplateMatchModes.CCorrNormed,
            "sqdiffnormed" => TemplateMatchModes.SqDiffNormed,
            _ => TemplateMatchModes.CCoeffNormed
        };
}
