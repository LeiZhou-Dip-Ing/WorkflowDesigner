using System.Text.Json;
using OpenCvSharp;
using WorkflowRuntime.ActionSdk;
using WorkflowRuntime.OpenCvSamplePlugin.Shared;
using WorkflowRuntime.ResourceSdk;

namespace WorkflowRuntime.OpenCvSamplePlugin;

[WorkflowAction(
    "sample.vision.interactiveTemplateMatch",
    "SDK Learned Template Match",
    ActionId = "cbb244fa-cf7a-4ad1-83cc-58c2ff9c5dc9",
    Category = "Vision / SDK Plugin / Matching",
    Description = "Learns a masked gray/shape template from a hand-selected ROI, persists the model, then searches with angle and scale tolerance.",
    DisplayTemplate = "Learned template match {SearchImage} → {OutputImage}",
    ActionKind = WorkflowActionKinds.Vision,
    WorkspaceKind = WorkflowWorkspaceKeys.Image,
    DoubleClickEditor = OpenCvDesignerKeys.InteractiveTemplateMatchActionEditor)]
public sealed class InteractiveTemplateMatchSdkAction : WorkflowActionBase
{
    [WorkflowActionInput(DisplayName = "Learning image", Category = "Images", ValueType = "image", Editor = WorkflowPropertyEditorKeys.Variable,
        DataSource = "methodVariables", AllowCustomValue = false, AllowClear = true, Required = true, Order = 0)]
    public string TemplateSourceImage { get; set; } = string.Empty;

    [WorkflowActionInput(DisplayName = "Search image", Category = "Images", ValueType = "image", Editor = WorkflowPropertyEditorKeys.Variable,
        DataSource = "methodVariables", AllowCustomValue = false, AllowClear = true, Required = true, Order = 1)]
    public string SearchImage { get; set; } = string.Empty;

    [WorkflowActionInput(DisplayName = "Learn ROI X", Category = "Learning ROI", ValueType = "integer", Editor = WorkflowPropertyEditorKeys.Number, Minimum = 0, Order = 10)]
    public int RoiX { get; set; } = 160;
    [WorkflowActionInput(DisplayName = "Learn ROI Y", Category = "Learning ROI", ValueType = "integer", Editor = WorkflowPropertyEditorKeys.Number, Minimum = 0, Order = 11)]
    public int RoiY { get; set; } = 145;
    [WorkflowActionInput(DisplayName = "Learn ROI width", Category = "Learning ROI", ValueType = "integer", Editor = WorkflowPropertyEditorKeys.Number, Minimum = 2, Order = 12)]
    public int RoiWidth { get; set; } = 120;
    [WorkflowActionInput(DisplayName = "Learn ROI height", Category = "Learning ROI", ValueType = "integer", Editor = WorkflowPropertyEditorKeys.Number, Minimum = 2, Order = 13)]
    public int RoiHeight { get; set; } = 100;

    [WorkflowActionInput(DisplayName = "Search ROI X", Category = "Search ROI", ValueType = "integer", Editor = WorkflowPropertyEditorKeys.Number, Minimum = 0, Order = 20)]
    public int SearchRoiX { get; set; }
    [WorkflowActionInput(DisplayName = "Search ROI Y", Category = "Search ROI", ValueType = "integer", Editor = WorkflowPropertyEditorKeys.Number, Minimum = 0, Order = 21)]
    public int SearchRoiY { get; set; }
    [WorkflowActionInput(DisplayName = "Search ROI width (0 = full)", Category = "Search ROI", ValueType = "integer", Editor = WorkflowPropertyEditorKeys.Number, Minimum = 0, Order = 22)]
    public int SearchRoiWidth { get; set; }
    [WorkflowActionInput(DisplayName = "Search ROI height (0 = full)", Category = "Search ROI", ValueType = "integer", Editor = WorkflowPropertyEditorKeys.Number, Minimum = 0, Order = 23)]
    public int SearchRoiHeight { get; set; }

