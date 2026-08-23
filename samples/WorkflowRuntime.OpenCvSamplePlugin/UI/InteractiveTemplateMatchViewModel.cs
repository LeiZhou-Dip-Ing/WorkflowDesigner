using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using WorkflowDesigner.WpfSdk;

namespace WorkflowRuntime.OpenCvSamplePlugin.UI;

public enum TemplateEditorTool { Pointer, LearnRoi, AddMask, EraseMask }
public enum TemplateBrushShape { Circle, Rectangle }

internal sealed class InteractiveTemplateMatchViewModel : INotifyPropertyChanged
{
    private readonly IWorkflowDesignerActionContext _context;
    private readonly IWorkflowDesignerResourcePreviewCapability? _preview;
    private bool _running;
    private string _status = "选择学习图像和搜索图像，然后点击“开始学习”。";
    private ImageSource? _displayImage;
    private BitmapSource? _learningImage;
    private byte[]? _mask;
    private int _maskWidth;
    private int _maskHeight;
    private TemplateEditorTool _tool;
    private TemplateBrushShape _brushShape;
    private int _brushSize = 18;

    public InteractiveTemplateMatchViewModel(IWorkflowDesignerActionContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _preview = context.GetCapability<IWorkflowDesignerResourcePreviewCapability>();
        _displayImage = OpenCvPreviewDecoder.Decode(_preview?.Current);
        if (_preview != null)
        {
            _preview.PropertyChanged += PreviewOnPropertyChanged;
        }
        StartLearningCommand = new AsyncRelayCommand(StartLearningAsync, () => CanRun);
        LearnModelCommand = new AsyncRelayCommand(LearnModelAsync, () => CanRun);
        ExecuteMatchCommand = new AsyncRelayCommand(ExecuteMatchAsync, () => CanRun);
        EditMaskCommand = new RelayCommand(_ => EditMask());
        SelectToolCommand = new RelayCommand(parameter =>
        {
            if (parameter is TemplateEditorTool tool) Tool = tool;
        });
        ResetMaskAllCommand = new RelayCommand(_ => ResetMask(includeAll: true));
        ResetMaskEmptyCommand = new RelayCommand(_ => ResetMask(includeAll: false));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public IWorkflowDesignerActionContext Context => _context;
    public ImageSource? DisplayImage { get => _displayImage; private set { if (ReferenceEquals(_displayImage, value)) return; _displayImage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasImage)); } }
    public bool HasImage => DisplayImage != null;
    public bool IsRunning { get => _running; private set { if (_running == value) return; _running = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanRun)); RaiseCommandStates(); } }
    public bool CanRun => !IsRunning && _preview != null;
    public string PreviewInfo => _preview?.Current?.Description ?? "No preview yet";
    public string Status { get => _status; private set { if (_status == value) return; _status = value; OnPropertyChanged(); } }
    public bool HasLearnedMask => _mask is { Length: > 0 };

    public TemplateEditorTool Tool
    {
        get => _tool;
        set { if (_tool == value) return; _tool = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsRoiTool)); OnPropertyChanged(nameof(IsMaskTool)); }
    }
    public bool IsRoiTool => Tool == TemplateEditorTool.LearnRoi;
    public bool IsMaskTool => Tool is TemplateEditorTool.AddMask or TemplateEditorTool.EraseMask;
    public TemplateBrushShape BrushShape { get => _brushShape; set { if (_brushShape == value) return; _brushShape = value; OnPropertyChanged(); } }
    public int BrushSize { get => _brushSize; set { var next = Math.Clamp(value, 2, 100); if (_brushSize == next) return; _brushSize = next; OnPropertyChanged(); } }

    public ICommand StartLearningCommand { get; }
    public ICommand LearnModelCommand { get; }
    public ICommand ExecuteMatchCommand { get; }
    public ICommand EditMaskCommand { get; }
    public ICommand SelectToolCommand { get; }
    public ICommand ResetMaskAllCommand { get; }
    public ICommand ResetMaskEmptyCommand { get; }

    public IWorkflowPropertyEditorModel? LearningImageProperty => Find("TemplateSourceImage");
    public IWorkflowPropertyEditorModel? SearchImageProperty => Find("SearchImage");
    public IWorkflowPropertyEditorModel? RoiXProperty => Find("RoiX");
    public IWorkflowPropertyEditorModel? RoiYProperty => Find("RoiY");
    public IWorkflowPropertyEditorModel? RoiWidthProperty => Find("RoiWidth");
    public IWorkflowPropertyEditorModel? RoiHeightProperty => Find("RoiHeight");
    public IWorkflowPropertyEditorModel? SearchRoiXProperty => Find("SearchRoiX");
    public IWorkflowPropertyEditorModel? SearchRoiYProperty => Find("SearchRoiY");
    public IWorkflowPropertyEditorModel? SearchRoiWidthProperty => Find("SearchRoiWidth");
    public IWorkflowPropertyEditorModel? SearchRoiHeightProperty => Find("SearchRoiHeight");
    public IWorkflowPropertyEditorModel? ModelFileProperty => Find("TemplateFilePath");
    public IWorkflowPropertyEditorModel? ModelTypeProperty => Find("ModelType");
    public IWorkflowPropertyEditorModel? EdgeLowProperty => Find("EdgeThresholdLow");
    public IWorkflowPropertyEditorModel? EdgeHighProperty => Find("EdgeThresholdHigh");
    public IWorkflowPropertyEditorModel? BlurSizeProperty => Find("BlurSize");
    public IWorkflowPropertyEditorModel? MinAngleProperty => Find("MinAngle");
    public IWorkflowPropertyEditorModel? MaxAngleProperty => Find("MaxAngle");
    public IWorkflowPropertyEditorModel? AngleStepProperty => Find("AngleStep");
    public IWorkflowPropertyEditorModel? MinScaleProperty => Find("MinScale");
    public IWorkflowPropertyEditorModel? MaxScaleProperty => Find("MaxScale");
    public IWorkflowPropertyEditorModel? ScaleStepProperty => Find("ScaleStep");
    public IWorkflowPropertyEditorModel? MinimumScoreProperty => Find("MinimumScore");
    public IWorkflowPropertyEditorModel? MaximumMatchesProperty => Find("MaximumMatches");
    public IWorkflowPropertyEditorModel? MaximumOverlapProperty => Find("MaximumOverlap");
    public IWorkflowPropertyEditorModel? ShowSearchRegionProperty => Find("ShowSearchRegion");
    public IWorkflowPropertyEditorModel? ShowResultContourProperty => Find("ShowResultContour");
    public IWorkflowPropertyEditorModel? PublishPreviewProperty => Find("PublishPreview");
    public IWorkflowPropertyEditorModel? OutputImageProperty => Find("OutputImage");
    public IWorkflowPropertyEditorModel? TemplateImageProperty => Find("TemplateImage");
    public IWorkflowPropertyEditorModel? SavedPathProperty => Find("SavedPath");
    public IWorkflowPropertyEditorModel? MatchedProperty => Find("Matched");
    public IWorkflowPropertyEditorModel? MatchCountProperty => Find("MatchCount");
    public IWorkflowPropertyEditorModel? ScoreProperty => Find("Score");
    public IWorkflowPropertyEditorModel? MatchXProperty => Find("MatchX");
    public IWorkflowPropertyEditorModel? MatchYProperty => Find("MatchY");
    public IWorkflowPropertyEditorModel? MatchAngleProperty => Find("MatchAngle");
    public IWorkflowPropertyEditorModel? MatchScaleProperty => Find("MatchScale");

    public async Task StartLearningAsync()
    {
        await RunAsync("正在加载学习图像…", "sample.opencv.template.preview-learning", "PreviewLearning");
        if (OpenCvPreviewDecoder.Decode(_preview?.Current) is BitmapSource bitmap)
        {
            _learningImage = bitmap;
            DisplayImage = bitmap;
            EnsureMaskForCurrentRoi(reset: false);
            Tool = TemplateEditorTool.LearnRoi;
            Status = "学习模式：拖动黄色学习框；随后可用添加/橡皮擦修整有效区域。";
            OnPropertyChanged(nameof(HasLearnedMask));
        }
    }

    public async Task LearnModelAsync()
    {
        if (_learningImage == null) { Status = "请先点击“开始学习”加载学习图像。"; return; }
        EnsureMaskForCurrentRoi(reset: false);
        SetText("TemplateMaskData", EncodeMask());
        await RunAsync("正在提取特征、保存模型并验证匹配…", "sample.opencv.template.learn", "LearnAndMatch");
        DisplayImage = OpenCvPreviewDecoder.Decode(_preview?.Current);
        Tool = TemplateEditorTool.Pointer;
        Status = "学习完成：模型、模板图和掩膜已保存；当前 Action 已切换为 MatchOnly。";
    }

    public async Task ExecuteMatchAsync()
    {
        await RunAsync("正在使用已学习模型执行匹配…", "sample.opencv.template.match", "MatchOnly");
        DisplayImage = OpenCvPreviewDecoder.Decode(_preview?.Current);
        Tool = TemplateEditorTool.Pointer;
        Status = "执行完成。结果轮廓、中心、分数、角度和缩放已输出。";
    }

    public void EditMask()
    {
        if (_learningImage == null) { Status = "请先开始学习。"; return; }
        EnsureMaskForCurrentRoi(reset: false);
        DisplayImage = _learningImage;
        Tool = TemplateEditorTool.EraseMask;
        Status = "掩膜编辑：蓝色为参与学习的区域；橡皮擦可去除背景和噪点。";
    }

    public void SetLearningRoi(int x, int y, int width, int height)
    {
        SetNumber("RoiX", x); SetNumber("RoiY", y); SetNumber("RoiWidth", width); SetNumber("RoiHeight", height);
        EnsureMaskForCurrentRoi(reset: true);
        Status = $"学习区域：X={x}, Y={y}, Width={width}, Height={height}。可继续编辑掩膜或完成学习。";
        OnPropertyChanged(nameof(HasLearnedMask));
    }

    public (int X, int Y, int Width, int Height) GetLearningRoi()
        => (GetInt("RoiX", 0), GetInt("RoiY", 0), Math.Max(2, GetInt("RoiWidth", 2)), Math.Max(2, GetInt("RoiHeight", 2)));

    public (int X, int Y, int Width, int Height)? GetSearchRoi()
    {
        var width = GetInt("SearchRoiWidth", 0); var height = GetInt("SearchRoiHeight", 0);
        return width <= 0 || height <= 0 ? null : (GetInt("SearchRoiX", 0), GetInt("SearchRoiY", 0), width, height);
    }

    public void ResetMask(bool includeAll)
    {
        EnsureMaskForCurrentRoi(reset: true, included: includeAll);
        Status = includeAll ? "掩膜已重置为整个学习区域。" : "掩膜已清空，请用“添加区域”画出有效特征。";
        OnPropertyChanged(nameof(MaskOverlay));
    }

    public void PaintMask(int imageX, int imageY)
    {
        if (_mask == null || !IsMaskTool) return;
        var roi = GetLearningRoi(); var cx = imageX - roi.X; var cy = imageY - roi.Y;
        if (cx < 0 || cy < 0 || cx >= roi.Width || cy >= roi.Height) return;
        var radius = Math.Max(1, BrushSize / 2); var value = Tool == TemplateEditorTool.AddMask ? (byte)255 : (byte)0;
        for (var y = Math.Max(0, cy - radius); y <= Math.Min(_maskHeight - 1, cy + radius); y++)
        for (var x = Math.Max(0, cx - radius); x <= Math.Min(_maskWidth - 1, cx + radius); x++)
        {
            if (BrushShape == TemplateBrushShape.Circle && (x - cx) * (x - cx) + (y - cy) * (y - cy) > radius * radius) continue;
            _mask[y * _maskWidth + x] = value;
        }
        OnPropertyChanged(nameof(MaskOverlay));
    }

    public ImageSource? MaskOverlay
    {
        get
        {
            if (_learningImage == null || _mask == null) return null;
            var pixels = new byte[_learningImage.PixelWidth * _learningImage.PixelHeight * 4]; var roi = GetLearningRoi();
            for (var y = 0; y < _maskHeight; y++)
            for (var x = 0; x < _maskWidth; x++)
            {
                var ix = roi.X + x; var iy = roi.Y + y;
                if (ix < 0 || iy < 0 || ix >= _learningImage.PixelWidth || iy >= _learningImage.PixelHeight) continue;
                var p = (iy * _learningImage.PixelWidth + ix) * 4;
                if (_mask[y * _maskWidth + x] > 0) { pixels[p] = 255; pixels[p + 1] = 125; pixels[p + 2] = 0; pixels[p + 3] = 48; }
                else { pixels[p] = 30; pixels[p + 1] = 30; pixels[p + 2] = 220; pixels[p + 3] = 105; }
            }
            var bitmap = BitmapSource.Create(_learningImage.PixelWidth, _learningImage.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, _learningImage.PixelWidth * 4);
            bitmap.Freeze(); return bitmap;
        }
    }

    private async Task RunAsync(string runningStatus, string? commandId = null, string? operation = null)
    {
        if (!CanRun) return;
        IsRunning = true; Status = runningStatus;
        try
        {
            if (commandId == null)
            {
                if (_preview != null)
                {
                    await _preview.RefreshAsync().ConfigureAwait(true);
                }
            }
            else
            {
                var result = await _context.ExecuteCommandAsync(new WorkflowDesignerCommandRequest(
                    commandId,
                    new Dictionary<string, object?> { ["Operation"] = operation })).ConfigureAwait(true);
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException(result.Message);
                }

                if (_preview != null)
                {
                    await _preview.RefreshAsync().ConfigureAwait(true);
                }
            }

            OnPropertyChanged(nameof(PreviewInfo));
        }
        catch (Exception exception) { Status = $"操作失败：{exception.Message}"; }
        finally { IsRunning = false; }
    }

    private void EnsureMaskForCurrentRoi(bool reset, bool included = true)
    {
        var roi = GetLearningRoi();
        if (!reset && _mask != null && _maskWidth == roi.Width && _maskHeight == roi.Height) return;
        _maskWidth = roi.Width; _maskHeight = roi.Height; _mask = new byte[checked(_maskWidth * _maskHeight)];
        if (included) Array.Fill(_mask, (byte)255);
        OnPropertyChanged(nameof(MaskOverlay));
    }

    private string EncodeMask()
    {
        if (_mask == null || _mask.Length == 0) return string.Empty;
        var runs = new List<string>(); var value = _mask[0]; var count = 1;
        for (var i = 1; i < _mask.Length; i++)
        {
            if (_mask[i] == value) { count++; continue; }
            runs.Add($"{value}*{count}"); value = _mask[i]; count = 1;
        }
        runs.Add($"{value}*{count}"); return $"{_maskWidth}x{_maskHeight}:{string.Join(',', runs)}";
    }

    private IWorkflowPropertyEditorModel? Find(string name) => _context.Properties.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
    private void SetText(string name, string value) { var property = Find(name); if (property == null) return; if (property.EnumValues.Cast<object>().Any()) property.SelectedValue = value; else property.ValueText = value; }
    private void SetNumber(string name, int value) { var property = Find(name); if (property != null) property.ValueText = value.ToString(CultureInfo.InvariantCulture); }
    private int GetInt(string name, int fallback) => int.TryParse(Find(name)?.ValueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private void PreviewOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName)
            || e.PropertyName is nameof(IWorkflowDesignerResourcePreviewCapability.Current)
                or nameof(IWorkflowDesignerResourcePreviewCapability.HasContent))
        {
            OnPropertyChanged(nameof(CanRun));
            OnPropertyChanged(nameof(PreviewInfo));
            RaiseCommandStates();
        }
    }
    private void RaiseCommandStates()
    {
        (StartLearningCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (LearnModelCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ExecuteMatchCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
