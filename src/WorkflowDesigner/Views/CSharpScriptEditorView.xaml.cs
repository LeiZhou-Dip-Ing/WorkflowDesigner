using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;
using WorkflowCore.WpfDemo.Controls;
using WorkflowCore.WpfDemo.Models;
using WorkflowCore.WpfDemo.ViewModels;

namespace WorkflowCore.WpfDemo.Views;

public partial class CSharpScriptEditorView : UserControl
{
    private bool _configured;
    private bool _diagnosticsExpanded = true;

    public CSharpScriptEditorView()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (!_configured)
        {
            ConfigureEditor();
            _configured = true;
        }

        ScriptEditor.TextArea.Caret.PositionChanged -= CaretOnPositionChanged;
        ScriptEditor.TextArea.Caret.PositionChanged += CaretOnPositionChanged;
        UpdateCaretPosition();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
        => ScriptEditor.TextArea.Caret.PositionChanged -= CaretOnPositionChanged;

    private void CaretOnPositionChanged(object? sender, EventArgs args)
    {
        UpdateCaretPosition();
        ScriptEditor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    private void UpdateCaretPosition()
    {
        if (DataContext is CSharpScriptEditorViewModel viewModel)
        {
            viewModel.CaretLine = ScriptEditor.TextArea.Caret.Line;
            viewModel.CaretColumn = ScriptEditor.TextArea.Caret.Column;
        }
    }

    private void DiagnosticsGridOnMouseDoubleClick(object sender, MouseButtonEventArgs args)
    {
        if (sender is not DataGrid { SelectedItem: SharpScriptDiagnosticItem diagnostic })
        {
            return;
        }

        if (DataContext is CSharpScriptEditorViewModel viewModel)
        {
            viewModel.SelectDiagnosticCommand.Execute(diagnostic);
        }

        var line = Math.Max(1, diagnostic.Line);
        var column = Math.Max(1, diagnostic.Column);
        ScriptEditor.ScrollTo(line, column);
        ScriptEditor.TextArea.Caret.Line = line;
        ScriptEditor.TextArea.Caret.Column = column;
        ScriptEditor.Focus();
    }

    private void ToggleDiagnosticsPanelOnClick(object sender, RoutedEventArgs args)
    {
        _diagnosticsExpanded = !_diagnosticsExpanded;
        DiagnosticsRow.Height = new GridLength(_diagnosticsExpanded ? 190 : 38);
        if (sender is Button button)
        {
            button.Content = _diagnosticsExpanded ? "⌃" : "⌄";
            button.ToolTip = _diagnosticsExpanded ? "Collapse diagnostics" : "Expand diagnostics";
        }
    }

    private void CloseDiagnosticsPanelOnClick(object sender, RoutedEventArgs args)
    {
        DiagnosticsPanel.Visibility = Visibility.Collapsed;
        DiagnosticsRow.Height = new GridLength(0);
    }

    private void OpenDiagnosticsPanelOnClick(object sender, RoutedEventArgs args)
    {
        DiagnosticsPanel.Visibility = Visibility.Visible;
        _diagnosticsExpanded = true;
        DiagnosticsRow.Height = new GridLength(190);
    }

    private void ConfigureEditor()
    {
        ScriptEditor.Options.HighlightCurrentLine = true;
        ScriptEditor.Options.ConvertTabsToSpaces = true;
        ScriptEditor.Options.IndentationSize = 4;
        ScriptEditor.Options.ShowTabs = false;
        ScriptEditor.Options.ShowSpaces = false;
        ScriptEditor.Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
        ScriptEditor.Foreground = new SolidColorBrush(Color.FromRgb(212, 212, 212));
        ScriptEditor.LineNumbersForeground = new SolidColorBrush(Color.FromRgb(133, 133, 133));
        ScriptEditor.TextArea.SelectionBrush = new SolidColorBrush(Color.FromArgb(120, 38, 79, 120));
        ScriptEditor.TextArea.SelectionForeground = Brushes.White;
        ScriptEditor.TextArea.Caret.CaretBrush = Brushes.White;
        ScriptEditor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(Color.FromRgb(37, 37, 38));
        ScriptEditor.TextArea.TextView.CurrentLineBorder = new Pen(new SolidColorBrush(Color.FromRgb(51, 51, 51)), 1);
        ScriptEditor.TextArea.TextView.LinkTextForegroundBrush = new SolidColorBrush(Color.FromRgb(86, 156, 214));
        if (!ScriptEditor.TextArea.TextView.LineTransformers.OfType<VisualStudioCSharpColorizer>().Any())
        {
            ScriptEditor.TextArea.TextView.LineTransformers.Add(new VisualStudioCSharpColorizer());
        }

        if (!ScriptEditor.TextArea.TextView.BackgroundRenderers.OfType<IndentationGuideRenderer>().Any())
        {
            ScriptEditor.TextArea.TextView.BackgroundRenderers.Add(new IndentationGuideRenderer());
        }

    }
}