    [WorkflowActionInput(DisplayName = "Model file", Description = "JSON model file on the Runtime host; companion template and mask PNG files are stored beside it.",
        Category = "Model", ValueType = "string", Editor = WorkflowPropertyEditorKeys.Text, Order = 30)]
    public string TemplateFilePath { get; set; } = "%TEMP%\\WorkflowVisionDemo\\learned-template.json";

    [WorkflowActionInput(DisplayName = "Model type", Category = "Model", ValueType = "string", Editor = WorkflowPropertyEditorKeys.Select,
        EnumValues = new[] { "Shape", "Gray" }, Order = 31)]
    public string ModelType { get; set; } = "Shape";

    [WorkflowActionInput(DisplayName = "Low edge threshold", Category = "Extraction", ValueType = "integer", Editor = WorkflowPropertyEditorKeys.Number,
        Minimum = 0, Maximum = 255, Order = 40)]
    public int EdgeThresholdLow { get; set; } = 40;
    [WorkflowActionInput(DisplayName = "High edge threshold", Category = "Extraction", ValueType = "integer", Editor = WorkflowPropertyEditorKeys.Number,
        Minimum = 1, Maximum = 255, Order = 41)]
    public int EdgeThresholdHigh { get; set; } = 120;
    [WorkflowActionInput(DisplayName = "Blur size", Category = "Extraction", ValueType = "integer", Editor = WorkflowPropertyEditorKeys.Number,
        Minimum = 0, Maximum = 15, Order = 42)]
    public int BlurSize { get; set; } = 3;

    [WorkflowActionInput(DisplayName = "Minimum angle (°)", Category = "Search parameters", ValueType = "number", Editor = WorkflowPropertyEditorKeys.Number,
        Minimum = -180, Maximum = 180, Order = 50)]
    public double MinAngle { get; set; }
    [WorkflowActionInput(DisplayName = "Maximum angle (°)", Category = "Search parameters", ValueType = "number", Editor = WorkflowPropertyEditorKeys.Number,
        Minimum = -180, Maximum = 180, Order = 51)]
    public double MaxAngle { get; set; }
    [WorkflowActionInput(DisplayName = "Angle step (°)", Category = "Search parameters", ValueType = "number", Editor = WorkflowPropertyEditorKeys.Number,
        Minimum = 0.1, Maximum = 90, Order = 52)]
    public double AngleStep { get; set; } = 5;
    [WorkflowActionInput(DisplayName = "Minimum scale", Category = "Search parameters", ValueType = "number", Editor = WorkflowPropertyEditorKeys.Number,
        Minimum = 0.1, Maximum = 5, Order = 53)]
    public double MinScale { get; set; } = 1;
    [WorkflowActionInput(DisplayName = "Maximum scale", Category = "Search parameters", ValueType = "number", Editor = WorkflowPropertyEditorKeys.Number,
        Minimum = 0.1, Maximum = 5, Order = 54)]
    public double MaxScale { get; set; } = 1;
    [WorkflowActionInput(DisplayName = "Scale step", Category = "Search parameters", ValueType = "number", Editor = WorkflowPropertyEditorKeys.Number,
        Minimum = 0.01, Maximum = 1, Order = 55)]
    public double ScaleStep { get; set; } = 0.1;
    [WorkflowActionInput(DisplayName = "Minimum score", Category = "Search parameters", ValueType = "number", Editor = WorkflowPropertyEditorKeys.Number,
        Minimum = 0, Maximum = 1, Order = 56)]
    public double MinimumScore { get; set; } = 0.75;
    [WorkflowActionInput(DisplayName = "Maximum matches", Category = "Search parameters", ValueType = "integer", Editor = WorkflowPropertyEditorKeys.Number,
        Minimum = 1, Maximum = 50, Order = 57)]
    public int MaximumMatches { get; set; } = 1;
    [WorkflowActionInput(DisplayName = "Maximum overlap", Category = "Search parameters", ValueType = "number", Editor = WorkflowPropertyEditorKeys.Number,
        Minimum = 0, Maximum = 1, Order = 58)]
    public double MaximumOverlap { get; set; } = 0.3;

