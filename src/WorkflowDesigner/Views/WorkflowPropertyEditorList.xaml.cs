using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace WorkflowCore.WpfDemo.Views;

public partial class WorkflowPropertyEditorList : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(WorkflowPropertyEditorList),
        new PropertyMetadata(null));

    public static readonly DependencyProperty OwnerProperty = DependencyProperty.Register(
        nameof(Owner),
        typeof(object),
        typeof(WorkflowPropertyEditorList),
        new PropertyMetadata(null));

    public WorkflowPropertyEditorList()
    {
        InitializeComponent();
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public object? Owner
    {
        get => GetValue(OwnerProperty);
        set => SetValue(OwnerProperty, value);
    }
}
