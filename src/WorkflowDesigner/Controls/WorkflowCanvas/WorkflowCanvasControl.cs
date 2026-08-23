using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.ViewModels;
using WorkflowRuntime.Contracts;
using WorkflowRuntime.ActionSdk;
using WorkflowDesigner.WpfSdk;

namespace WorkflowCore.WpfDemo.Controls.WorkflowCanvas;

/// <summary>
/// Dify-style visual projection of the existing MethodLine collection.
/// The list editor and the runtime model remain the source of truth. Canvas links write
/// the same Action input expressions and output bindings that are edited by the property panel.
/// </summary>
public sealed class WorkflowCanvasControl : UserControl
{
    private const double NodeWidth = 292;
    private const double HeaderHeight = 48;
    private const double FlowRowHeight = 34;
    private const double PortRowHeight = 34;
    private const double NodeBottomPadding = 10;
    private const string XKey = "canvasX";
    private const string YKey = "canvasY";
    private const string DataConnectionsKey = "canvasDataConnections";
    private const double MinimumZoom = 0.20;
    private const double MaximumZoom = 2.50;
    private const double ZoomStep = 0.10;
    private const double WorldOriginX = 2400;
    private const double WorldOriginY = 1600;
    private const double VirtualCanvasWidth = 7200;
    private const double VirtualCanvasHeight = 4800;
    private const double MiniMapCanvasWidth = 236;
    private const double MiniMapCanvasHeight = 112;

    private static readonly Brush CanvasBackground = BrushFromRgb(23, 25, 27);
    private static readonly Brush NodeBackground = BrushFromRgb(35, 37, 40);
    private static readonly Brush NodeHeaderBackground = BrushFromRgb(42, 44, 48);
    private static readonly Brush SoftBorder = BrushFromRgb(76, 81, 87);
    private static readonly Brush PrimaryText = BrushFromRgb(239, 242, 245);
    private static readonly Brush SecondaryText = BrushFromRgb(166, 174, 183);
    private static readonly Brush FlowBrush = BrushFromRgb(183, 193, 202);
    private static readonly Brush DataBrush = BrushFromRgb(45, 156, 219);
    private static readonly Brush DisabledPortBrush = BrushFromRgb(91, 98, 105);
    private static readonly Brush SuccessBrush = BrushFromRgb(94, 201, 67);
    private static readonly Brush WarningBrush = BrushFromRgb(238, 153, 49);

    private readonly Canvas _surface = new()
    {
        MinWidth = VirtualCanvasWidth,
        MinHeight = VirtualCanvasHeight,
        ClipToBounds = false
    };

    private readonly Grid _root = new();
    private readonly ScrollViewer _scrollViewer;
    private readonly ScaleTransform _zoomTransform = new(1, 1);
    private readonly TextBlock _zoomText = new()
    {
        MinWidth = 48,
        Foreground = PrimaryText,
        FontSize = 11,
        TextAlignment = TextAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly Dictionary<Guid, NodeVisualInfo> _nodes = new();
    private readonly Dictionary<PortIdentity, CanvasPortDefinition> _ports = new();
    private readonly Canvas _miniMapCanvas = new()
    {
        Width = MiniMapCanvasWidth,
        Height = MiniMapCanvasHeight,
        ClipToBounds = true,
        Background = BrushFromRgb(24, 26, 29),
        Cursor = Cursors.Hand
    };
    private readonly Rectangle _miniMapViewport = new()
    {
        Fill = new SolidColorBrush(Color.FromArgb(28, 45, 156, 219)),
        Stroke = DataBrush,
        StrokeThickness = 1.2,
        IsHitTestVisible = false
    };
    private readonly Border _miniMapHost;
    private MethodEditorViewModel? _vm;
    private Point _dragStart;
    private Point _nodeStart;
    private Border? _dragNode;
    private MethodLine? _dragLine;
    private CanvasPortDefinition? _linkSource;
    private Path? _previewPath;
    private bool _isRebuilding;
    private bool _isPanning;
    private bool _initialViewPending;
    private double _zoom = 1.0;
    private Point _panStart;
    private double _panHorizontalOffset;
    private double _panVerticalOffset;
    private Rect _miniMapWorldBounds = Rect.Empty;
    private double _miniMapScale = 1.0;
    private Point _miniMapOffset;

    public WorkflowCanvasControl()
    {
        AllowDrop = true;
        Focusable = true;
        Background = CanvasBackground;
        _surface.Background = CreateGridBrush();
        _surface.LayoutTransform = _zoomTransform;
        UpdateZoomText();

        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
        _root.RowDefinitions.Add(new RowDefinition());
        _root.Children.Add(CreateCanvasToolbar());

        _scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            PanningMode = PanningMode.Both,
            CanContentScroll = false,
            Background = CanvasBackground,
            Content = _surface
        };
        _scrollViewer.PreviewMouseWheel += OnCanvasMouseWheel;
        _scrollViewer.PreviewMouseDown += OnViewportMouseDown;
        _scrollViewer.PreviewMouseMove += OnViewportMouseMove;
        _scrollViewer.PreviewMouseUp += OnViewportMouseUp;
        _scrollViewer.LostMouseCapture += (_, _) => EndPan();
        _scrollViewer.ScrollChanged += (_, _) => UpdateMiniMapViewport();
        _scrollViewer.SizeChanged += (_, _) =>
        {
            if (_initialViewPending)
            {
                CenterInitialView();
            }
            UpdateMiniMapViewport();
        };
        Grid.SetRow(_scrollViewer, 1);
        _root.Children.Add(_scrollViewer);

        _miniMapHost = CreateMiniMap();
        Grid.SetRow(_miniMapHost, 1);
        Panel.SetZIndex(_miniMapHost, 100);
        _root.Children.Add(_miniMapHost);
        Content = _root;

        Loaded += (_, _) => Attach(DataContext as MethodEditorViewModel);
        Unloaded += (_, _) => Detach();
        DataContextChanged += (_, e) => Attach(e.NewValue as MethodEditorViewModel);
        DragOver += OnDragOver;
        Drop += OnDrop;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
        PreviewMouseMove += OnCanvasMouseMove;
        PreviewMouseLeftButtonUp += OnCanvasMouseLeftButtonUp;
        PreviewMouseRightButtonDown += OnCanvasRightButtonDown;
    }

    private UIElement CreateCanvasToolbar()
    {
        var border = new Border
        {
            Background = BrushFromRgb(31, 33, 36),
            BorderBrush = BrushFromRgb(57, 61, 66),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 0, 10, 0)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var legend = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        legend.Children.Add(CreateLegendItem(FlowBrush, "Execution order"));
        legend.Children.Add(CreateLegendItem(DataBrush, "Variable mapping", new Thickness(18, 0, 0, 0)));
        legend.Children.Add(new TextBlock
        {
            Text = "Drag blue ports to map values · Wheel zooms · Middle mouse or Space+drag pans",
            Foreground = SecondaryText,
            FontSize = 11,
            Margin = new Thickness(22, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        grid.Children.Add(legend);

        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        var zoomOut = CreateToolbarButton("−", 30, "Zoom out");
        zoomOut.Click += (_, _) => SetZoom(_zoom - ZoomStep, keepCursorPosition: true);
        controls.Children.Add(zoomOut);

        var zoomReset = CreateToolbarButton(string.Empty, 54, "Reset zoom to 100%");
        zoomReset.Content = _zoomText;
        zoomReset.Click += (_, _) => SetZoom(1.0, keepCursorPosition: true);
        controls.Children.Add(zoomReset);

        var zoomIn = CreateToolbarButton("+", 30, "Zoom in");
        zoomIn.Click += (_, _) => SetZoom(_zoom + ZoomStep, keepCursorPosition: true);
        controls.Children.Add(zoomIn);

        var fit = CreateToolbarButton("Fit", 42, "Fit and center all actions in the viewport");
        fit.Margin = new Thickness(8, 0, 0, 0);
        fit.Click += (_, _) => FitToContent();
        controls.Children.Add(fit);

        var miniMap = CreateToolbarButton("Mini map", 70, "Show or hide the mini map inside the canvas");
        miniMap.Click += (_, _) =>
        {
            _miniMapHost.Visibility = _miniMapHost.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
            if (_miniMapHost.Visibility == Visibility.Visible)
            {
                RenderMiniMap();
            }
        };
        controls.Children.Add(miniMap);

        var autoLayout = CreateToolbarButton(
            "Auto layout",
            88,
            "Arrange all actions without changing their List view execution order");
        autoLayout.Click += (_, _) => AutoLayout();
        controls.Children.Add(autoLayout);

        Grid.SetColumn(controls, 1);
        grid.Children.Add(controls);

        border.Child = grid;
        return border;
    }

    private Border CreateMiniMap()
    {
        var host = new Border
        {
            Width = 254,
            Height = 160,
            Margin = new Thickness(14),
            Padding = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = BrushFromRgb(28, 30, 33),
            BorderBrush = BrushFromRgb(78, 83, 89),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Effect = new DropShadowEffect
            {
                BlurRadius = 16,
                ShadowDepth = 0,
                Opacity = 0.55,
                Color = Colors.Black
            }
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
        root.RowDefinitions.Add(new RowDefinition());

        var header = new Grid
        {
            Background = BrushFromRgb(37, 39, 43)
        };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        header.Children.Add(new TextBlock
        {
            Text = "Mini map",
            Margin = new Thickness(8, 0, 0, 0),
            Foreground = PrimaryText,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });

        var close = CreateToolbarButton("×", 26, "Close mini map");
        close.Width = 26;
        close.Height = 24;
        close.Margin = new Thickness(0);
        close.Padding = new Thickness(0);
        close.Background = Brushes.Transparent;
        close.BorderThickness = new Thickness(0);
        close.Click += (_, _) => host.Visibility = Visibility.Collapsed;
        Grid.SetColumn(close, 1);
        header.Children.Add(close);
        root.Children.Add(header);

        var body = new Border
        {
            Margin = new Thickness(7, 5, 7, 7),
            Background = BrushFromRgb(24, 26, 29),
            BorderBrush = BrushFromRgb(54, 58, 63),
            BorderThickness = new Thickness(1),
            Child = _miniMapCanvas
        };
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        _miniMapCanvas.MouseLeftButtonDown += OnMiniMapMouseDown;
        host.Child = root;
        return host;
    }

    private static Button CreateToolbarButton(string content, double minWidth, string toolTip)
        => new()
        {
            Content = content,
            MinWidth = minWidth,
            Height = 26,
            Margin = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(8, 0, 8, 0),
            Background = BrushFromRgb(49, 52, 56),
            Foreground = PrimaryText,
            BorderBrush = BrushFromRgb(78, 83, 89),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = toolTip
        };

    private static UIElement CreateLegendItem(Brush brush, string text, Thickness? margin = null)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = margin ?? new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(new Border
        {
            Width = 24,
            Height = 3,
            CornerRadius = new CornerRadius(2),
            Background = brush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0)
        });
        panel.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = PrimaryText,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        });
        return panel;
    }

