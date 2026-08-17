using System.Windows;
using ICSharpCode.AvalonEdit;

namespace WorkflowCore.WpfDemo.Controls;

/// <summary>Two-way text bridge for AvalonEdit, whose Text property is not a dependency property.</summary>
public static class AvalonEditTextBinding
{
    public static readonly DependencyProperty BoundTextProperty = DependencyProperty.RegisterAttached(
        "BoundText",
        typeof(string),
        typeof(AvalonEditTextBinding),
        new FrameworkPropertyMetadata(
            string.Empty,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnBoundTextChanged));

    public static string GetBoundText(DependencyObject obj) => (string)obj.GetValue(BoundTextProperty);

    public static void SetBoundText(DependencyObject obj, string value) => obj.SetValue(BoundTextProperty, value);

    private static void OnBoundTextChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not TextEditor editor)
        {
            return;
        }

        var value = args.NewValue as string ?? string.Empty;
        if (!string.Equals(editor.Text, value, StringComparison.Ordinal))
        {
            editor.Text = value;
        }

        editor.TextChanged -= EditorOnTextChanged;
        editor.TextChanged += EditorOnTextChanged;
    }

    private static void EditorOnTextChanged(object? sender, EventArgs args)
    {
        if (sender is TextEditor editor)
        {
            SetBoundText(editor, editor.Text);
        }
    }
}
