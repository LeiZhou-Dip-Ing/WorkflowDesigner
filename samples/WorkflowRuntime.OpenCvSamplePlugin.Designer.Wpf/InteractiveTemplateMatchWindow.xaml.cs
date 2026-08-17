using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace WorkflowRuntime.OpenCvSamplePlugin.Designer.Wpf;

public partial class InteractiveTemplateMatchWindow : Window
{
    private Point? _dragStart;
    private bool _painting;

    public InteractiveTemplateMatchWindow(InteractiveTemplateMatchViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        viewModel.PropertyChanged += ViewModelOnPropertyChanged;
        Loaded += (_, _) => UpdateOverlays();
        PreviewSurface.SizeChanged += (_, _) => UpdateOverlays();
    }

    private InteractiveTemplateMatchViewModel ViewModel => (InteractiveTemplateMatchViewModel)DataContext;

    private async void StartLearning_OnClick(object sender, RoutedEventArgs e) { await ViewModel.StartLearningAsync(); UpdateOverlays(); }
    private async void LearnModel_OnClick(object sender, RoutedEventArgs e) { await ViewModel.LearnModelAsync(); UpdateOverlays(); }
    private async void ExecuteMatch_OnClick(object sender, RoutedEventArgs e) { await ViewModel.ExecuteMatchAsync(); UpdateOverlays(); }
    private void EditMask_OnClick(object sender, RoutedEventArgs e) { ViewModel.EditMask(); UpdateOverlays(); }
    private void PointerTool_OnClick(object sender, RoutedEventArgs e) { ViewModel.Tool = TemplateEditorTool.Pointer; UpdateOverlays(); }
    private void RoiTool_OnClick(object sender, RoutedEventArgs e) { ViewModel.Tool = TemplateEditorTool.LearnRoi; UpdateOverlays(); }
    private void AddMaskTool_OnClick(object sender, RoutedEventArgs e) { ViewModel.EditMask(); ViewModel.Tool = TemplateEditorTool.AddMask; UpdateOverlays(); }
    private void EraseMaskTool_OnClick(object sender, RoutedEventArgs e) { ViewModel.EditMask(); ViewModel.Tool = TemplateEditorTool.EraseMask; UpdateOverlays(); }
    private void ResetMaskAll_OnClick(object sender, RoutedEventArgs e) { ViewModel.ResetMask(true); UpdateOverlays(); }
    private void ResetMaskEmpty_OnClick(object sender, RoutedEventArgs e) { ViewModel.ResetMask(false); UpdateOverlays(); }
    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();