    private static DrawingBrush CreateGridBrush(double zoom = 1.0)
    {
        var minor = zoom < 0.35 ? 60d : zoom < 0.65 ? 24d : 12d;
        var major = minor * 5;
        var finePen = new Pen(BrushFromRgb(38, 41, 44), 0.55);
        var strongPen = new Pen(BrushFromRgb(48, 52, 56), 0.75);
        finePen.Freeze();
        strongPen.Freeze();

        var drawing = new DrawingGroup();
        for (var offset = 0d; offset < major; offset += minor)
        {
            var pen = Math.Abs(offset) < 0.001 ? strongPen : finePen;
            drawing.Children.Add(new GeometryDrawing
            {
                Pen = pen,
                Geometry = new LineGeometry(new Point(offset, 0), new Point(offset, major))
            });
            drawing.Children.Add(new GeometryDrawing
            {
                Pen = pen,
                Geometry = new LineGeometry(new Point(0, offset), new Point(major, offset))
            });
        }
        drawing.Freeze();

        var brush = new DrawingBrush
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, major, major),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, major, major),
            ViewboxUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
            Drawing = drawing
        };
        brush.Freeze();
        return brush;
    }

    private void OnViewportMouseDown(object sender, MouseButtonEventArgs e)
    {
        var shouldPan = e.ChangedButton == MouseButton.Middle
                        || (e.ChangedButton == MouseButton.Left && Keyboard.IsKeyDown(Key.Space));
        if (!shouldPan || _linkSource != null)
        {
            return;
        }

        Focus();
        _isPanning = true;
        _panStart = e.GetPosition(_scrollViewer);
        _panHorizontalOffset = _scrollViewer.HorizontalOffset;
        _panVerticalOffset = _scrollViewer.VerticalOffset;
        _scrollViewer.Cursor = Cursors.Hand;
        _scrollViewer.CaptureMouse();
        e.Handled = true;
    }

    private void OnViewportMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        var current = e.GetPosition(_scrollViewer);
        var delta = current - _panStart;
        _scrollViewer.ScrollToHorizontalOffset(Math.Max(0, _panHorizontalOffset - delta.X));
        _scrollViewer.ScrollToVerticalOffset(Math.Max(0, _panVerticalOffset - delta.Y));
        e.Handled = true;
    }

    private void OnViewportMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        if (e.ChangedButton == MouseButton.Middle
            || e.ChangedButton == MouseButton.Left)
        {
            EndPan();
            e.Handled = true;
        }
    }

    private void EndPan()
    {
        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        _scrollViewer.Cursor = Keyboard.IsKeyDown(Key.Space) ? Cursors.Hand : Cursors.Arrow;
        if (_scrollViewer.IsMouseCaptured)
        {
            _scrollViewer.ReleaseMouseCapture();
        }
    }

    private Rect GetContentBounds(double padding = 0)
    {
        if (_nodes.Count == 0)
        {
            return new Rect(
                WorldOriginX - 320,
                WorldOriginY - 220,
                640,
                440);
        }

        var left = _nodes.Values.Min(node => node.X);
        var top = _nodes.Values.Min(node => node.Y);
        var right = _nodes.Values.Max(node => node.X + NodeWidth);
        var bottom = _nodes.Values.Max(node => node.Y + node.Height);
        return new Rect(
            left - padding,
            top - padding,
            Math.Max(1, right - left + padding * 2),
            Math.Max(1, bottom - top + padding * 2));
    }

    private void CenterInitialView()
    {
        if (_scrollViewer.ViewportWidth < 80 || _scrollViewer.ViewportHeight < 80)
        {
            _initialViewPending = true;
            return;
        }

        _initialViewPending = false;
        var bounds = GetContentBounds(90);
        CenterViewportOn(new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2));
        RenderMiniMap();
    }

    private void FitToContent()
    {
        if (_scrollViewer.ViewportWidth < 80 || _scrollViewer.ViewportHeight < 80)
        {
            _initialViewPending = true;
            return;
        }

        var bounds = GetContentBounds(90);
        var widthZoom = _scrollViewer.ViewportWidth / Math.Max(1, bounds.Width);
        var heightZoom = _scrollViewer.ViewportHeight / Math.Max(1, bounds.Height);
        var targetZoom = Math.Clamp(Math.Min(1.0, Math.Min(widthZoom, heightZoom)), MinimumZoom, MaximumZoom);
        SetZoom(targetZoom, keepCursorPosition: false);
        _initialViewPending = false;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            CenterViewportOn(new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2));
            RenderMiniMap();
        }));
    }

    private void CenterViewportOn(Point worldPoint)
    {
        var horizontal = worldPoint.X * _zoom - _scrollViewer.ViewportWidth / 2;
        var vertical = worldPoint.Y * _zoom - _scrollViewer.ViewportHeight / 2;
        _scrollViewer.ScrollToHorizontalOffset(Math.Max(0, horizontal));
        _scrollViewer.ScrollToVerticalOffset(Math.Max(0, vertical));
        UpdateMiniMapViewport();
    }

    private void RenderMiniMap()
    {
        if (_miniMapHost.Visibility != Visibility.Visible)
        {
            return;
        }

        _miniMapCanvas.Children.Clear();
        _miniMapWorldBounds = GetContentBounds(120);
        var usableWidth = Math.Max(1, MiniMapCanvasWidth - 12);
        var usableHeight = Math.Max(1, MiniMapCanvasHeight - 12);
        _miniMapScale = Math.Min(
            usableWidth / Math.Max(1, _miniMapWorldBounds.Width),
            usableHeight / Math.Max(1, _miniMapWorldBounds.Height));
        _miniMapScale = Math.Max(0.001, _miniMapScale);
        _miniMapOffset = new Point(
            (MiniMapCanvasWidth - _miniMapWorldBounds.Width * _miniMapScale) / 2,
            (MiniMapCanvasHeight - _miniMapWorldBounds.Height * _miniMapScale) / 2);

        if (_vm != null)
        {
            var ordered = _vm.Method.MethodLines
                .Where(line => line.Action != null && _nodes.ContainsKey(line.Uid))
                .OrderBy(line => line.SequenceNo)
                .ThenBy(line => line.LineNo)
                .ToArray();
            for (var index = 0; index + 1 < ordered.Length; index++)
            {
                AddMiniMapConnection(
                    GetExecutionPoint(_nodes[ordered[index].Uid], true),
                    GetExecutionPoint(_nodes[ordered[index + 1].Uid], false),
                    FlowBrush,
                    1.0);
            }

            foreach (var connection in ResolveDataConnections())
            {
                if (_nodes.TryGetValue(connection.Source.Line.Uid, out var sourceNode)
                    && _nodes.TryGetValue(connection.Target.Line.Uid, out var targetNode))
                {
                    AddMiniMapConnection(
                        GetDataPoint(sourceNode, connection.Source),
                        GetDataPoint(targetNode, connection.Target),
                        DataBrush,
                        1.15);
                }
            }
        }

        foreach (var node in _nodes.Values)
        {
            AddMiniMapNodePreview(node);
        }

        Panel.SetZIndex(_miniMapViewport, 10);
        _miniMapCanvas.Children.Add(_miniMapViewport);
        UpdateMiniMapViewport();
    }

    /// <summary>
    /// Draws the real node visual into the mini map instead of replacing it with a plain box.
    /// A VisualBrush keeps the thumbnail synchronized with the node title, ports, values,
    /// selection accent and availability state without introducing a second node template.
    /// </summary>
    private void AddMiniMapNodePreview(NodeVisualInfo node)
    {
        var topLeft = MapToMiniMap(new Point(node.X, node.Y));
        var previewWidth = Math.Max(8, NodeWidth * _miniMapScale);
        var previewHeight = Math.Max(6, node.Height * _miniMapScale);
        var accent = GetAccent(node.Line.Action?.ActionType ?? string.Empty);

        var nodeBrush = new VisualBrush(node.Node)
        {
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
            Stretch = Stretch.Fill,
            TileMode = TileMode.None
        };

        // The real node remains the single visual source of truth. At normal mini-map
        // sizes this preserves the header, title, input/output rows and port colours.
        var preview = new Border
        {
            Width = previewWidth,
            Height = previewHeight,
            Background = nodeBrush,
            BorderBrush = accent,
            BorderThickness = new Thickness(previewWidth >= 18 ? 0.65 : 0.4),
            CornerRadius = new CornerRadius(Math.Min(2, Math.Max(0.5, previewHeight * 0.06))),
            Opacity = 0.96,
            SnapsToDevicePixels = true,
            IsHitTestVisible = false
        };

        Canvas.SetLeft(preview, topLeft.X);
        Canvas.SetTop(preview, topLeft.Y);
        Panel.SetZIndex(preview, 2);
        _miniMapCanvas.Children.Add(preview);
    }

    private void AddMiniMapConnection(Point start, Point end, Brush brush, double thickness)
    {
        var miniStart = MapToMiniMap(start);
        var miniEnd = MapToMiniMap(end);
        var line = new Line
        {
            X1 = miniStart.X,
            Y1 = miniStart.Y,
            X2 = miniEnd.X,
            Y2 = miniEnd.Y,
            Stroke = brush,
            StrokeThickness = thickness,
            Opacity = 0.8,
            IsHitTestVisible = false
        };
        Panel.SetZIndex(line, 1);
        _miniMapCanvas.Children.Add(line);
    }

    private Point MapToMiniMap(Point worldPoint)
        => new(
            _miniMapOffset.X + (worldPoint.X - _miniMapWorldBounds.X) * _miniMapScale,
            _miniMapOffset.Y + (worldPoint.Y - _miniMapWorldBounds.Y) * _miniMapScale);

    private void UpdateMiniMapViewport()
    {
        if (_miniMapHost.Visibility != Visibility.Visible
            || _miniMapWorldBounds.IsEmpty
            || _miniMapScale <= 0)
        {
            return;
        }

        var visibleLeft = _scrollViewer.HorizontalOffset / _zoom;
        var visibleTop = _scrollViewer.VerticalOffset / _zoom;
        var visibleWidth = _scrollViewer.ViewportWidth / _zoom;
        var visibleHeight = _scrollViewer.ViewportHeight / _zoom;
        var topLeft = MapToMiniMap(new Point(visibleLeft, visibleTop));
        _miniMapViewport.Width = Math.Max(8, visibleWidth * _miniMapScale);
        _miniMapViewport.Height = Math.Max(8, visibleHeight * _miniMapScale);
        Canvas.SetLeft(_miniMapViewport, topLeft.X);
        Canvas.SetTop(_miniMapViewport, topLeft.Y);
    }

    private void OnMiniMapMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_miniMapWorldBounds.IsEmpty || _miniMapScale <= 0)
        {
            return;
        }

        var point = e.GetPosition(_miniMapCanvas);
        var worldPoint = new Point(
            _miniMapWorldBounds.X + (point.X - _miniMapOffset.X) / _miniMapScale,
            _miniMapWorldBounds.Y + (point.Y - _miniMapOffset.Y) / _miniMapScale);
        CenterViewportOn(worldPoint);
        e.Handled = true;
    }

    private void OnCanvasMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_linkSource != null)
        {
            return;
        }

        var cursor = e.GetPosition(_scrollViewer);
        SetZoom(_zoom + (e.Delta > 0 ? ZoomStep : -ZoomStep), keepCursorPosition: true, cursor: cursor);
        e.Handled = true;
    }

    private void SetZoom(double value, bool keepCursorPosition = false, Point? cursor = null)
    {
        var next = Math.Clamp(Math.Round(value / ZoomStep) * ZoomStep, MinimumZoom, MaximumZoom);
        if (Math.Abs(next - _zoom) < 0.001)
        {
            return;
        }

        var oldZoom = _zoom;
        var pointer = cursor ?? new Point(
            Math.Max(0, _scrollViewer.ViewportWidth / 2),
            Math.Max(0, _scrollViewer.ViewportHeight / 2));
        var logicalX = (_scrollViewer.HorizontalOffset + pointer.X) / oldZoom;
        var logicalY = (_scrollViewer.VerticalOffset + pointer.Y) / oldZoom;

        _zoom = next;
        _zoomTransform.ScaleX = _zoom;
        _zoomTransform.ScaleY = _zoom;
        _surface.Background = CreateGridBrush(_zoom);
        UpdateZoomText();

        if (!keepCursorPosition)
        {
            Dispatcher.BeginInvoke(new Action(UpdateMiniMapViewport));
            return;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            _scrollViewer.ScrollToHorizontalOffset(Math.Max(0, logicalX * _zoom - pointer.X));
            _scrollViewer.ScrollToVerticalOffset(Math.Max(0, logicalY * _zoom - pointer.Y));
            UpdateMiniMapViewport();
        }));
    }

    private void UpdateZoomText()
        => _zoomText.Text = $"{Math.Round(_zoom * 100):0}%";

    private void Attach(MethodEditorViewModel? vm)
    {
        if (ReferenceEquals(_vm, vm))
        {
            Rebuild();
            return;
        }

        Detach();
        _vm = vm;
        _zoom = 1.0;
        _zoomTransform.ScaleX = 1.0;
        _zoomTransform.ScaleY = 1.0;
        _surface.Background = CreateGridBrush(1.0);
        UpdateZoomText();
        _initialViewPending = true;
        if (_vm?.Owner.VisibleMethodLineItems is INotifyCollectionChanged collection)
        {
            collection.CollectionChanged += OnLinesChanged;
        }
        if (_vm?.Owner is INotifyPropertyChanged owner)
        {
            owner.PropertyChanged += OnOwnerPropertyChanged;
        }
        if (_vm != null)
        {
            _vm.Owner.CanvasContentChanged += OnCanvasContentChanged;
        }
        Rebuild();
    }

    private void Detach()
    {
        if (_vm?.Owner.VisibleMethodLineItems is INotifyCollectionChanged oldCollection)
        {
            oldCollection.CollectionChanged -= OnLinesChanged;
        }
        if (_vm?.Owner is INotifyPropertyChanged oldOwner)
        {
            oldOwner.PropertyChanged -= OnOwnerPropertyChanged;
        }
        if (_vm != null)
        {
            _vm.Owner.CanvasContentChanged -= OnCanvasContentChanged;
        }
        _vm = null;
    }

    private bool _rebuildPending;

    private void OnCanvasContentChanged(object? sender, EventArgs e)
        => ScheduleRebuild();

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => ScheduleRebuild();

    private void ScheduleRebuild()
    {
        if (_rebuildPending)
        {
            return;
        }

        _rebuildPending = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _rebuildPending = false;
            Rebuild();
        }));
    }

    private void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.SelectedMethodLine)
            or nameof(MainWindowViewModel.SelectedMethod))
        {
            UpdateSelection();
        }
    }

    private void Rebuild()
    {
        if (_isRebuilding)
        {
            return;
        }

        _isRebuilding = true;
        try
        {
            CancelDataLink();
            _surface.Children.Clear();
            _nodes.Clear();
            _ports.Clear();
            if (_vm == null)
            {
                return;
            }

            var lines = _vm.Method.MethodLines
                .Where(line => line.Action != null)
                .OrderBy(line => line.SequenceNo)
                .ThenBy(line => line.LineNo)
                .ToList();

            for (var index = 0; index < lines.Count; index++)
            {
                var line = lines[index];
                EnsurePosition(line, index);
                var inputs = BuildInputPorts(line);
                var outputs = BuildOutputPorts(line);
                var node = CreateNode(line, inputs, outputs);
                var x = WorldOriginX + ReadDouble(line.ExtensionData[XKey], 80 + (index % 3) * 365);
                var y = WorldOriginY + ReadDouble(line.ExtensionData[YKey], 70 + (index / 3) * 245);
                var nodeInfo = new NodeVisualInfo(line, node, inputs, outputs, x, y, node.Height);
                _nodes[line.Uid] = nodeInfo;
                foreach (var port in inputs.Concat(outputs))
                {
                    _ports[port.Identity] = port;
                }

                Canvas.SetLeft(node, x);
                Canvas.SetTop(node, y);
                Panel.SetZIndex(node, 10);
                _surface.Children.Add(node);
            }

            DrawConnectionsOnly();
            UpdateSelection();
            UpdateSurfaceExtent();
            RenderMiniMap();
            if (_initialViewPending)
            {
                Dispatcher.BeginInvoke(new Action(CenterInitialView));
            }
        }
        finally
        {
            _isRebuilding = false;
        }
    }

    private void EnsurePosition(MethodLine line, int index)
    {
        if (line.ExtensionData[XKey] == null)
        {
            line.ExtensionData[XKey] = 80 + (index % 3) * 365;
        }
        if (line.ExtensionData[YKey] == null)
        {
            line.ExtensionData[YKey] = 70 + (index / 3) * 245;
        }
    }

    private Border CreateNode(
        MethodLine line,
        IReadOnlyList<CanvasPortDefinition> inputs,
        IReadOnlyList<CanvasPortDefinition> outputs)
    {
        var action = line.Action!;
        var descriptor = _vm?.Owner.ResolveActionDescriptor(action);
        var accent = GetAccent(action.ActionType);
        var displayName = !string.IsNullOrWhiteSpace(action.Name)
            ? action.Name
            : descriptor?.DisplayName ?? GetShortActionType(action.ActionType);
        var subtitle = descriptor?.Category ?? GetShortActionType(action.ActionType);
        var portRows = Math.Max(1, Math.Max(inputs.Count, outputs.Count));
        var nodeHeight = HeaderHeight + FlowRowHeight + 1 + portRows * PortRowHeight + NodeBottomPadding;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderHeight) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(FlowRowHeight) });        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        root.RowDefinitions.Add(new RowDefinition());

        var header = new Grid { Background = NodeHeaderBackground };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.Children.Add(new Border { Background = accent });

        var iconBorder = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(4),
            Background = BrushFromRgb(31, 33, 36),
            BorderBrush = accent,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(iconBorder, 1);
        iconBorder.Child = new TextBlock
        {
            Text = GetGlyph(action.ActionType),
            Foreground = accent,
            FontSize = action.ActionType.Contains("script", StringComparison.OrdinalIgnoreCase) ? 12 : 18,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(iconBorder);

        var titlePanel = new StackPanel
        {
            Margin = new Thickness(3, 5, 8, 4),
            VerticalAlignment = VerticalAlignment.Center
        };
        titlePanel.Children.Add(new TextBlock
        {
            Text = displayName,
            Foreground = PrimaryText,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = subtitle,
            Foreground = SecondaryText,
            FontSize = 10.5,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(titlePanel, 2);
        header.Children.Add(titlePanel);
        root.Children.Add(header);

        var flowRow = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        flowRow.ColumnDefinitions.Add(new ColumnDefinition());
        flowRow.ColumnDefinitions.Add(new ColumnDefinition());
        flowRow.Children.Add(CreateExecutionPort(line, isOutput: false));
        var flowOut = CreateExecutionPort(line, isOutput: true);
        Grid.SetColumn(flowOut, 1);
        flowRow.Children.Add(flowOut);
        Grid.SetRow(flowRow, 1);
        root.Children.Add(flowRow);

        var separator = new Border { Background = BrushFromRgb(59, 63, 68) };
        Grid.SetRow(separator, 2);
        root.Children.Add(separator);

        var portsGrid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        portsGrid.ColumnDefinitions.Add(new ColumnDefinition());
        portsGrid.ColumnDefinitions.Add(new ColumnDefinition());
        var inputPanel = new StackPanel();
        var outputPanel = new StackPanel();
        for (var index = 0; index < portRows; index++)
        {
            inputPanel.Children.Add(index < inputs.Count
                ? CreateDataPortRow(inputs[index])
                : CreateEmptyPortRow());
            outputPanel.Children.Add(index < outputs.Count
                ? CreateDataPortRow(outputs[index])
                : CreateEmptyPortRow());
        }
        portsGrid.Children.Add(inputPanel);
        Grid.SetColumn(outputPanel, 1);
        portsGrid.Children.Add(outputPanel);
        Grid.SetRow(portsGrid, 3);
        root.Children.Add(portsGrid);

        var node = new Border
        {
            Width = NodeWidth,
            Height = nodeHeight,
            Background = NodeBackground,
            BorderBrush = line.IsActionAvailable ? SoftBorder : WarningBrush,
            BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(6),
            Child = root,
            Tag = line,
            Cursor = Cursors.SizeAll,
            SnapsToDevicePixels = true,
            ClipToBounds = true,
            Opacity = line.IsActive && action.IsActive ? 1 : 0.55,
            ToolTip = line.IsActionAvailable
                ? descriptor?.Description
                : line.ActionAvailabilityMessage
        };
        node.MouseLeftButtonDown += OnNodeMouseDown;
        node.MouseMove += OnNodeMouseMove;
        node.MouseLeftButtonUp += OnNodeMouseUp;
        node.MouseRightButtonDown += (_, e) =>
        {
            Select(line);
            e.Handled = true;
        };
        return node;
    }

    private static UIElement CreateExecutionPort(MethodLine line, bool isOutput)
    {
        var grid = new Grid
        {
            Height = FlowRowHeight,
            Margin = isOutput ? new Thickness(8, 0, 0, 0) : new Thickness(0, 0, 8, 0)
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });

        var dot = new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = NodeBackground,
            Stroke = FlowBrush,
            StrokeThickness = 2,
            IsHitTestVisible = false,
            ToolTip = isOutput ? "Execution output (order only)" : "Execution input (order only)"
        };
        Grid.SetColumn(dot, isOutput ? 2 : 0);
        grid.Children.Add(dot);

        var text = new TextBlock
        {
            Text = isOutput ? "Exec out" : "Exec in",
            Foreground = SecondaryText,
            FontSize = 10.5,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = isOutput ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Margin = isOutput ? new Thickness(0, 0, 5, 0) : new Thickness(5, 0, 0, 0)
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }

    private UIElement CreateDataPortRow(CanvasPortDefinition port)
    {
        var grid = new Grid
        {
            Height = PortRowHeight,
            Background = Brushes.Transparent,
            ToolTip = $"{port.DisplayName}\nType: {port.ValueType}\n{port.GetTooltipBindingText()}"
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });

        var dotBrush = port.CanConnect ? DataBrush : DisabledPortBrush;
        var dot = new Ellipse
        {
            Width = 11,
            Height = 11,
            Fill = NodeBackground,
            Stroke = dotBrush,
            StrokeThickness = 2.2,
            Cursor = port.CanConnect
                ? (port.IsOutput ? Cursors.Cross : Cursors.Hand)
                : Cursors.Arrow,
            Tag = new PortHandleTag(port),
            Opacity = port.CanConnect ? 1 : 0.65
        };
        if (port.IsOutput && port.CanConnect)
        {
            dot.PreviewMouseLeftButtonDown += OnOutputPortMouseDown;
        }
        Grid.SetColumn(dot, port.IsOutput ? 2 : 0);
        grid.Children.Add(dot);

        var textPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = port.IsOutput ? new Thickness(4, 0, 5, 0) : new Thickness(5, 0, 4, 0)
        };
        textPanel.Children.Add(new TextBlock
        {
            Text = port.GetDisplayText(),
            Foreground = PrimaryText,
            FontSize = 11.2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = port.IsOutput ? HorizontalAlignment.Right : HorizontalAlignment.Left
        });
        textPanel.Children.Add(new TextBlock
        {
            Text = port.ValueType,
            Foreground = SecondaryText,
            FontSize = 9.4,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = port.IsOutput ? HorizontalAlignment.Right : HorizontalAlignment.Left
        });
        Grid.SetColumn(textPanel, 1);
        grid.Children.Add(textPanel);
        return grid;
    }

    private static UIElement CreateEmptyPortRow()
        => new Border { Height = PortRowHeight, Background = Brushes.Transparent };

    private IReadOnlyList<CanvasPortDefinition> BuildInputPorts(MethodLine line)
    {
        if (_vm == null || line.Action == null)
        {
            return Array.Empty<CanvasPortDefinition>();
        }

        var action = line.Action;
        var descriptor = _vm.Owner.ResolveActionDescriptor(action);
        var targetMethod = ResolveTargetMethod(action);
        var result = new List<CanvasPortDefinition>();
        var representedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (descriptor != null)
        {
            foreach (var field in GetCanvasInputFields(descriptor))
            {
                representedFields.Add(field.Name);
                if (targetMethod != null && string.Equals(field.Name, "Parameters", StringComparison.OrdinalIgnoreCase))
                {
                    result.AddRange(CreateMethodParameterPorts(line, action, targetMethod));
                    continue;
                }

                if (targetMethod != null && string.Equals(field.Name, "ReturnVarNames", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                result.AddRange(CreateStandardInputPorts(line, action, field));
            }
        }

        // The Runtime catalog can be older than a project-local C# script revision. Preserve and
        // project every real Action value that the property panel can edit, even when that value
        // is not yet present in descriptor.Inputs.
        var fallbackOrder = result.Count == 0 ? 0 : result.Max(port => port.Order) + 1;
        foreach (var property in action.GetEditableProperties())
        {
            if (representedFields.Contains(property.Key)
                || IsCanvasCommonField(property.Key)
                || (targetMethod != null
                    && (string.Equals(property.Key, "Parameters", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(property.Key, "ReturnVarNames", StringComparison.OrdinalIgnoreCase))))
            {
                continue;
            }

            result.AddRange(CreateUnknownInputPorts(line, action, property.Key, property.Value, fallbackOrder++));
        }

        return result
            .OrderBy(port => port.Order)
            .ThenBy(port => port.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<CanvasPortDefinition> BuildOutputPorts(MethodLine line)
    {
        if (_vm == null || line.Action == null)
        {
            return Array.Empty<CanvasPortDefinition>();
        }

        var action = line.Action;
        var descriptor = _vm.Owner.ResolveActionDescriptor(action);
        var targetMethod = ResolveTargetMethod(action);
        if (targetMethod != null && IsActionType(action, "runMethod"))
        {
            return CreateMethodReturnPorts(line, action, targetMethod).ToArray();
        }

        var result = new List<CanvasPortDefinition>();
        var representedOutputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (descriptor != null)
        {
            foreach (var field in GetCanvasOutputFields(descriptor))
            {
                representedOutputs.Add(field.Name);
                result.Add(CreateStandardOutputPort(line, action, field));
            }
        }

        // Output bindings are authoritative project data. Show them even if a stale/offline
        // catalog has not yet published the matching output descriptor.
        var fallbackOrder = result.Count == 0 ? 0 : result.Max(port => port.Order) + 1;
        foreach (var binding in action.GetOutputBindings())
        {
            if (representedOutputs.Contains(binding.Key))
            {
                continue;
            }

            result.Add(CreateUnknownOutputPort(line, action, binding.Key, binding.Value, fallbackOrder++));
        }

        return result
            .OrderBy(port => port.Order)
            .ThenBy(port => port.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<WorkflowActionFieldDto> GetCanvasInputFields(
        WorkflowActionDescriptorDto descriptor)
        => descriptor.Inputs
            .Concat(descriptor.Properties)
            .Where(field => !IsCanvasCommonField(field.Name))
            .Where(field => !IsCanvasOutputField(field))
            .DistinctBy(field => field.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(field => field.Order);

    private static IEnumerable<WorkflowActionFieldDto> GetCanvasOutputFields(
        WorkflowActionDescriptorDto descriptor)
        => descriptor.Outputs
            .Concat(descriptor.Properties.Where(IsCanvasOutputField))
            .Where(field => !IsCanvasCommonField(field.Name))
            .DistinctBy(field => field.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(field => field.Order);

    private static bool IsCanvasOutputField(WorkflowActionFieldDto field)
        => field.SupportsOutputBinding
           || string.Equals(field.Direction, "output", StringComparison.OrdinalIgnoreCase);

    private static bool IsCanvasCommonField(string name)
        => name.Equals("Comment", StringComparison.OrdinalIgnoreCase)
           || name.Equals("Deactivate", StringComparison.OrdinalIgnoreCase)
           || name.Equals("IsActive", StringComparison.OrdinalIgnoreCase)
           || name.Equals("Name", StringComparison.OrdinalIgnoreCase);

    private IReadOnlyList<CanvasPortDefinition> CreateUnknownInputPorts(
        MethodLine line,
        WorkflowAction action,
        string name,
        JsonNode? value,
        int order)
    {
        var field = new WorkflowActionFieldDto
        {
            Name = name,
            DisplayName = name,
            Description = "Project-local Action input preserved without current catalog metadata.",
            Order = order,
            ValueType = InferValueType(value),
            Direction = "input",
            Category = "Action",
            Editor = value is JsonArray or JsonObject ? "json" : "text",
            SupportsVariableExpression = true
        };
        return CreateStandardInputPorts(line, action, field);
    }

    private CanvasPortDefinition CreateUnknownOutputPort(
        MethodLine line,
        WorkflowAction action,
        string name,
        string binding,
        int order)
        => new(
            line,
            name,
            ResolvePortDisplayName(name, binding, name),
            ResolvePortValueType("object", binding),
            isOutput: true,
            canConnect: true,
            order,
            () => action.GetOutputBinding(name),
            value => action.SetOutputBinding(name, value),
            defaultText: string.Empty);

    private IReadOnlyList<CanvasPortDefinition> CreateStandardInputPorts(
        MethodLine line,
        WorkflowAction action,
        WorkflowActionFieldDto field)
    {
        var node = action.GetProperty(field.Name);
        if (node is JsonArray array && array.Count > 0)
        {
            return Enumerable.Range(0, array.Count)
                .Select(index => CreateArrayInputPort(line, action, field, index))
                .ToArray();
        }

        if (node is JsonObject objectValue && objectValue.Count > 0)
        {
            return objectValue
                .Select((pair, index) => CreateObjectInputPort(line, action, field, pair.Key, index))
                .ToArray();
        }

        var rawValue = ReadNodeText(node);
        var listValues = SplitVariableList(rawValue);
        if (listValues.Count > 1)
        {
            return listValues
                .Select((_, index) => CreateDelimitedInputPort(
                    line,
                    action,
                    field,
                    index,
                    listValues.Count))
                .ToArray();
        }

        return new[] { CreateScalarInputPort(line, action, field) };
    }

    private CanvasPortDefinition CreateScalarInputPort(
        MethodLine line,
        WorkflowAction action,
        WorkflowActionFieldDto field)
    {
        var binding = ReadNodeText(action.GetProperty(field.Name));
        var displayName = ResolvePortDisplayName(
            string.IsNullOrWhiteSpace(field.DisplayName) ? field.Name : field.DisplayName,
            binding,
            field.Name);
        return new CanvasPortDefinition(
            line,
            field.Name,
            displayName,
            ResolvePortValueType(field.ValueType, binding),
            isOutput: false,
            CanFieldConnect(field, binding),
            field.Order,
            () => ReadNodeText(action.GetProperty(field.Name)),
            value => WriteScalarActionProperty(action, field.Name, value),
            defaultText: ReadNodeText(field.DefaultValue));
    }

    private CanvasPortDefinition CreateDelimitedInputPort(
        MethodLine line,
        WorkflowAction action,
        WorkflowActionFieldDto field,
        int index,
        int expectedCount)
    {
        var values = SplitVariableList(ReadNodeText(action.GetProperty(field.Name)), preserveEmpty: true);
        var binding = index < values.Count ? values[index] : string.Empty;
        var fieldDisplayName = string.IsNullOrWhiteSpace(field.DisplayName) ? field.Name : field.DisplayName;
        var displayName = ResolvePortDisplayName(
            $"{fieldDisplayName} {index + 1}",
            binding,
            $"{field.Name}[{index}]");
        return new CanvasPortDefinition(
            line,
            $"{field.Name}[{index}]",
            displayName,
            ResolvePortValueType(field.ValueType, binding),
            isOutput: false,
            CanFieldConnect(field, binding),
            field.Order * 100 + index,
            () =>
            {
                var current = SplitVariableList(
                    ReadNodeText(action.GetProperty(field.Name)),
                    preserveEmpty: true);
                return index < current.Count ? current[index] : string.Empty;
            },
            value => WriteDelimitedActionProperty(action, field.Name, index, expectedCount, value),
            defaultText: string.Empty);
    }

    private CanvasPortDefinition CreateArrayInputPort(
        MethodLine line,
        WorkflowAction action,
        WorkflowActionFieldDto field,
        int index)
    {
        var binding = action.GetProperty(field.Name) is JsonArray current && index < current.Count
            ? ReadNodeText(current[index])
            : string.Empty;
        var fieldDisplayName = string.IsNullOrWhiteSpace(field.DisplayName) ? field.Name : field.DisplayName;
        var displayName = ResolvePortDisplayName(
            $"{fieldDisplayName} {index + 1}",
            binding,
            $"{field.Name}[{index}]");
        return new CanvasPortDefinition(
            line,
            $"{field.Name}[{index}]",
            displayName,
            ResolvePortValueType(field.ValueType, binding),
            isOutput: false,
            CanFieldConnect(field, binding),
            field.Order * 100 + index,
            () => action.GetProperty(field.Name) is JsonArray array && index < array.Count
                ? ReadNodeText(array[index])
                : string.Empty,
            value => WriteArrayActionProperty(action, field.Name, index, value),
            defaultText: string.Empty);
    }

    private CanvasPortDefinition CreateObjectInputPort(
        MethodLine line,
        WorkflowAction action,
        WorkflowActionFieldDto field,
        string propertyName,
        int index)
    {
        var binding = action.GetProperty(field.Name) is JsonObject current
            ? ReadNodeText(current[propertyName])
            : string.Empty;
        var displayName = ResolvePortDisplayName(propertyName, binding, $"{field.Name}.{propertyName}");
        return new CanvasPortDefinition(
            line,
            $"{field.Name}.{propertyName}",
            displayName,
            ResolvePortValueType(field.ValueType, binding),
            isOutput: false,
            CanFieldConnect(field, binding),
            field.Order * 100 + index,
            () => action.GetProperty(field.Name) is JsonObject objectValue
                ? ReadNodeText(objectValue[propertyName])
                : string.Empty,
            value => WriteObjectActionProperty(action, field.Name, propertyName, value),
            defaultText: string.Empty);
    }

    private CanvasPortDefinition CreateStandardOutputPort(
        MethodLine line,
        WorkflowAction action,
        WorkflowActionFieldDto field)
    {
        var binding = action.GetOutputBinding(field.Name);
        var fallbackName = string.IsNullOrWhiteSpace(field.DisplayName) ? field.Name : field.DisplayName;
        return new CanvasPortDefinition(
            line,
            field.Name,
            ResolvePortDisplayName(fallbackName, binding, field.Name),
            ResolvePortValueType(field.ValueType, binding),
            isOutput: true,
            canConnect: field.SupportsOutputBinding
                        || string.Equals(field.Direction, "output", StringComparison.OrdinalIgnoreCase)
                        || !string.IsNullOrWhiteSpace(binding),
            field.Order,
            () => action.GetOutputBinding(field.Name),
            value => action.SetOutputBinding(field.Name, value),
            defaultText: string.Empty);
    }

    private bool CanFieldConnect(WorkflowActionFieldDto field, string binding)
        => field.SupportsVariableExpression
           || string.Equals(DesignerKeyCompatibility.NormalizePropertyEditor(field.Editor), WorkflowPropertyEditorKeys.Variable, StringComparison.OrdinalIgnoreCase)
           || string.Equals(DesignerKeyCompatibility.NormalizePropertyEditor(field.Editor), WorkflowPropertyEditorKeys.Expression, StringComparison.OrdinalIgnoreCase)
           || WorkflowVariableNaming.IsVariable(binding.Trim());

    private string ResolvePortDisplayName(string fallback, string binding, string sourceName)
    {
        binding = binding.Trim();
        if (_vm == null || !WorkflowVariableNaming.IsVariable(binding))
        {
            return fallback;
        }

        var parameter = _vm.Method.Inputs
            .Concat(_vm.Method.Outputs)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.VariableName, binding, StringComparison.OrdinalIgnoreCase));
        if (parameter != null)
        {
            return string.IsNullOrWhiteSpace(parameter.DisplayName)
                ? parameter.Name
                : parameter.DisplayName;
        }

        var variable = _vm.Method.MethodVariables.FirstOrDefault(candidate =>
            candidate.IsActive
            && string.Equals(candidate.VariableName, binding, StringComparison.OrdinalIgnoreCase));
        if (variable != null)
        {
            return string.IsNullOrWhiteSpace(variable.Label)
                ? sourceName
                : variable.Label;
        }

        var baseName = WorkflowVariableNaming.GetBaseName(binding);
        return string.IsNullOrWhiteSpace(baseName) ? fallback : baseName;
    }

    private string ResolvePortValueType(string fallback, string binding)
    {
        binding = binding.Trim();
        if (_vm == null || !WorkflowVariableNaming.IsVariable(binding))
        {
            return fallback;
        }

        var parameter = _vm.Method.Inputs
            .Concat(_vm.Method.Outputs)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.VariableName, binding, StringComparison.OrdinalIgnoreCase));
        if (parameter != null && !string.IsNullOrWhiteSpace(parameter.ValueType))
        {
            return parameter.ValueType;
        }

        var variable = _vm.Method.MethodVariables.FirstOrDefault(candidate =>
            candidate.IsActive
            && string.Equals(candidate.VariableName, binding, StringComparison.OrdinalIgnoreCase));
        return variable?.DataType ?? fallback;
    }

    private static IReadOnlyList<string> SplitVariableList(string value, bool preserveEmpty = false)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.Contains(','))
        {
            return Array.Empty<string>();
        }

        var options = preserveEmpty
            ? StringSplitOptions.None
            : StringSplitOptions.RemoveEmptyEntries;
        var values = value
            .Split(',', options)
            .Select(item => item.Trim())
            .ToArray();
        if (values.Length <= 1
            || !values.Any(WorkflowVariableNaming.IsVariable))
        {
            return Array.Empty<string>();
        }

        return values;
    }

    private static void WriteScalarActionProperty(
        WorkflowAction action,
        string fieldName,
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            action.RemoveProperty(fieldName);
        }
        else
        {
            action.SetProperty(fieldName, JsonValue.Create(value.Trim()));
        }
    }

    private static void WriteDelimitedActionProperty(
        WorkflowAction action,
        string fieldName,
        int index,
        int expectedCount,
        string value)
    {
        var currentText = ReadNodeText(action.GetProperty(fieldName));
        var current = currentText.Contains(',')
            ? currentText.Split(',', StringSplitOptions.None).Select(item => item.Trim()).ToList()
            : new List<string>();
        while (current.Count < Math.Max(expectedCount, index + 1))
        {
            current.Add(string.Empty);
        }

        current[index] = value.Trim();
        var last = current.FindLastIndex(item => item.Length > 0);
        if (last < 0)
        {
            action.RemoveProperty(fieldName);
            return;
        }

        action.SetProperty(fieldName, JsonValue.Create(string.Join(',', current.Take(last + 1))));
    }

    private static void WriteArrayActionProperty(
        WorkflowAction action,
        string fieldName,
        int index,
        string value)
    {
        var array = action.GetProperty(fieldName) is JsonArray current
            ? (JsonArray)current.DeepClone()
            : new JsonArray();
        while (array.Count <= index)
        {
            array.Add(null);
        }

        array[index] = string.IsNullOrWhiteSpace(value) ? null : JsonValue.Create(value.Trim());
        action.SetProperty(fieldName, array);
    }

    private static void WriteObjectActionProperty(
        WorkflowAction action,
        string fieldName,
        string propertyName,
        string value)
    {
        var objectValue = action.GetProperty(fieldName) is JsonObject current
            ? (JsonObject)current.DeepClone()
            : new JsonObject();
        if (string.IsNullOrWhiteSpace(value))
        {
            objectValue.Remove(propertyName);
        }
        else
        {
            objectValue[propertyName] = value.Trim();
        }

        if (objectValue.Count == 0)
        {
            action.RemoveProperty(fieldName);
        }
        else
        {
            action.SetProperty(fieldName, objectValue);
        }
    }

    private IEnumerable<CanvasPortDefinition> CreateMethodParameterPorts(
        MethodLine line,
        WorkflowAction action,
        WorkflowMethod targetMethod)
    {
        foreach (var input in targetMethod.Inputs
                     .OrderBy(item => item.Order)
                     .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var inputName = input.Name;
            var variable = targetMethod.MethodVariables.FirstOrDefault(candidate =>
                candidate.IsActive
                && string.Equals(candidate.VariableName, input.VariableName, StringComparison.OrdinalIgnoreCase));
            var expectedType = variable?.DataType ?? input.ValueType;
            var defaultValue = variable?.DefaultValue ?? input.DefaultValue;
            var binding = ReadParameter(action, inputName);
            var fallbackName = string.IsNullOrWhiteSpace(input.DisplayName) ? input.Name : input.DisplayName;
            yield return new CanvasPortDefinition(
                line,
                $"Parameters.{inputName}",
                ResolvePortDisplayName(fallbackName, binding, inputName),
                ResolvePortValueType(expectedType, binding),
                isOutput: false,
                canConnect: true,
                input.Order,
                () => ReadParameter(action, inputName),
                value => WriteParameter(action, inputName, value),
                defaultText: FormatObject(defaultValue));
        }
    }

    private IEnumerable<CanvasPortDefinition> CreateMethodReturnPorts(
        MethodLine line,
        WorkflowAction action,
        WorkflowMethod targetMethod)
    {
        var outputs = targetMethod.Outputs
            .OrderBy(item => item.Order)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        for (var index = 0; index < outputs.Length; index++)
        {
            var outputIndex = index;
            var output = outputs[index];
            var variable = targetMethod.MethodVariables.FirstOrDefault(candidate =>
                candidate.IsActive
                && string.Equals(candidate.VariableName, output.VariableName, StringComparison.OrdinalIgnoreCase));
            var expectedType = variable?.DataType ?? output.ValueType;
            var binding = ReadReturnDestination(action, outputIndex);
            var fallbackName = string.IsNullOrWhiteSpace(output.DisplayName) ? output.Name : output.DisplayName;
            yield return new CanvasPortDefinition(
                line,
                $"ReturnVarNames.{output.Name}",
                ResolvePortDisplayName(fallbackName, binding, output.Name),
                ResolvePortValueType(expectedType, binding),
                isOutput: true,
                canConnect: true,
                output.Order,
                () => ReadReturnDestination(action, outputIndex),
                value => WriteReturnDestination(action, outputIndex, outputs.Length, value),
                defaultText: string.Empty);
        }
    }

    private WorkflowMethod? ResolveTargetMethod(WorkflowAction action)
    {
        if (_vm == null || (!IsActionType(action, "runMethod") && !IsActionType(action, "threadStart")))
        {
            return null;
        }

        var methodName = ReadNodeText(action.GetProperty("MethodName"));
        return string.IsNullOrWhiteSpace(methodName) ? null : _vm.Owner.Project.FindMethod(methodName);
    }

    private void DrawConnectionsOnly()
    {
        foreach (var path in _surface.Children.OfType<Path>().Where(path => !ReferenceEquals(path, _previewPath)).ToList())
        {
            _surface.Children.Remove(path);
        }

        DrawExecutionConnections();
        DrawDataConnections();
    }

    private void DrawExecutionConnections()
    {
        if (_vm == null)
        {
            return;
        }

        var lines = _vm.Method.MethodLines
            .Where(line => line.Action != null && _nodes.ContainsKey(line.Uid))
            .OrderBy(line => line.SequenceNo)
            .ThenBy(line => line.LineNo)
            .ToArray();
        for (var index = 0; index + 1 < lines.Length; index++)
        {
            var start = GetExecutionPoint(_nodes[lines[index].Uid], isOutput: true);
            var end = GetExecutionPoint(_nodes[lines[index + 1].Uid], isOutput: false);
            var path = CreateBezierPath(start, end, FlowBrush, 2.2, hitTestVisible: false);
            path.Opacity = 0.9;
            AddPathBehindNodes(path);
        }
    }

    private void DrawDataConnections()
    {
        foreach (var connection in ResolveDataConnections())
        {
            if (!_nodes.TryGetValue(connection.Source.Line.Uid, out var sourceNode)
                || !_nodes.TryGetValue(connection.Target.Line.Uid, out var targetNode))
            {
                continue;
            }

            var start = GetDataPoint(sourceNode, connection.Source);
            var end = GetDataPoint(targetNode, connection.Target);
            var path = CreateBezierPath(start, end, DataBrush, 2.6, hitTestVisible: true);
            path.Tag = connection;
            path.Cursor = Cursors.Hand;
            path.ToolTip = $"{connection.Source.DisplayName} → {connection.Target.DisplayName}\nVariable: {connection.VariableName}\nRight-click to remove mapping";
            path.MouseRightButtonDown += OnDataConnectionRightClick;
            AddPathBehindNodes(path);
        }
    }

    private IReadOnlyList<ResolvedDataConnection> ResolveDataConnections()
    {
        if (_vm == null)
        {
            return Array.Empty<ResolvedDataConnection>();
        }

        var result = new List<ResolvedDataConnection>();
        var usedTargets = new HashSet<PortIdentity>();
        foreach (var stored in ReadStoredConnections())
        {
            var sourceIdentity = new PortIdentity(stored.SourceLineUid, stored.SourcePort, true);
            var targetIdentity = new PortIdentity(stored.TargetLineUid, stored.TargetPort, false);
            if (!_ports.TryGetValue(sourceIdentity, out var source)
                || !_ports.TryGetValue(targetIdentity, out var target))
            {
                continue;
            }

            var sourceVariable = source.ReadBinding().Trim();
            var targetVariable = target.ReadBinding().Trim();
            if (string.IsNullOrWhiteSpace(sourceVariable)
                || !string.Equals(sourceVariable, targetVariable, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(new ResolvedDataConnection(source, target, sourceVariable));
            usedTargets.Add(targetIdentity);
        }

        var outputPorts = _ports.Values
            .Where(port => port.IsOutput && port.CanConnect)
            .OrderBy(port => port.Line.SequenceNo)
            .ThenBy(port => port.Order)
            .ToArray();
        foreach (var target in _ports.Values
                     .Where(port => !port.IsOutput && port.CanConnect)
                     .OrderBy(port => port.Line.SequenceNo)
                     .ThenBy(port => port.Order))
        {
            if (usedTargets.Contains(target.Identity))
            {
                continue;
            }

            var targetVariable = target.ReadBinding().Trim();
            if (!WorkflowVariableNaming.IsVariable(targetVariable))
            {
                continue;
            }

            var source = outputPorts
                .Where(candidate => candidate.Line.SequenceNo <= target.Line.SequenceNo)
                .Where(candidate => TypesAreCompatible(candidate.ValueType, target.ValueType))
                .LastOrDefault(candidate => string.Equals(
                    candidate.ReadBinding().Trim(),
                    targetVariable,
                    StringComparison.OrdinalIgnoreCase));
            if (source == null)
            {
                continue;
            }

            result.Add(new ResolvedDataConnection(source, target, targetVariable));
            usedTargets.Add(target.Identity);
        }

        return result;
    }

    private void OnOutputPortMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: PortHandleTag tag }
            || !tag.Port.IsOutput
            || !tag.Port.CanConnect)
        {
            return;
        }

        Focus();
        Select(tag.Port.Line);
        _linkSource = tag.Port;
        _previewPath = CreateBezierPath(
            GetDataPoint(_nodes[tag.Port.Line.Uid], tag.Port),
            e.GetPosition(_surface),
            DataBrush,
            2.4,
            hitTestVisible: false);
        _previewPath.StrokeDashArray = new DoubleCollection { 5, 3 };
        Panel.SetZIndex(_previewPath, 5);
        _surface.Children.Add(_previewPath);
        Mouse.Capture(this, CaptureMode.SubTree);
        e.Handled = true;
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (_linkSource == null || _previewPath == null)
        {
            return;
        }

        var start = GetDataPoint(_nodes[_linkSource.Line.Uid], _linkSource);
        _previewPath.Data = CreateBezierGeometry(start, e.GetPosition(_surface));
        e.Handled = true;
    }

    private void OnCanvasMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_linkSource == null)
        {
            return;
        }

        var source = _linkSource;
        var target = FindPortAt(e.GetPosition(_surface));
        CancelDataLink();
        if (target == null || target.IsOutput || !target.CanConnect || source.Line.Uid == target.Line.Uid)
        {
            return;
        }

        if (!TypesAreCompatible(source.ValueType, target.ValueType))
        {
            ShowCanvasMessage($"Cannot map {source.ValueType} to {target.ValueType}.");
            return;
        }

        CreateVariableMapping(source, target);
        e.Handled = true;
    }

    private void OnCanvasRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_linkSource != null)
        {
            CancelDataLink();
            e.Handled = true;
        }
    }

    private CanvasPortDefinition? FindPortAt(Point point)
    {
        var hit = _surface.InputHitTest(point) as DependencyObject;
        while (hit != null)
        {
            if (hit is FrameworkElement { Tag: PortHandleTag tag })
            {
                return tag.Port;
            }
            hit = VisualTreeHelper.GetParent(hit);
        }
        return null;
    }

    private void CreateVariableMapping(CanvasPortDefinition source, CanvasPortDefinition target)
    {
        if (_vm == null)
        {
            return;
        }

        var variableName = source.ReadBinding().Trim();
        if (!WorkflowVariableNaming.IsVariable(variableName))
        {
            variableName = CreateMappingVariableName(source);
            source.WriteBinding(variableName);
        }

        EnsureVariableDeclaration(variableName, source.ValueType);
        target.WriteBinding(variableName);

        var stored = ReadStoredConnections()
            .Where(connection => !(connection.TargetLineUid == target.Line.Uid
                                   && string.Equals(connection.TargetPort, target.Key, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        stored.Add(new StoredDataConnection(
            source.Line.Uid,
            source.Key,
            target.Line.Uid,
            target.Key,
            variableName));
        SaveStoredConnections(stored);

        Select(target.Line);
        _vm.Owner.MarkProjectChanged();
        ShowCanvasMessage($"Mapped {source.DisplayName} to {target.DisplayName} through {variableName}.");
        Rebuild();
    }

    private void OnDataConnectionRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Path { Tag: ResolvedDataConnection connection })
        {
            return;
        }

        var menu = new ContextMenu
        {
            PlacementTarget = (UIElement)sender
        };
        var title = new MenuItem
        {
            Header = $"{connection.Source.DisplayName} → {connection.Target.DisplayName}",
            IsEnabled = false
        };
        var remove = new MenuItem { Header = "Remove variable mapping" };
        remove.Click += (_, _) => RemoveVariableMapping(connection);
        menu.Items.Add(title);
        menu.Items.Add(new Separator());
        menu.Items.Add(remove);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void RemoveVariableMapping(ResolvedDataConnection connection)
    {
        if (_vm == null)
        {
            return;
        }

        connection.Target.WriteBinding(string.Empty);
        var stored = ReadStoredConnections()
            .Where(item => !(item.TargetLineUid == connection.Target.Line.Uid
                             && string.Equals(item.TargetPort, connection.Target.Key, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        SaveStoredConnections(stored);
        Select(connection.Target.Line);
        _vm.Owner.MarkProjectChanged();
        ShowCanvasMessage($"Removed mapping to {connection.Target.DisplayName}.");
        Rebuild();
    }

    private string CreateMappingVariableName(CanvasPortDefinition source)
    {
        if (_vm == null)
        {
            return "_$CanvasValue";
        }

        var actionName = source.Line.Action?.Name;
        if (string.IsNullOrWhiteSpace(actionName))
        {
            actionName = _vm.Owner.ResolveActionDescriptor(source.Line.Action!)?.DisplayName
                         ?? source.Line.Action?.ActionType
                         ?? "Action";
        }

        var baseName = SanitizeVariableBaseName($"{actionName}{source.DisplayName}");
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "CanvasValue";
        }

        var candidate = WorkflowVariableNaming.LocalDeterminedPrefix + baseName;
        var suffix = 2;
        while (_vm.Method.MethodVariables.Any(variable =>
                   string.Equals(variable.VariableName, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = WorkflowVariableNaming.LocalDeterminedPrefix + baseName + suffix++;
        }
        return candidate;
    }

    private void EnsureVariableDeclaration(string variableName, string valueType)
    {
        if (_vm == null || _vm.Method.MethodVariables.Any(variable =>
                string.Equals(variable.VariableName, variableName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _vm.Method.MethodVariables.Add(new WorkflowVariable
        {
            VariableName = variableName,
            DataType = NormalizeValueType(valueType),
            IsActive = true,
            OrderIndex = _vm.Method.MethodVariables.Count,
            Description = "Created by Canvas variable mapping."
        });
    }

    private IReadOnlyList<StoredDataConnection> ReadStoredConnections()
    {
        if (_vm?.Method.ExtensionData[DataConnectionsKey] is not JsonArray array)
        {
            return Array.Empty<StoredDataConnection>();
        }

        var result = new List<StoredDataConnection>();
        foreach (var item in array.OfType<JsonObject>())
        {
            if (!Guid.TryParse(ReadNodeText(item["sourceLineUid"]), out var sourceLineUid)
                || !Guid.TryParse(ReadNodeText(item["targetLineUid"]), out var targetLineUid))
            {
                continue;
            }

            var sourcePort = ReadNodeText(item["sourcePort"]);
            var targetPort = ReadNodeText(item["targetPort"]);
            if (string.IsNullOrWhiteSpace(sourcePort) || string.IsNullOrWhiteSpace(targetPort))
            {
                continue;
            }

            result.Add(new StoredDataConnection(
                sourceLineUid,
                sourcePort,
                targetLineUid,
                targetPort,
                ReadNodeText(item["variableName"])));
        }
        return result;
    }

    private void SaveStoredConnections(IReadOnlyList<StoredDataConnection> connections)
    {
        if (_vm == null)
        {
            return;
        }

        if (connections.Count == 0)
        {
            _vm.Method.ExtensionData.Remove(DataConnectionsKey);
            return;
        }

        _vm.Method.ExtensionData[DataConnectionsKey] = new JsonArray(connections.Select(connection =>
            (JsonNode?)new JsonObject
            {
                ["sourceLineUid"] = connection.SourceLineUid.ToString(),
                ["sourcePort"] = connection.SourcePort,
                ["targetLineUid"] = connection.TargetLineUid.ToString(),
                ["targetPort"] = connection.TargetPort,
                ["variableName"] = connection.VariableName
            }).ToArray());
    }

    private void CancelDataLink()
    {
        _linkSource = null;
        if (_previewPath != null)
        {
            _surface.Children.Remove(_previewPath);
            _previewPath = null;
        }
        if (Mouse.Captured == this || IsMouseCaptured)
        {
            Mouse.Capture(null);
        }
    }

    private void OnNodeMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_linkSource != null || sender is not Border { Tag: MethodLine line } node)
        {
            return;
        }

        Focus();
        Select(line);
        _dragNode = node;
        _dragLine = line;
        _dragStart = e.GetPosition(_surface);
        _nodeStart = new Point(Canvas.GetLeft(node), Canvas.GetTop(node));
        node.CaptureMouse();
        e.Handled = true;
    }

    private void OnNodeMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragNode == null || _dragLine == null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(_surface);
        var x = Math.Max(16, _nodeStart.X + current.X - _dragStart.X);
        var y = Math.Max(16, _nodeStart.Y + current.Y - _dragStart.Y);
        Canvas.SetLeft(_dragNode, x);
        Canvas.SetTop(_dragNode, y);
        _dragLine.ExtensionData[XKey] = x - WorldOriginX;
        _dragLine.ExtensionData[YKey] = y - WorldOriginY;
        if (_nodes.TryGetValue(_dragLine.Uid, out var info))
        {
            info.X = x;
            info.Y = y;
        }
        DrawConnectionsOnly();
        UpdateSurfaceExtent();
        RenderMiniMap();
        e.Handled = true;
    }

    private void OnNodeMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragNode == null)
        {
            return;
        }

        _dragNode.ReleaseMouseCapture();
        _dragNode = null;
        _dragLine = null;
        _vm?.Owner.MarkProjectChanged();
        e.Handled = true;
    }

    private void Select(MethodLine line)
    {
        if (_vm == null)
        {
            return;
        }

        _vm.Activate();
        _vm.Owner.SelectedMethodLine = line;
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        if (_vm == null)
        {
            return;
        }

        foreach (var pair in _nodes)
        {
            var selected = pair.Key == _vm.Owner.SelectedMethodLine?.Uid;
            var line = pair.Value.Line;
            var accent = GetAccent(line.Action?.ActionType ?? string.Empty);
            pair.Value.Node.BorderBrush = selected ? accent : line.IsActionAvailable ? SoftBorder : WarningBrush;
            pair.Value.Node.BorderThickness = new Thickness(selected ? 2.4 : 1.2);
            pair.Value.Node.Effect = selected
                ? new DropShadowEffect
                {
                    BlurRadius = 18,
                    ShadowDepth = 0,
                    Color = Colors.Black,
                    Opacity = 0.72
                }
                : null;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.StringFormat)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (_vm == null
            || e.Data.GetData(DataFormats.StringFormat) is not string actionType
            || string.IsNullOrWhiteSpace(actionType))
        {
            return;
        }

        var before = _vm.Method.MethodLines.OrderBy(line => line.SequenceNo).LastOrDefault()?.LineNo;
        _vm.DropActionCommand.Execute(new Models.ActionDropRequest(actionType, before.HasValue ? before.Value + 1 : null));
        var dropPoint = e.GetPosition(_surface);
        Dispatcher.BeginInvoke(() =>
        {
            var last = _vm.Method.MethodLines.OrderBy(line => line.SequenceNo).LastOrDefault();
            if (last != null)
            {
                last.ExtensionData[XKey] = dropPoint.X - WorldOriginX - NodeWidth / 2;
                last.ExtensionData[YKey] = dropPoint.Y - WorldOriginY - 60;
            }
            Rebuild();
        });
        e.Handled = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && !_isPanning)
        {
            _scrollViewer.Cursor = Cursors.Hand;
        }

        if (e.Key == Key.Escape && _linkSource != null)
        {
            CancelDataLink();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && _vm?.Owner.DeleteLineCommand.CanExecute(null) == true)
        {
            _vm.Owner.DeleteLineCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && !_isPanning)
        {
            _scrollViewer.Cursor = Cursors.Arrow;
        }
    }

    private void AutoLayout()
    {
        if (_vm == null)
        {
            return;
        }

        var lines = _vm.Method.MethodLines
            .Where(line => line.Action != null)
            .OrderBy(line => line.SequenceNo)
            .ThenBy(line => line.LineNo)
            .ToArray();
        const int columns = 3;
        const double horizontalGap = 76;
        const double verticalGap = 82;
        var y = 70d;
        for (var rowStart = 0; rowStart < lines.Length; rowStart += columns)
        {
            var row = lines.Skip(rowStart).Take(columns).ToArray();
            var rowHeight = row
                .Select(line => _nodes.TryGetValue(line.Uid, out var info) ? info.Height : 180)
                .DefaultIfEmpty(180)
                .Max();
            for (var column = 0; column < row.Length; column++)
            {
                row[column].ExtensionData[XKey] = 80 + column * (NodeWidth + horizontalGap);
                row[column].ExtensionData[YKey] = y;
            }
            y += rowHeight + verticalGap;
        }

        _vm.Owner.MarkProjectChanged();
        _initialViewPending = true;
        Rebuild();
    }

    private void UpdateSurfaceExtent()
    {
        if (_nodes.Count == 0)
        {
            _surface.MinWidth = VirtualCanvasWidth;
            _surface.MinHeight = VirtualCanvasHeight;
            return;
        }

        _surface.MinWidth = Math.Max(
            VirtualCanvasWidth,
            _nodes.Values.Max(node => node.X + NodeWidth) + 600);
        _surface.MinHeight = Math.Max(
            VirtualCanvasHeight,
            _nodes.Values.Max(node => node.Y + node.Height) + 600);
    }

    private Point GetExecutionPoint(NodeVisualInfo node, bool isOutput)
        => new(
            isOutput ? node.X + NodeWidth : node.X,
            node.Y + HeaderHeight + FlowRowHeight / 2);

    private Point GetDataPoint(NodeVisualInfo node, CanvasPortDefinition port)
    {
        var list = port.IsOutput ? node.Outputs : node.Inputs;
        var index = Math.Max(0, list.FindIndex(candidate => candidate.Identity.Equals(port.Identity)));
        return new Point(
            port.IsOutput ? node.X + NodeWidth : node.X,
            node.Y + HeaderHeight + FlowRowHeight + 1 + 5 + index * PortRowHeight + PortRowHeight / 2);
    }

    private static Path CreateBezierPath(
        Point start,
        Point end,
        Brush stroke,
        double thickness,
        bool hitTestVisible)
        => new()
        {
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Data = CreateBezierGeometry(start, end),
            IsHitTestVisible = hitTestVisible,
            SnapsToDevicePixels = true
        };

    private static Geometry CreateBezierGeometry(Point start, Point end)
    {
        var distance = Math.Abs(end.X - start.X);
        var direction = end.X >= start.X ? 1 : -1;
        var bend = Math.Max(70, distance * 0.42);
        var first = new Point(start.X + bend * direction, start.Y);
        var second = new Point(end.X - bend * direction, end.Y);
        return new PathGeometry(new[]
        {
            new PathFigure(start, new PathSegment[]
            {
                new BezierSegment(first, second, end, true)
            }, false)
        });
    }

    private void AddPathBehindNodes(Path path)
    {
        Panel.SetZIndex(path, 1);
        _surface.Children.Insert(0, path);
    }

    private static bool TypesAreCompatible(string source, string target)
    {
        source = NormalizeValueType(source);
        target = NormalizeValueType(target);
        if (source == "object" || target == "object" || source == target)
        {
            return true;
        }
        return (source == "integer" && target == "number")
               || (source == "number" && target == "integer");
    }

    private static string NormalizeValueType(string? type)
        => type?.Trim().ToLowerInvariant() switch
        {
            "bool" or "boolean" => "boolean",
            "int" or "int32" or "int64" or "long" or "integer" => "integer",
            "float" or "double" or "decimal" or "number" => "number",
            "string" => "string",
            "array" or "list" => "array",
            _ => "object"
        };

    private static string InferValueType(JsonNode? value)
        => value switch
        {
            JsonArray => "array",
            JsonObject => "object",
            JsonValue jsonValue when jsonValue.TryGetValue<bool>(out _) => "boolean",
            JsonValue jsonValue when jsonValue.TryGetValue<long>(out _) => "integer",
            JsonValue jsonValue when jsonValue.TryGetValue<double>(out _) => "number",
            _ => "string"
        };

    private static string ReadParameter(WorkflowAction action, string name)
        => action.GetProperty("Parameters") is JsonObject parameters
            ? ReadNodeText(parameters[name])
            : string.Empty;

    private static void WriteParameter(WorkflowAction action, string name, string value)
    {
        var parameters = action.GetProperty("Parameters") is JsonObject current
            ? (JsonObject)current.DeepClone()
            : new JsonObject();
        if (string.IsNullOrWhiteSpace(value))
        {
            parameters.Remove(name);
        }
        else
        {
            parameters[name] = value;
        }

        if (parameters.Count == 0)
        {
            action.RemoveProperty("Parameters");
        }
        else
        {
            action.SetProperty("Parameters", parameters);
        }
    }

    private static string ReadReturnDestination(WorkflowAction action, int index)
    {
        var values = ReadNodeText(action.GetProperty("ReturnVarNames"))
            .Split(',', StringSplitOptions.None);
        return index < values.Length ? values[index].Trim() : string.Empty;
    }

    private static void WriteReturnDestination(WorkflowAction action, int index, int count, string value)
    {
        var current = ReadNodeText(action.GetProperty("ReturnVarNames"))
            .Split(',', StringSplitOptions.None);
        var values = Enumerable.Repeat(string.Empty, Math.Max(count, current.Length)).ToArray();
        for (var currentIndex = 0; currentIndex < current.Length; currentIndex++)
        {
            values[currentIndex] = current[currentIndex].Trim();
        }
        values[index] = value.Trim();
        var last = Array.FindLastIndex(values, item => item.Length > 0);
        if (last < 0)
        {
            action.RemoveProperty("ReturnVarNames");
        }
        else
        {
            action.SetProperty("ReturnVarNames", JsonValue.Create(string.Join(',', values.Take(last + 1))));
        }
    }

    private static bool IsActionType(WorkflowAction action, string type)
        => string.Equals(action.ActionType, type, StringComparison.OrdinalIgnoreCase)
           || string.Equals(action.ActionId, type, StringComparison.OrdinalIgnoreCase);

    private static string ReadNodeText(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return node?.ToJsonString() ?? string.Empty;
        }
        if (value.TryGetValue<string>(out var text))
        {
            return text;
        }
        if (value.TryGetValue<bool>(out var boolean))
        {
            return boolean ? "true" : "false";
        }
        if (value.TryGetValue<long>(out var integer))
        {
            return integer.ToString(CultureInfo.InvariantCulture);
        }
        if (value.TryGetValue<double>(out var number))
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }
        return node.ToJsonString();
    }

    private static string FormatObject(object? value)
        => value switch
        {
            null => string.Empty,
            string text => text,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

    private static double ReadDouble(JsonNode? node, double fallback)
    {
        try
        {
            return node?.GetValue<double>() ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static string SanitizeVariableBaseName(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }
        return builder.ToString();
    }

    private void ShowCanvasMessage(string message)
    {
        if (_vm != null)
        {
            _vm.Owner.SetStatusText(message);
        }
    }

    private static Brush GetAccent(string actionType)
    {
        var color = actionType.Contains("delay", StringComparison.OrdinalIgnoreCase)
            ? Color.FromRgb(229, 139, 35)
            : actionType.Contains("log", StringComparison.OrdinalIgnoreCase)
                ? Color.FromRgb(45, 156, 219)
                : actionType.Contains("script", StringComparison.OrdinalIgnoreCase)
                    ? Color.FromRgb(52, 161, 224)
                    : actionType.Contains("return", StringComparison.OrdinalIgnoreCase)
                        ? Color.FromRgb(221, 79, 71)
                        : actionType.Contains("if", StringComparison.OrdinalIgnoreCase)
                            ? Color.FromRgb(177, 116, 222)
                            : Color.FromRgb(94, 201, 67);
        return new SolidColorBrush(color);
    }

    private static string GetGlyph(string actionType)
        => actionType.Contains("script", StringComparison.OrdinalIgnoreCase) ? "C#"
            : actionType.Contains("delay", StringComparison.OrdinalIgnoreCase) ? "◷"
            : actionType.Contains("log", StringComparison.OrdinalIgnoreCase) ? "▤"
            : actionType.Contains("return", StringComparison.OrdinalIgnoreCase) ? "□"
            : actionType.Contains("if", StringComparison.OrdinalIgnoreCase) ? "◇"
            : "⚡";

    private static string GetShortActionType(string actionType)
    {
        if (string.IsNullOrWhiteSpace(actionType))
        {
            return "Action";
        }
        var separator = actionType.LastIndexOf(':');
        return separator >= 0 && separator + 1 < actionType.Length
            ? actionType[..separator]
            : actionType;
    }

    private static SolidColorBrush BrushFromRgb(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private readonly record struct PortIdentity(Guid LineUid, string PortKey, bool IsOutput);

    private sealed class CanvasPortDefinition
    {
        private readonly Func<string> _readBinding;
        private readonly Action<string> _writeBinding;
        private readonly string _defaultText;

        public CanvasPortDefinition(
            MethodLine line,
            string key,
            string displayName,
            string valueType,
            bool isOutput,
            bool canConnect,
            int order,
            Func<string> readBinding,
            Action<string> writeBinding,
            string defaultText)
        {
            Line = line;
            Key = key;
            DisplayName = displayName;
            ValueType = NormalizeValueType(valueType);
            IsOutput = isOutput;
            CanConnect = canConnect;
            Order = order;
            _readBinding = readBinding;
            _writeBinding = writeBinding;
            _defaultText = defaultText;
        }

        public MethodLine Line { get; }
        public string Key { get; }
        public string DisplayName { get; }
        public string ValueType { get; }
        public bool IsOutput { get; }
        public bool CanConnect { get; }
        public int Order { get; }
        public PortIdentity Identity => new(Line.Uid, Key, IsOutput);
        public string ReadBinding() => _readBinding() ?? string.Empty;
        public void WriteBinding(string value) => _writeBinding(value);

        public string GetDisplayText()
        {
            var binding = ReadBinding().Trim();
            if (IsOutput)
            {
                return string.IsNullOrWhiteSpace(binding)
                    ? $"{DisplayName}  ·  not mapped"
                    : $"{DisplayName}  →  {binding}";
            }

            if (string.IsNullOrWhiteSpace(binding))
            {
                return string.IsNullOrWhiteSpace(_defaultText)
                    ? $"{DisplayName}  ·  not configured"
                    : $"{DisplayName}  =  {TrimForDisplay(_defaultText)}";
            }

            return WorkflowVariableNaming.IsVariable(binding)
                ? $"{DisplayName}  ←  {binding}"
                : $"{DisplayName}  =  {TrimForDisplay(binding)}";
        }

        public string GetTooltipBindingText()
        {
            var binding = ReadBinding().Trim();
            if (string.IsNullOrWhiteSpace(binding))
            {
                return string.IsNullOrWhiteSpace(_defaultText)
                    ? (IsOutput ? "No output variable is assigned." : "No value is configured.")
                    : $"Default value: {_defaultText}";
            }
            return IsOutput
                ? $"Output variable: {binding}"
                : WorkflowVariableNaming.IsVariable(binding)
                    ? $"Input variable: {binding}"
                    : $"Literal/expression: {binding}";
        }

        private static string TrimForDisplay(string value)
            => value.Length <= 24 ? value : value[..21] + "…";
    }

    private sealed class NodeVisualInfo
    {
        public NodeVisualInfo(
            MethodLine line,
            Border node,
            IReadOnlyList<CanvasPortDefinition> inputs,
            IReadOnlyList<CanvasPortDefinition> outputs,
            double x,
            double y,
            double height)
        {
            Line = line;
            Node = node;
            Inputs = inputs.ToList();
            Outputs = outputs.ToList();
            X = x;
            Y = y;
            Height = height;
        }

        public MethodLine Line { get; }
        public Border Node { get; }
        public List<CanvasPortDefinition> Inputs { get; }
        public List<CanvasPortDefinition> Outputs { get; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Height { get; }
    }

    private sealed record PortHandleTag(CanvasPortDefinition Port);

    private sealed record StoredDataConnection(
        Guid SourceLineUid,
        string SourcePort,
        Guid TargetLineUid,
        string TargetPort,
        string VariableName);

    private sealed record ResolvedDataConnection(
        CanvasPortDefinition Source,
        CanvasPortDefinition Target,
        string VariableName);
}

internal static class CanvasPortListExtensions
{
    public static int FindIndex<T>(this IReadOnlyList<T> source, Predicate<T> match)
    {
        for (var index = 0; index < source.Count; index++)
        {
            if (match(source[index]))
            {
                return index;
            }
        }
        return -1;
    }
}