    [WorkflowActionInput(DisplayName = "Show search region", Category = "Display", ValueType = "boolean", Editor = WorkflowPropertyEditorKeys.Checkbox, Order = 60)]
    public bool ShowSearchRegion { get; set; } = true;
    [WorkflowActionInput(DisplayName = "Show result contour", Category = "Display", ValueType = "boolean", Editor = WorkflowPropertyEditorKeys.Checkbox, Order = 61)]
    public bool ShowResultContour { get; set; } = true;
    [WorkflowActionInput(DisplayName = "Publish preview", Category = "Display", ValueType = "boolean", Editor = WorkflowPropertyEditorKeys.Checkbox, Order = 62)]
    public bool PublishPreview { get; set; } = true;

    [WorkflowActionInput(DisplayName = "Operation", Category = "Designer state", ValueType = "string", Editor = WorkflowPropertyEditorKeys.Select,
        EnumValues = new[] { "MatchOnly", "PreviewLearning", "LearnAndMatch" }, Order = 90)]
    public string Operation { get; set; } = "LearnAndMatch";
    [WorkflowActionInput(DisplayName = "Edited mask", Description = "SDK-owned run-length encoded mask generated by the template editor.",
        Category = "Designer state", ValueType = "string", Editor = WorkflowPropertyEditorKeys.Text, Required = false, Order = 91)]
    public string TemplateMaskData { get; set; } = string.Empty;

    [WorkflowActionOutput(DisplayName = "Template image", ValueType = "image", Required = false, Order = 100)]
    public string TemplateImage { get; private set; } = string.Empty;
    [WorkflowActionOutput(DisplayName = "Match result image", ValueType = "image", Required = true, Order = 101)]
    public string OutputImage { get; private set; } = string.Empty;
    [WorkflowActionOutput(DisplayName = "Saved model path", ValueType = "string", Required = false, Order = 102)]
    public string SavedPath { get; private set; } = string.Empty;
    [WorkflowActionOutput(DisplayName = "Matched", ValueType = "boolean", Required = false, Order = 103)]
    public bool Matched { get; private set; }
    [WorkflowActionOutput(DisplayName = "Match count", ValueType = "integer", Required = false, Order = 104)]
    public int MatchCount { get; private set; }
    [WorkflowActionOutput(DisplayName = "Score", ValueType = "number", Required = false, Order = 105)]
    public double Score { get; private set; }
    [WorkflowActionOutput(DisplayName = "Center X", ValueType = "number", Required = false, Order = 106)]
    public double MatchX { get; private set; }
    [WorkflowActionOutput(DisplayName = "Center Y", ValueType = "number", Required = false, Order = 107)]
    public double MatchY { get; private set; }
    [WorkflowActionOutput(DisplayName = "Angle", ValueType = "number", Required = false, Order = 108)]
    public double MatchAngle { get; private set; }
    [WorkflowActionOutput(DisplayName = "Scale", ValueType = "number", Required = false, Order = 109)]
    public double MatchScale { get; private set; }
    [WorkflowActionOutput(DisplayName = "Template width", ValueType = "integer", Required = false, Order = 110)]
    public int TemplateWidth { get; private set; }
    [WorkflowActionOutput(DisplayName = "Template height", ValueType = "integer", Required = false, Order = 111)]
    public int TemplateHeight { get; private set; }

