using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using WorkflowCore.WpfDemo.Controls;
using WorkflowCore.WpfDemo.Theming;
using WorkflowCore.WpfDemo.ViewModels;

namespace WorkflowCore.WpfDemo.Views;

public partial class ComparisonTextView : UserControl
{
    private const int ContextLineCount = 2;
    private bool _synchronizingScroll;
    private int _differenceIndex = -1;
    private IReadOnlyList<int> _differenceLines = Array.Empty<int>();
    private DeploymentComparisonViewModel? _viewModel;

    public ComparisonTextView()
    {
        InitializeComponent();
        ConfigureEditor(LocalEditor);
        ConfigureEditor(RuntimeEditor);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        DataContextChanged += OnDataContextChanged;
        WorkflowThemeContext.Changed += ThemeChanged;
        AttachViewModel(DataContext as DeploymentComparisonViewModel);
        LocalEditor.TextArea.TextView.ScrollOffsetChanged += LocalScrollOffsetChanged;
        RuntimeEditor.TextArea.TextView.ScrollOffsetChanged += RuntimeScrollOffsetChanged;
        ApplyTheme();
        Rebuild();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DataContextChanged -= OnDataContextChanged;
        WorkflowThemeContext.Changed -= ThemeChanged;
        AttachViewModel(null);
        LocalEditor.TextArea.TextView.ScrollOffsetChanged -= LocalScrollOffsetChanged;
        RuntimeEditor.TextArea.TextView.ScrollOffsetChanged -= RuntimeScrollOffsetChanged;
    }

