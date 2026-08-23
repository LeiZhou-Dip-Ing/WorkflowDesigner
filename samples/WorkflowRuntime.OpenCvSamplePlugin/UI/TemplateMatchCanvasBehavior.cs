using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace WorkflowRuntime.OpenCvSamplePlugin.UI;

internal static class TemplateMatchCanvasBehavior
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(TemplateMatchCanvasBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State",
        typeof(InteractionState),
        typeof(TemplateMatchCanvasBehavior));

    public static void SetIsEnabled(DependencyObject element, bool value)
        => element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element)
        => (bool)element.GetValue(IsEnabledProperty);

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not FrameworkElement surface) return;
        (surface.GetValue(StateProperty) as InteractionState)?.Dispose();
        surface.ClearValue(StateProperty);
        if (args.NewValue is true)
        {
            surface.SetValue(StateProperty, new InteractionState(surface));
        }
    }

    private sealed class InteractionState : IDisposable
    {
        private readonly FrameworkElement _surface;
        private InteractiveTemplateMatchViewModel? _viewModel;
        private Point? _dragStart;
        private bool _painting;

        public InteractionState(FrameworkElement surface)
        {
            _surface = surface;
            _surface.Loaded += OnLoaded;
            _surface.Unloaded += OnUnloaded;
            _surface.SizeChanged += OnSizeChanged;
            _surface.DataContextChanged += OnDataContextChanged;
            _surface.MouseLeftButtonDown += OnMouseLeftButtonDown;
            _surface.MouseMove += OnMouseMove;
            _surface.MouseLeftButtonUp += OnMouseLeftButtonUp;
            AttachViewModel(surface.DataContext as InteractiveTemplateMatchViewModel);
        }

        private BitmapSource? Bitmap => Find<Image>("PreviewImageControl")?.Source as BitmapSource;

        private void OnLoaded(object sender, RoutedEventArgs e) => UpdateOverlays();

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _dragStart = null;
            _painting = false;
            _surface.ReleaseMouseCapture();
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateOverlays();

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
            => AttachViewModel(e.NewValue as InteractiveTemplateMatchViewModel);

        private void AttachViewModel(InteractiveTemplateMatchViewModel? viewModel)
        {
            if (_viewModel != null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = viewModel;
            if (_viewModel != null) _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            UpdateOverlays();
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.PropertyName)
                || e.PropertyName is nameof(InteractiveTemplateMatchViewModel.DisplayImage)
                    or nameof(InteractiveTemplateMatchViewModel.Tool)
                    or nameof(InteractiveTemplateMatchViewModel.BrushSize))
            {
                _surface.Dispatcher.BeginInvoke(UpdateOverlays);
            }
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel == null || Bitmap == null) return;
            var position = ClampToImage(e.GetPosition(_surface));
            if (_viewModel.IsRoiTool)
            {
                _dragStart = position;
                _surface.CaptureMouse();
                UpdateLearningRectangle(position, position);
            }
            else if (_viewModel.IsMaskTool)
            {
                _painting = true;
                _surface.CaptureMouse();
                Paint(position);
            }

            e.Handled = true;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_viewModel == null || Bitmap == null) return;
            var position = ClampToImage(e.GetPosition(_surface));
            UpdateBrushCursor(position);
            if (_dragStart.HasValue && e.LeftButton == MouseButtonState.Pressed)
            {
                UpdateLearningRectangle(_dragStart.Value, position);
            }
            else if (_painting && e.LeftButton == MouseButtonState.Pressed)
            {
                Paint(position);
            }
        }

        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel == null || Bitmap is not { } bitmap) return;
            var position = ClampToImage(e.GetPosition(_surface));
            if (_dragStart.HasValue)
            {
                var start = _dragStart.Value;
                _dragStart = null;
                _surface.ReleaseMouseCapture();
                var a = DisplayToImage(start, bitmap);
                var b = DisplayToImage(position, bitmap);
                var x = Math.Clamp((int)Math.Floor(Math.Min(a.X, b.X)), 0, bitmap.PixelWidth - 2);
                var y = Math.Clamp((int)Math.Floor(Math.Min(a.Y, b.Y)), 0, bitmap.PixelHeight - 2);
                var width = Math.Clamp((int)Math.Ceiling(Math.Abs(a.X - b.X)), 2, bitmap.PixelWidth - x);
                var height = Math.Clamp((int)Math.Ceiling(Math.Abs(a.Y - b.Y)), 2, bitmap.PixelHeight - y);
                _viewModel.SetLearningRoi(x, y, width, height);
                UpdateOverlays();
            }

            if (_painting)
            {
                _painting = false;
                _surface.ReleaseMouseCapture();
            }

            e.Handled = true;
        }

        private void Paint(Point displayPoint)
        {
            if (_viewModel == null || Bitmap is not { } bitmap) return;
            var imagePoint = DisplayToImage(displayPoint, bitmap);
            _viewModel.PaintMask((int)Math.Round(imagePoint.X), (int)Math.Round(imagePoint.Y));
        }

        private Point ClampToImage(Point point)
        {
            if (Bitmap is not { } bitmap) return point;
            var rect = DisplayedImageRect(bitmap);
            return new Point(
                Math.Clamp(point.X, rect.Left, rect.Right),
                Math.Clamp(point.Y, rect.Top, rect.Bottom));
        }

        private Point DisplayToImage(Point point, BitmapSource bitmap)
        {
            var rect = DisplayedImageRect(bitmap);
            return new Point(
                (point.X - rect.X) * bitmap.PixelWidth / rect.Width,
                (point.Y - rect.Y) * bitmap.PixelHeight / rect.Height);
        }

        private Rect ImageToDisplay(int x, int y, int width, int height, BitmapSource bitmap)
        {
            var rect = DisplayedImageRect(bitmap);
            var scaleX = rect.Width / bitmap.PixelWidth;
            var scaleY = rect.Height / bitmap.PixelHeight;
            return new Rect(rect.X + x * scaleX, rect.Y + y * scaleY, width * scaleX, height * scaleY);
        }

        private Rect DisplayedImageRect(BitmapSource bitmap)
        {
            var scale = Math.Min(_surface.ActualWidth / bitmap.PixelWidth, _surface.ActualHeight / bitmap.PixelHeight);
            var width = bitmap.PixelWidth * scale;
            var height = bitmap.PixelHeight * scale;
            return new Rect((_surface.ActualWidth - width) / 2, (_surface.ActualHeight - height) / 2, width, height);
        }

        private void UpdateLearningRectangle(Point start, Point end)
        {
            if (Find<Rectangle>("LearningRectangle") is not { } rectangle) return;
            rectangle.Visibility = Visibility.Visible;
            Canvas.SetLeft(rectangle, Math.Min(start.X, end.X));
            Canvas.SetTop(rectangle, Math.Min(start.Y, end.Y));
            rectangle.Width = Math.Max(1, Math.Abs(end.X - start.X));
            rectangle.Height = Math.Max(1, Math.Abs(end.Y - start.Y));
        }

        private void UpdateOverlays()
        {
            var learningRectangle = Find<Rectangle>("LearningRectangle");
            var searchRectangle = Find<Rectangle>("SearchRectangle");
            var brushCursor = Find<Ellipse>("BrushCursor");
            if (_viewModel == null || Bitmap is not { } bitmap)
            {
                if (learningRectangle != null) learningRectangle.Visibility = Visibility.Collapsed;
                if (searchRectangle != null) searchRectangle.Visibility = Visibility.Collapsed;
                if (brushCursor != null) brushCursor.Visibility = Visibility.Collapsed;
                return;
            }

            var learning = _viewModel.GetLearningRoi();
            SetRectangle(learningRectangle, ImageToDisplay(learning.X, learning.Y, learning.Width, learning.Height, bitmap));
            var search = _viewModel.GetSearchRoi();
            SetRectangle(
                searchRectangle,
                search.HasValue
                    ? ImageToDisplay(search.Value.X, search.Value.Y, search.Value.Width, search.Value.Height, bitmap)
                    : DisplayedImageRect(bitmap));
            if (brushCursor != null)
            {
                brushCursor.Visibility = _viewModel.IsMaskTool ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private static void SetRectangle(FrameworkElement? element, Rect rect)
        {
            if (element == null) return;
            element.Visibility = Visibility.Visible;
            Canvas.SetLeft(element, rect.X);
            Canvas.SetTop(element, rect.Y);
            element.Width = rect.Width;
            element.Height = rect.Height;
        }

        private void UpdateBrushCursor(Point point)
        {
            if (_viewModel == null || !_viewModel.IsMaskTool || Bitmap is not { } bitmap
                || Find<Ellipse>("BrushCursor") is not { } cursor)
            {
                if (Find<Ellipse>("BrushCursor") is { } hidden) hidden.Visibility = Visibility.Collapsed;
                return;
            }

            var rect = DisplayedImageRect(bitmap);
            var diameter = _viewModel.BrushSize * rect.Width / bitmap.PixelWidth;
            cursor.Visibility = Visibility.Visible;
            cursor.Width = diameter;
            cursor.Height = diameter;
            Canvas.SetLeft(cursor, point.X - diameter / 2);
            Canvas.SetTop(cursor, point.Y - diameter / 2);
        }

        private T? Find<T>(string name) where T : FrameworkElement
            => _surface.FindName(name) as T;

        public void Dispose()
        {
            AttachViewModel(null);
            _surface.Loaded -= OnLoaded;
            _surface.Unloaded -= OnUnloaded;
            _surface.SizeChanged -= OnSizeChanged;
            _surface.DataContextChanged -= OnDataContextChanged;
            _surface.MouseLeftButtonDown -= OnMouseLeftButtonDown;
            _surface.MouseMove -= OnMouseMove;
            _surface.MouseLeftButtonUp -= OnMouseLeftButtonUp;
        }
    }
}