    protected override ValueTask ExecuteActionAsync(IWorkflowActionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var vision = OpenCvActionSupport.RequireVision(context);
        var learning = OpenCvActionSupport.RequireImage(vision, TemplateSourceImage, "Learning image");
        var search = OpenCvActionSupport.RequireImage(vision, SearchImage, "Search image");
        var learnRoi = NormalizeRoi(learning.Size(), RoiX, RoiY, RoiWidth, RoiHeight, false);
        var searchRoi = NormalizeRoi(search.Size(), SearchRoiX, SearchRoiY, SearchRoiWidth, SearchRoiHeight, true);

        if (string.Equals(Operation, "PreviewLearning", StringComparison.OrdinalIgnoreCase))
        {
            PublishLearningPreview(vision, learning, learnRoi, searchRoi);
            context.Log($"Learning preview ready. ROI=({learnRoi.X},{learnRoi.Y},{learnRoi.Width},{learnRoi.Height}).");
            return ValueTask.CompletedTask;
        }

        LearnedTemplateModel model;
        Mat template;
        Mat mask;
        if (string.Equals(Operation, "LearnAndMatch", StringComparison.OrdinalIgnoreCase))
        {
            (model, template, mask) = LearnModel(learning, learnRoi);
            SavedPath = SaveModel(model, template, mask);
        }
        else
        {
            (model, template, mask) = LoadModel();
            SavedPath = ResolveModelPath();
        }

        using (template)
        using (mask)
        {
            TemplateWidth = template.Width;
            TemplateHeight = template.Height;
            var templatePreview = RenderTemplatePreview(template, mask, model);
            TemplateImage = vision.StoreResource(templatePreview,
                OpenCvActionSupport.Metadata(templatePreview, "SDK learned template"), false);

            var matches = FindMatches(search, searchRoi, template, mask, model, cancellationToken);
            MatchCount = matches.Count;
            Matched = MatchCount > 0;
            if (Matched)
            {
                var best = matches[0];
                Score = best.Score;
                MatchX = best.Center.X;
                MatchY = best.Center.Y;
                MatchAngle = best.Angle;
                MatchScale = best.Scale;
            }

            var annotated = RenderMatches(search, searchRoi, matches);
            OutputImage = vision.StoreResource(annotated,
                OpenCvActionSupport.Metadata(annotated, "SDK learned template match"), PublishPreview);
            context.Log(Matched
                ? $"Template model matched {MatchCount} time(s); best score={Score:0.000}, center=({MatchX:0.0},{MatchY:0.0}), angle={MatchAngle:0.0}°, scale={MatchScale:0.00}."
                : $"No template match reached score {MinimumScore:0.000} inside search ROI.");
        }
        return ValueTask.CompletedTask;
    }

    private (LearnedTemplateModel Model, Mat Template, Mat Mask) LearnModel(Mat source, Rect roi)
    {
        var template = new Mat(source, roi).Clone();
        var mask = DecodeMask(TemplateMaskData, roi.Width, roi.Height) ?? new Mat(roi.Height, roi.Width, MatType.CV_8UC1, Scalar.All(255));
        if (Cv2.CountNonZero(mask) < 16)
        {
            template.Dispose(); mask.Dispose();
            throw new InvalidOperationException("The learned mask is empty. Add a valid template region before learning.");
        }
        var model = CurrentModel(roi.Width, roi.Height);
        return (model, template, mask);
    }

    private LearnedTemplateModel CurrentModel(int width, int height) => new()
    {
        Version = 2, ModelType = NormalizeModelType(ModelType), Width = width, Height = height,
        EdgeThresholdLow = Math.Clamp(EdgeThresholdLow, 0, 255), EdgeThresholdHigh = Math.Clamp(EdgeThresholdHigh, 1, 255),
        BlurSize = NormalizeBlur(BlurSize), MinAngle = Math.Min(MinAngle, MaxAngle), MaxAngle = Math.Max(MinAngle, MaxAngle),
        AngleStep = Math.Max(0.1, AngleStep), MinScale = Math.Max(0.1, Math.Min(MinScale, MaxScale)),
        MaxScale = Math.Max(0.1, Math.Max(MinScale, MaxScale)), ScaleStep = Math.Max(0.01, ScaleStep),
        LearnedUtc = DateTimeOffset.UtcNow
    };

