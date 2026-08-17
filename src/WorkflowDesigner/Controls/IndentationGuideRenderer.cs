using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;

namespace WorkflowCore.WpfDemo.Controls;

public sealed class IndentationGuideRenderer : IBackgroundRenderer
{
    private const int IndentSize = 4;
    private readonly Pen _pen;

    public IndentationGuideRenderer()
    {
        var brush = new SolidColorBrush(Color.FromRgb(63, 63, 70));
        brush.Freeze();
        _pen = new Pen(brush, 1);
        _pen.Freeze();
    }

    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (textView.Document == null)
        {
            return;
        }

        textView.EnsureVisualLines();
        foreach (var visualLine in textView.VisualLines)
        {
            var line = visualLine.FirstDocumentLine;
            if (line == null || line.Length == 0)
            {
                continue;
            }

            var text = textView.Document.GetText(line.Offset, line.Length);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var indentation = CountIndentColumns(text) / IndentSize;
            for (var level = 1; level <= indentation; level++)
            {
                var point = visualLine.GetVisualPosition(level * IndentSize, VisualYPosition.TextTop);
                var x = Math.Round(point.X - textView.HorizontalOffset) + 0.5;
                var top = visualLine.VisualTop - textView.VerticalOffset;
                drawingContext.DrawLine(_pen, new Point(x, top), new Point(x, top + visualLine.Height));
            }
        }
    }

    private static int CountIndentColumns(string text)
    {
        var columns = 0;
        foreach (var character in text)
        {
            if (character == ' ') columns++;
            else if (character == '\t') columns += IndentSize;
            else break;
        }

        return columns;
    }
}
