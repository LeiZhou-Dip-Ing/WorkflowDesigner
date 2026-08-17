using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WorkflowDesigner.WpfSdk;

namespace WorkflowCore.WpfDemo.Controls;

public partial class ActionPropertyPanel : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(ActionPropertyPanel));

    public static readonly DependencyProperty CreateValueCommandProperty = DependencyProperty.Register(
        nameof(CreateValueCommand), typeof(ICommand), typeof(ActionPropertyPanel));

    public static readonly DependencyProperty ClearValueCommandProperty = DependencyProperty.Register(
        nameof(ClearValueCommand), typeof(ICommand), typeof(ActionPropertyPanel));

    public static readonly DependencyProperty ActionContextProperty = DependencyProperty.Register(
        nameof(ActionContext), typeof(IWorkflowDesignerActionContext), typeof(ActionPropertyPanel));

    public ActionPropertyPanel()
    {
        InitializeComponent();
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public ICommand? CreateValueCommand
    {
        get => (ICommand?)GetValue(CreateValueCommandProperty);
        set => SetValue(CreateValueCommandProperty, value);
    }

    public ICommand? ClearValueCommand
    {
        get => (ICommand?)GetValue(ClearValueCommandProperty);
        set => SetValue(ClearValueCommandProperty, value);
    }

    public IWorkflowDesignerActionContext? ActionContext
    {
        get => (IWorkflowDesignerActionContext?)GetValue(ActionContextProperty);
        set => SetValue(ActionContextProperty, value);
    }
}