    private string SaveModel(LearnedTemplateModel model, Mat template, Mat mask)
    {
        var modelPath = ResolveModelPath();
        var directory = Path.GetDirectoryName(modelPath)!;
        Directory.CreateDirectory(directory);
        var stem = Path.Combine(directory, Path.GetFileNameWithoutExtension(modelPath));
        model.TemplateFile = Path.GetFileName(stem + ".template.png");
        model.MaskFile = Path.GetFileName(stem + ".mask.png");
        if (!Cv2.ImWrite(Path.Combine(directory, model.TemplateFile), template)
            || !Cv2.ImWrite(Path.Combine(directory, model.MaskFile), mask))
            throw new IOException("OpenCV failed to persist the learned template model images.");
        File.WriteAllText(modelPath, JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true }));
        return modelPath;
    }

    private (LearnedTemplateModel Model, Mat Template, Mat Mask) LoadModel()
    {
        var path = ResolveModelPath();
        if (!File.Exists(path)) throw new FileNotFoundException("Learned template model does not exist. Open the Action editor and click Learn model first.", path);
        var model = JsonSerializer.Deserialize<LearnedTemplateModel>(File.ReadAllText(path))
                    ?? throw new InvalidDataException("The learned template model metadata is invalid.");
        var directory = Path.GetDirectoryName(path)!;
        var template = Cv2.ImRead(Path.Combine(directory, model.TemplateFile), ImreadModes.Color);
        var mask = Cv2.ImRead(Path.Combine(directory, model.MaskFile), ImreadModes.Grayscale);
        if (template.Empty() || mask.Empty()) { template.Dispose(); mask.Dispose(); throw new InvalidDataException("The learned template or mask image is missing."); }
        return (model, template, mask);
    }

    private List<MatchCandidate> FindMatches(Mat source, Rect roi, Mat template, Mat mask, LearnedTemplateModel model, CancellationToken token)
    {
        using var searchCrop = new Mat(source, roi);
        using var searchPrepared = Prepare(searchCrop, model);
        using var templatePrepared = Prepare(template, model);
        var candidates = new List<MatchCandidate>();
        foreach (var scale in Range(model.MinScale, model.MaxScale, model.ScaleStep))
        foreach (var angle in Range(model.MinAngle, model.MaxAngle, model.AngleStep))
        {
            token.ThrowIfCancellationRequested();
            using var variant = RotateScale(templatePrepared, angle, scale, InterpolationFlags.Linear);
            using var variantMask = RotateScale(mask, angle, scale, InterpolationFlags.Nearest);
            if (variant.Width > searchPrepared.Width || variant.Height > searchPrepared.Height || Cv2.CountNonZero(variantMask) < 16) continue;
            using var result = new Mat();
            // SQDIFF_NORMED is stable for sparse Canny/shape templates and is one of
            // OpenCV's template-match modes that supports a mask.  CCORR_NORMED can
            // rank empty background above the real object when most template pixels
            // are zero (a typical shape-model image).
            Cv2.MatchTemplate(searchPrepared, variant, result, TemplateMatchModes.SqDiffNormed, variantMask);
            Cv2.PatchNaNs(result, 1.0);
            for (var occurrence = 0; occurrence < Math.Max(1, MaximumMatches); occurrence++)
            {
                Cv2.MinMaxLoc(result, out var minimumError, out _, out var location, out _);
                if (!double.IsFinite(minimumError)) break;
                var score = Math.Clamp(1.0 - minimumError, 0.0, 1.0);
                if (score < Math.Clamp(MinimumScore, 0, 1)) break;
                var rect = new Rect(location.X + roi.X, location.Y + roi.Y, variant.Width, variant.Height);
                candidates.Add(new MatchCandidate(score, rect, angle, scale));
                var suppress = new Rect(Math.Max(0, location.X - variant.Width / 2), Math.Max(0, location.Y - variant.Height / 2),
                    Math.Min(result.Width - Math.Max(0, location.X - variant.Width / 2), variant.Width * 2),
                    Math.Min(result.Height - Math.Max(0, location.Y - variant.Height / 2), variant.Height * 2));
                if (suppress.Width > 0 && suppress.Height > 0) result[suppress].SetTo(Scalar.All(1));
            }
        }
        var accepted = new List<MatchCandidate>();
        foreach (var candidate in candidates.OrderByDescending(item => item.Score))
        {
            if (accepted.All(existing => Overlap(existing.Bounds, candidate.Bounds) <= Math.Clamp(MaximumOverlap, 0, 1)))
                accepted.Add(candidate);
            if (accepted.Count >= Math.Max(1, MaximumMatches)) break;
        }
        return accepted;
    }

    private Mat Prepare(Mat image, LearnedTemplateModel model)
    {
        var gray = OpenCvActionSupport.ToGrayClone(image);
        if (model.BlurSize >= 3) Cv2.GaussianBlur(gray, gray, new Size(model.BlurSize, model.BlurSize), 0);
        if (!string.Equals(model.ModelType, "Shape", StringComparison.OrdinalIgnoreCase)) return gray;
        var edges = new Mat();
        Cv2.Canny(gray, edges, model.EdgeThresholdLow, model.EdgeThresholdHigh);
        gray.Dispose();
        return edges;
    }

    private Mat RenderMatches(Mat source, Rect searchRoi, IReadOnlyList<MatchCandidate> matches)
    {
        var output = OpenCvActionSupport.ToBgrClone(source);
        if (ShowSearchRegion) Cv2.Rectangle(output, searchRoi, new Scalar(255, 170, 0), 2, LineTypes.AntiAlias);
        foreach (var match in matches)
        {
            if (ShowResultContour) Cv2.Rectangle(output, match.Bounds, new Scalar(0, 255, 0), 3, LineTypes.AntiAlias);
            Cv2.DrawMarker(output, new Point((int)match.Center.X, (int)match.Center.Y), new Scalar(0, 255, 255), MarkerTypes.Cross, 24, 2);
            Cv2.PutText(output, $"{match.Score:0.000}  {match.Angle:0.#}deg  x{match.Scale:0.00}",
                new Point(match.Bounds.X, Math.Max(22, match.Bounds.Y - 8)), HersheyFonts.HersheySimplex, .58, new Scalar(0, 255, 0), 2, LineTypes.AntiAlias);
        }
        return output;
    }

    private void PublishLearningPreview(IWorkflowResourceActionContext vision, Mat source, Rect learnRoi, Rect searchRoi)
    {
        var preview = OpenCvActionSupport.ToBgrClone(source);
        Cv2.Rectangle(preview, searchRoi, new Scalar(255, 170, 0), 2, LineTypes.AntiAlias);
        Cv2.Rectangle(preview, learnRoi, new Scalar(0, 255, 255), 3, LineTypes.AntiAlias);
        Cv2.PutText(preview, "LEARNING ROI", new Point(learnRoi.X, Math.Max(24, learnRoi.Y - 8)), HersheyFonts.HersheySimplex, .65, new Scalar(0, 255, 255), 2);
        OutputImage = vision.StoreResource(preview, OpenCvActionSupport.Metadata(preview, "SDK template learning preview"), true);
    }

    private static Mat RenderTemplatePreview(Mat template, Mat mask, LearnedTemplateModel model)
    {
        var preview = OpenCvActionSupport.ToBgrClone(template);
        using var prepared = string.Equals(model.ModelType, "Shape", StringComparison.OrdinalIgnoreCase)
            ? new InteractiveTemplateMatchSdkAction().Prepare(template, model)
            : mask.Clone();
        var contours = Cv2.FindContoursAsArray(prepared, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        Cv2.DrawContours(preview, contours, -1, new Scalar(0, 255, 0), 1, LineTypes.AntiAlias);
        using var excluded = new Mat(); Cv2.BitwiseNot(mask, excluded); preview.SetTo(new Scalar(45, 45, 45), excluded);
        return preview;
    }

    private static Mat? DecodeMask(string value, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var parts = value.Split(':', 2); if (parts.Length != 2 || parts[0] != $"{width}x{height}") return null;
        var bytes = new byte[checked(width * height)]; var index = 0;
        foreach (var token in parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = token.Split('*', 2); if (pair.Length != 2 || !byte.TryParse(pair[0], out var pixel) || !int.TryParse(pair[1], out var count)) return null;
            if (count < 0 || index + count > bytes.Length) return null;
            Array.Fill(bytes, pixel, index, count); index += count;
        }
        if (index != bytes.Length) return null;
        var mat = new Mat(height, width, MatType.CV_8UC1); System.Runtime.InteropServices.Marshal.Copy(bytes, 0, mat.Data, bytes.Length); return mat;
    }

    private string ResolveModelPath()
    {
        var requested = Environment.ExpandEnvironmentVariables(TemplateFilePath?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(requested)) throw new InvalidOperationException("Model file is required before learning or matching.");
        var path = Path.GetFullPath(requested); return string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase) ? path : path + ".json";
    }

    private static Rect NormalizeRoi(Size size, int x, int y, int width, int height, bool allowFull)
    {
        if (allowFull && (width <= 0 || height <= 0)) return new Rect(0, 0, size.Width, size.Height);
        var nx = Math.Clamp(x, 0, size.Width - 2); var ny = Math.Clamp(y, 0, size.Height - 2);
        return new Rect(nx, ny, Math.Clamp(width, 2, size.Width - nx), Math.Clamp(height, 2, size.Height - ny));
    }

    private static Mat RotateScale(Mat source, double angle, double scale, InterpolationFlags interpolation)
    {
        var radians = angle * Math.PI / 180; var cos = Math.Abs(Math.Cos(radians) * scale); var sin = Math.Abs(Math.Sin(radians) * scale);
        var width = Math.Max(2, (int)Math.Ceiling(source.Width * cos + source.Height * sin));
        var height = Math.Max(2, (int)Math.Ceiling(source.Width * sin + source.Height * cos));
        using var matrix = Cv2.GetRotationMatrix2D(new Point2f(source.Width / 2f, source.Height / 2f), angle, scale);
        matrix.Set(0, 2, matrix.At<double>(0, 2) + width / 2.0 - source.Width / 2.0);
        matrix.Set(1, 2, matrix.At<double>(1, 2) + height / 2.0 - source.Height / 2.0);
        var output = new Mat(); Cv2.WarpAffine(source, output, matrix, new Size(width, height), interpolation, BorderTypes.Constant, Scalar.All(0)); return output;
    }

    private static IEnumerable<double> Range(double min, double max, double step)
    {
        if (Math.Abs(max - min) < 0.0001) { yield return min; yield break; }
        for (var value = min; value <= max + step * .25; value += Math.Max(.0001, step)) yield return Math.Min(value, max);
    }
    private static double Overlap(Rect a, Rect b) { var intersection = a & b; if (intersection.Width <= 0 || intersection.Height <= 0) return 0; return intersection.Width * intersection.Height / (double)Math.Min(a.Width * a.Height, b.Width * b.Height); }
    private static int NormalizeBlur(int value) => value < 3 ? 0 : value % 2 == 0 ? value + 1 : value;
    private static string NormalizeModelType(string value) => string.Equals(value, "Gray", StringComparison.OrdinalIgnoreCase) ? "Gray" : "Shape";

    private sealed class LearnedTemplateModel
    {
        public int Version { get; set; }
        public string ModelType { get; set; } = "Shape";
        public int Width { get; set; }
        public int Height { get; set; }
        public int EdgeThresholdLow { get; set; }
        public int EdgeThresholdHigh { get; set; }
        public int BlurSize { get; set; }
        public double MinAngle { get; set; }
        public double MaxAngle { get; set; }
        public double AngleStep { get; set; }
        public double MinScale { get; set; }
        public double MaxScale { get; set; }
        public double ScaleStep { get; set; }
        public string TemplateFile { get; set; } = string.Empty;
        public string MaskFile { get; set; } = string.Empty;
        public DateTimeOffset LearnedUtc { get; set; }
    }
    private sealed record MatchCandidate(double Score, Rect Bounds, double Angle, double Scale)
    {
        public Point2d Center => new(Bounds.X + Bounds.Width / 2d, Bounds.Y + Bounds.Height / 2d);
    }
}
