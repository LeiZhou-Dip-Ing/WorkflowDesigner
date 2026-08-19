using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.Views;

public partial class ProtectedWorkflowPresentationWindow : Window
{
    private const double NodeWidth = 250;
    private const double NodeHeight = 82;
    private readonly WorkflowPresentationResponse _presentation;
    private readonly ScaleTransform _zoom = new(1, 1);

    public ProtectedWorkflowPresentationWindow(WorkflowPresentationResponse presentation)
    {
        _presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        InitializeComponent();
        ProjectNameText.Text = presentation.ProjectName;
        MethodSelector.ItemsSource = presentation.Methods;
        DiagramCanvas.LayoutTransform = _zoom;
        StatusText.Text = $"{presentation.WorkflowId}  |  revision {presentation.Revision}";
        if (presentation.Methods.Count > 0)
        {
            MethodSelector.SelectedIndex = 0;
        }
    }

    private void OnMethodChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MethodSelector.SelectedItem is WorkflowPresentationMethodDto method)
        {
            Render(method);
        }
    }

    private void OnZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_zoom == null)
        {
            return;
        }

        _zoom.ScaleX = e.NewValue;
        _zoom.ScaleY = e.NewValue;
    }

    private void Render(WorkflowPresentationMethodDto method)
    {
        DiagramCanvas.Children.Clear();
        var positions = new Dictionary<Guid, Point>();
        var ordered = method.Nodes.OrderBy(node => node.SequenceNumber).ThenBy(node => node.LineNumber).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var node = ordered[index];
            var x = node.CanvasX ?? 70 + (index % 4) * 310;
            var y = node.CanvasY ?? 70 + (index / 4) * 150;
            positions[node.Uid] = new Point(Math.Max(30, x), Math.Max(30, y));
        }

        for (var index = 0; index + 1 < ordered.Length; index++)
        {
            DrawConnection(positions[ordered[index].Uid], positions[ordered[index + 1].Uid]);
        }

        foreach (var node in ordered)
        {
            DrawNode(node, positions[node.Uid]);
        }

        var width = positions.Count == 0 ? 1000 : positions.Values.Max(point => point.X) + NodeWidth + 100;
        var height = positions.Count == 0 ? 700 : positions.Values.Max(point => point.Y) + NodeHeight + 100;
        DiagramCanvas.Width = Math.Max(1000, width);
        DiagramCanvas.Height = Math.Max(700, height);
        StatusText.Text = $"{_presentation.WorkflowId}  |  revision {_presentation.Revision}  |  {ordered.Length} protected steps";
    }

    private void DrawConnection(Point source, Point target)
    {
        var start = new Point(source.X + NodeWidth, source.Y + NodeHeight / 2);
        var end = new Point(target.X, target.Y + NodeHeight / 2);
        var offset = Math.Max(50, Math.Abs(end.X - start.X) / 2);
        var figure = new PathFigure { StartPoint = start };
        figure.Segments.Add(new BezierSegment(
            new Point(start.X + offset, start.Y),
            new Point(end.X - offset, end.Y),
            end,
            true));
        DiagramCanvas.Children.Add(new Path
        {
            Data = new PathGeometry([figure]),
            Stroke = new SolidColorBrush(Color.FromRgb(152, 164, 175)),
            StrokeThickness = 2,
            IsHitTestVisible = false
        });
    }

    private void DrawNode(WorkflowPresentationNodeDto node, Point position)
    {
        var title = string.IsNullOrWhiteSpace(node.DisplayName) ? "Protected step" : node.DisplayName;
        var border = new Border
        {
            Width = NodeWidth,
            Height = NodeHeight,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromRgb(38, 41, 45)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(76, 83, 91)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 9, 12, 8),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = $"{node.LineNumber}  {title}",
                        Foreground = new SolidColorBrush(Color.FromRgb(239, 242, 245)),
                        FontWeight = FontWeights.SemiBold,
                        FontSize = 13,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    },
                    new TextBlock
                    {
                        Text = node.ActionType,
                        Foreground = new SolidColorBrush(Color.FromRgb(150, 173, 193)),
                        FontSize = 11,
                        Margin = new Thickness(0, 8, 0, 0),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    },
                    new TextBlock
                    {
                        Text = $"Level {node.NestingLevel}",
                        Foreground = new SolidColorBrush(Color.FromRgb(145, 151, 158)),
                        FontSize = 10,
                        Margin = new Thickness(0, 3, 0, 0)
                    }
                }
            }
        };
        Canvas.SetLeft(border, position.X);
        Canvas.SetTop(border, position.Y);
        DiagramCanvas.Children.Add(border);
    }
}