    private void BrushShape_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ViewModel.BrushShape = BrushShapeBox.SelectedIndex == 1 ? TemplateBrushShape.Rectangle : TemplateBrushShape.Circle;
    }
    private void BrushSize_OnChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DataContext is InteractiveTemplateMatchViewModel vm) vm.BrushSize = (int)Math.Round(e.NewValue);
    }

    private void Preview_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (PreviewImageControl.Source is not BitmapSource) return;
        var position = ClampToImage(e.GetPosition(PreviewSurface));
        if (ViewModel.IsRoiTool)
        {
            _dragStart = position; PreviewSurface.CaptureMouse(); UpdateLearningRectangle(position, position);
        }
        else if (ViewModel.IsMaskTool)
        {
            _painting = true; PreviewSurface.CaptureMouse(); Paint(position);
        }
        e.Handled = true;
    }

    private void Preview_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (PreviewImageControl.Source is not BitmapSource) return;
        var position = ClampToImage(e.GetPosition(PreviewSurface)); UpdateBrushCursor(position);
        if (_dragStart.HasValue && e.LeftButton == MouseButtonState.Pressed) UpdateLearningRectangle(_dragStart.Value, position);
        else if (_painting && e.LeftButton == MouseButtonState.Pressed) Paint(position);
    }

    private void Preview_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var position = ClampToImage(e.GetPosition(PreviewSurface));
        if (_dragStart.HasValue && PreviewImageControl.Source is BitmapSource bitmap)
        {
            var start = _dragStart.Value; _dragStart = null; PreviewSurface.ReleaseMouseCapture();
            var a = DisplayToImage(start, bitmap); var b = DisplayToImage(position, bitmap);
            var x = Math.Clamp((int)Math.Floor(Math.Min(a.X, b.X)), 0, bitmap.PixelWidth - 2);
            var y = Math.Clamp((int)Math.Floor(Math.Min(a.Y, b.Y)), 0, bitmap.PixelHeight - 2);
            var width = Math.Clamp((int)Math.Ceiling(Math.Abs(a.X - b.X)), 2, bitmap.PixelWidth - x);
            var height = Math.Clamp((int)Math.Ceiling(Math.Abs(a.Y - b.Y)), 2, bitmap.PixelHeight - y);
            ViewModel.SetLearningRoi(x, y, width, height); UpdateOverlays();
        }
        if (_painting) { _painting = false; PreviewSurface.ReleaseMouseCapture(); }
        e.Handled = true;
    }

    private void Paint(Point displayPoint)
    {
        if (PreviewImageControl.Source is not BitmapSource bitmap) return;
        var image = DisplayToImage(displayPoint, bitmap); ViewModel.PaintMask((int)Math.Round(image.X), (int)Math.Round(image.Y));
    }

    private Point ClampToImage(Point point)
    {
        if (PreviewImageControl.Source is not BitmapSource bitmap) return point;
        var rect = DisplayedImageRect(bitmap);
        return new Point(Math.Clamp(point.X, rect.Left, rect.Right), Math.Clamp(point.Y, rect.Top, rect.Bottom));
    }

    private Point DisplayToImage(Point point, BitmapSource bitmap)
    {
        var rect = DisplayedImageRect(bitmap);
        return new Point((point.X - rect.X) * bitmap.PixelWidth / rect.Width, (point.Y - rect.Y) * bitmap.PixelHeight / rect.Height);
    }

    private Rect ImageToDisplay(int x, int y, int width, int height, BitmapSource bitmap)
    {
        var rect = DisplayedImageRect(bitmap); var sx = rect.Width / bitmap.PixelWidth; var sy = rect.Height / bitmap.PixelHeight;
        return new Rect(rect.X + x * sx, rect.Y + y * sy, width * sx, height * sy);
    }

    private Rect DisplayedImageRect(BitmapSource bitmap)
    {
        var scale = Math.Min(PreviewSurface.ActualWidth / bitmap.PixelWidth, PreviewSurface.ActualHeight / bitmap.PixelHeight);
        var width = bitmap.PixelWidth * scale; var height = bitmap.PixelHeight * scale;
        return new Rect((PreviewSurface.ActualWidth - width) / 2, (PreviewSurface.ActualHeight - height) / 2, width, height);
    }

    private void UpdateLearningRectangle(Point start, Point end)
    {
        LearningRectangle.Visibility = Visibility.Visible;
        Canvas.SetLeft(LearningRectangle, Math.Min(start.X, end.X)); Canvas.SetTop(LearningRectangle, Math.Min(start.Y, end.Y));
        LearningRectangle.Width = Math.Max(1, Math.Abs(end.X - start.X)); LearningRectangle.Height = Math.Max(1, Math.Abs(end.Y - start.Y));
    }

    private void UpdateOverlays()
    {
        if (PreviewImageControl.Source is not BitmapSource bitmap) { LearningRectangle.Visibility = SearchRectangle.Visibility = Visibility.Collapsed; return; }
        var learn = ViewModel.GetLearningRoi(); SetRectangle(LearningRectangle, ImageToDisplay(learn.X, learn.Y, learn.Width, learn.Height, bitmap));
        var search = ViewModel.GetSearchRoi();
        if (search.HasValue) SetRectangle(SearchRectangle, ImageToDisplay(search.Value.X, search.Value.Y, search.Value.Width, search.Value.Height, bitmap));
        else SetRectangle(SearchRectangle, DisplayedImageRect(bitmap));
        BrushCursor.Visibility = ViewModel.IsMaskTool ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void SetRectangle(FrameworkElement element, Rect rect)
    {
        element.Visibility = Visibility.Visible; Canvas.SetLeft(element, rect.X); Canvas.SetTop(element, rect.Y); element.Width = rect.Width; element.Height = rect.Height;
    }

    private void UpdateBrushCursor(Point point)
    {
        if (!ViewModel.IsMaskTool || PreviewImageControl.Source is not BitmapSource bitmap) { BrushCursor.Visibility = Visibility.Collapsed; return; }
        var rect = DisplayedImageRect(bitmap); var diameter = ViewModel.BrushSize * rect.Width / bitmap.PixelWidth;
        BrushCursor.Visibility = Visibility.Visible; BrushCursor.Width = diameter; BrushCursor.Height = diameter;
        Canvas.SetLeft(BrushCursor, point.X - diameter / 2); Canvas.SetTop(BrushCursor, point.Y - diameter / 2);
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InteractiveTemplateMatchViewModel.DisplayImage) or nameof(InteractiveTemplateMatchViewModel.Tool)) Dispatcher.BeginInvoke(UpdateOverlays);
    }
}