    private void ThemeChanged(object? sender, EventArgs e) => ApplyTheme();
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) { AttachViewModel(e.NewValue as DeploymentComparisonViewModel); Rebuild(); }

    private void AttachViewModel(DeploymentComparisonViewModel? value)
    {
        if (_viewModel != null) _viewModel.PropertyChanged -= ViewModelPropertyChanged;
        _viewModel = value;
        if (_viewModel != null) _viewModel.PropertyChanged += ViewModelPropertyChanged;
    }

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DeploymentComparisonViewModel.SelectedScript)
            or nameof(DeploymentComparisonViewModel.IgnoreWhitespace)
            or nameof(DeploymentComparisonViewModel.IgnoreLineEndings)) { Rebuild(); return; }
        if (e.PropertyName != nameof(DeploymentComparisonViewModel.NavigationRequest)) return;
        if (!string.IsNullOrWhiteSpace(_viewModel?.SearchText)) FindNext(_viewModel.SearchText);
        else NavigateDifference(_viewModel?.NavigationDirection ?? 1);
    }

    private void Rebuild()
    {
        if (_viewModel?.SelectedScript == null) { LocalEditor.Text = string.Empty; RuntimeEditor.Text = string.Empty; return; }
        var diff = new SideBySideDiffBuilder(new Differ()).BuildDiffModel(
            Normalize(_viewModel.SelectedScript.LocalText, _viewModel.IgnoreLineEndings),
            Normalize(_viewModel.SelectedScript.RuntimeText, _viewModel.IgnoreLineEndings),
            _viewModel.IgnoreWhitespace, false);
        var excerpt = BuildExcerpt(diff.OldText.Lines, diff.NewText.Lines);
        LocalEditor.Text = string.Join(Environment.NewLine, excerpt.LocalLines);
        RuntimeEditor.Text = string.Join(Environment.NewLine, excerpt.RuntimeLines);
        ApplyDiffColors(LocalEditor, excerpt.LocalChanges);
        ApplyDiffColors(RuntimeEditor, excerpt.RuntimeChanges);
        _differenceLines = excerpt.DifferenceLines;
        _differenceIndex = -1;
    }

    private void ApplyTheme()
    {
        foreach (var editor in new[] { LocalEditor, RuntimeEditor })
        {
            editor.Background = FindBrush("AppEditorBrush", WorkflowThemeContext.IsDark ? Color.FromRgb(30, 30, 30) : Colors.White);
            editor.Foreground = FindBrush("AppTextBrush", WorkflowThemeContext.IsDark ? Color.FromRgb(212, 212, 212) : Color.FromRgb(30, 30, 30));
            editor.LineNumbersForeground = FindBrush("AppMutedTextBrush", Color.FromRgb(120, 128, 136));
            editor.TextArea.SelectionBrush = FindBrush("AppDataGridSelectedRowBrush", Color.FromRgb(38, 79, 120));
            editor.TextArea.SelectionForeground = editor.Foreground;
            editor.TextArea.Caret.CaretBrush = editor.Foreground;
            editor.TextArea.TextView.LineTransformers.RemoveAll(item => item is VisualStudioCSharpColorizer);
            editor.TextArea.TextView.LineTransformers.Add(new VisualStudioCSharpColorizer(WorkflowThemeContext.IsDark));
            editor.TextArea.TextView.Redraw();
        }
    }

    private static void ConfigureEditor(TextEditor editor) => editor.Options.HighlightCurrentLine = true;

    private static void ApplyDiffColors(TextEditor editor, IReadOnlyList<ChangeType> changes)
    {
        editor.TextArea.TextView.LineTransformers.RemoveAll(item => item is DiffLineColorizer);
        editor.TextArea.TextView.LineTransformers.Insert(0, new DiffLineColorizer(changes, WorkflowThemeContext.IsDark));
        editor.TextArea.TextView.Redraw();
    }

    private static DiffExcerpt BuildExcerpt(
        IReadOnlyList<DiffPiece> localLines,
        IReadOnlyList<DiffPiece> runtimeLines)
    {
        var count = Math.Min(localLines.Count, runtimeLines.Count);
        var include = new bool[count];
        var localNumbers = new int?[count];
        var runtimeNumbers = new int?[count];
        var localNumber = 0;
        var runtimeNumber = 0;

        for (var index = 0; index < count; index++)
        {
            if (localLines[index].Type != ChangeType.Imaginary) localNumbers[index] = ++localNumber;
            if (runtimeLines[index].Type != ChangeType.Imaginary) runtimeNumbers[index] = ++runtimeNumber;
            if (localLines[index].Type == ChangeType.Unchanged && runtimeLines[index].Type == ChangeType.Unchanged) continue;

            var start = Math.Max(0, index - ContextLineCount);
            var end = Math.Min(count - 1, index + ContextLineCount);
            for (var contextIndex = start; contextIndex <= end; contextIndex++) include[contextIndex] = true;
        }

        var localExcerpt = new List<string>();
        var runtimeExcerpt = new List<string>();
        var localChanges = new List<ChangeType>();
        var runtimeChanges = new List<ChangeType>();
        var differenceLines = new List<int>();
        var previousSourceIndex = -2;

        for (var index = 0; index < count; index++)
        {
            if (!include[index]) continue;
            if (index > previousSourceIndex + 1)
            {
                localExcerpt.Add("     ···");
                runtimeExcerpt.Add("     ···");
                localChanges.Add(ChangeType.Unchanged);
                runtimeChanges.Add(ChangeType.Unchanged);
            }

            localExcerpt.Add(FormatExcerptLine(localNumbers[index], localLines[index]));
            runtimeExcerpt.Add(FormatExcerptLine(runtimeNumbers[index], runtimeLines[index]));
            localChanges.Add(localLines[index].Type);
            runtimeChanges.Add(runtimeLines[index].Type);
            if (localLines[index].Type != ChangeType.Unchanged || runtimeLines[index].Type != ChangeType.Unchanged)
                differenceLines.Add(localExcerpt.Count);
            previousSourceIndex = index;
        }

        if (localExcerpt.Count == 0)
        {
            localExcerpt.Add("No changed lines.");
            runtimeExcerpt.Add("No changed lines.");
            localChanges.Add(ChangeType.Unchanged);
            runtimeChanges.Add(ChangeType.Unchanged);
        }

        return new DiffExcerpt(localExcerpt, runtimeExcerpt, localChanges, runtimeChanges, differenceLines);
    }

    private static string FormatExcerptLine(int? lineNumber, DiffPiece piece)
        => lineNumber.HasValue
            ? $"{lineNumber.Value,4} │ {piece.Text}"
            : "     │ ";

    private void NavigateDifference(int direction)
    {
        if (_differenceLines.Count == 0) return;
        _differenceIndex = (_differenceIndex + direction + _differenceLines.Count) % _differenceLines.Count;
        LocalEditor.ScrollToLine(_differenceLines[_differenceIndex]); RuntimeEditor.ScrollToLine(_differenceLines[_differenceIndex]);
    }

    private void FindNext(string search)
    {
        var localIndex = LocalEditor.Text.IndexOf(search, Math.Max(0, LocalEditor.SelectionStart + LocalEditor.SelectionLength), StringComparison.OrdinalIgnoreCase);
        if (localIndex >= 0) { LocalEditor.Select(localIndex, search.Length); LocalEditor.ScrollToLine(LocalEditor.Document.GetLineByOffset(localIndex).LineNumber); return; }
        var runtimeIndex = RuntimeEditor.Text.IndexOf(search, 0, StringComparison.OrdinalIgnoreCase);
        if (runtimeIndex >= 0) { RuntimeEditor.Select(runtimeIndex, search.Length); RuntimeEditor.ScrollToLine(RuntimeEditor.Document.GetLineByOffset(runtimeIndex).LineNumber); }
    }

    private void LocalScrollOffsetChanged(object? sender, EventArgs e) => SyncScroll(LocalEditor, RuntimeEditor);
    private void RuntimeScrollOffsetChanged(object? sender, EventArgs e) => SyncScroll(RuntimeEditor, LocalEditor);
    private void SyncScroll(TextEditor source, TextEditor target)
    {
        if (_synchronizingScroll) return;
        _synchronizingScroll = true;
        target.ScrollToVerticalOffset(source.VerticalOffset); target.ScrollToHorizontalOffset(source.HorizontalOffset);
        _synchronizingScroll = false;
    }

    private Brush FindBrush(string key, Color fallback) => TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);
    private static string Normalize(string text, bool ignoreLineEndings)
        => ignoreLineEndings ? text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n') : text;

    private sealed class DiffLineColorizer(IReadOnlyList<ChangeType> changes, bool isDark) : DocumentColorizingTransformer
    {
        protected override void ColorizeLine(DocumentLine line)
        {
            if (line.LineNumber < 1 || line.LineNumber > changes.Count) return;
            var color = changes[line.LineNumber - 1] switch
            {
                ChangeType.Inserted => isDark ? Color.FromRgb(31, 62, 43) : Color.FromRgb(226, 247, 232),
                ChangeType.Deleted => isDark ? Color.FromRgb(64, 37, 42) : Color.FromRgb(255, 232, 232),
                ChangeType.Modified => isDark ? Color.FromRgb(68, 55, 31) : Color.FromRgb(255, 246, 218),
                ChangeType.Imaginary => isDark ? Color.FromRgb(39, 39, 39) : Color.FromRgb(245, 247, 249),
                _ => Colors.Transparent
            };
            if (color != Colors.Transparent)
                ChangeLinePart(line.Offset, line.EndOffset, element => element.TextRunProperties.SetBackgroundBrush(new SolidColorBrush(color)));
        }
    }

    private sealed record DiffExcerpt(
        IReadOnlyList<string> LocalLines,
        IReadOnlyList<string> RuntimeLines,
        IReadOnlyList<ChangeType> LocalChanges,
        IReadOnlyList<ChangeType> RuntimeChanges,
        IReadOnlyList<int> DifferenceLines);
}
