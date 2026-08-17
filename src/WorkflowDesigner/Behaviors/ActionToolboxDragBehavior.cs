using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WorkflowCore.WpfDemo.Models;

namespace WorkflowCore.WpfDemo.Behaviors;

public static class ActionToolboxDragBehavior
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(ActionToolboxDragBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty DragCompletedCommandProperty = DependencyProperty.RegisterAttached(
        "DragCompletedCommand",
        typeof(ICommand),
        typeof(ActionToolboxDragBehavior));

    public static readonly DependencyProperty ItemClickCommandProperty = DependencyProperty.RegisterAttached(
        "ItemClickCommand",
        typeof(ICommand),
        typeof(ActionToolboxDragBehavior));

    public static readonly DependencyProperty ItemDoubleClickCommandProperty = DependencyProperty.RegisterAttached(
        "ItemDoubleClickCommand",
        typeof(ICommand),
        typeof(ActionToolboxDragBehavior));

    private static readonly DependencyProperty DragStartPointProperty = DependencyProperty.RegisterAttached(
        "DragStartPoint",
        typeof(Point),
        typeof(ActionToolboxDragBehavior));

    private static readonly DependencyProperty DragItemProperty = DependencyProperty.RegisterAttached(
        "DragItem",
        typeof(ActionTemplateItem),
        typeof(ActionToolboxDragBehavior));

    private static readonly DependencyProperty WasDraggingProperty = DependencyProperty.RegisterAttached(
        "WasDragging",
        typeof(bool),
        typeof(ActionToolboxDragBehavior));

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetDragCompletedCommand(DependencyObject element, ICommand? value) =>
        element.SetValue(DragCompletedCommandProperty, value);

    public static ICommand? GetDragCompletedCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(DragCompletedCommandProperty);

    public static void SetItemClickCommand(DependencyObject element, ICommand? value) =>
        element.SetValue(ItemClickCommandProperty, value);

    public static ICommand? GetItemClickCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(ItemClickCommandProperty);

    public static void SetItemDoubleClickCommand(DependencyObject element, ICommand? value) =>
        element.SetValue(ItemDoubleClickCommandProperty, value);

    public static ICommand? GetItemDoubleClickCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(ItemDoubleClickCommandProperty);

    private static void OnIsEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TreeView treeView)
        {
            return;
        }

        if ((bool)e.OldValue)
        {
            treeView.PreviewMouseLeftButtonDown -= TreeView_OnPreviewMouseLeftButtonDown;
            treeView.PreviewMouseMove -= TreeView_OnPreviewMouseMove;
            treeView.PreviewMouseLeftButtonUp -= TreeView_OnPreviewMouseLeftButtonUp;
            treeView.MouseDoubleClick -= TreeView_OnMouseDoubleClick;
        }

        if ((bool)e.NewValue)
        {
            treeView.PreviewMouseLeftButtonDown += TreeView_OnPreviewMouseLeftButtonDown;
            treeView.PreviewMouseMove += TreeView_OnPreviewMouseMove;
            treeView.PreviewMouseLeftButtonUp += TreeView_OnPreviewMouseLeftButtonUp;
            treeView.MouseDoubleClick += TreeView_OnMouseDoubleClick;
        }
    }

    private static void TreeView_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TreeView treeView)
        {
            return;
        }

        treeView.SetValue(DragStartPointProperty, e.GetPosition(treeView));
        treeView.SetValue(DragItemProperty, FindActionItem(e.OriginalSource as DependencyObject));
        treeView.SetValue(WasDraggingProperty, false);
    }

    private static void TreeView_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not TreeView treeView || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (treeView.GetValue(DragItemProperty) is not ActionTemplateItem item
            || item.IsCategory
            || string.IsNullOrWhiteSpace(item.ActionType))
        {
            return;
        }

        var startPoint = (Point)treeView.GetValue(DragStartPointProperty);
        var currentPoint = e.GetPosition(treeView);
        if (Math.Abs(currentPoint.X - startPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(currentPoint.Y - startPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        treeView.SetValue(WasDraggingProperty, true);

        var data = new DataObject();
        data.SetData(DataFormats.StringFormat, item.ActionType);
        DragDrop.DoDragDrop(treeView, data, DragDropEffects.Copy);

        var command = GetDragCompletedCommand(treeView);
        if (command?.CanExecute(item) == true)
        {
            command.Execute(item);
        }

        e.Handled = true;
    }

    private static void TreeView_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TreeView treeView)
        {
            return;
        }

        if ((bool)treeView.GetValue(WasDraggingProperty))
        {
            treeView.SetValue(WasDraggingProperty, false);
            treeView.ClearValue(DragItemProperty);
            e.Handled = true;
            return;
        }

        if (treeView.GetValue(DragItemProperty) is not ActionTemplateItem item
            || item.IsCategory
            || string.IsNullOrWhiteSpace(item.ActionType))
        {
            return;
        }

        treeView.ClearValue(DragItemProperty);
        var command = GetItemClickCommand(treeView);
        if (command?.CanExecute(item.ActionType) == true)
        {
            command.Execute(item.ActionType);
            e.Handled = true;
        }
    }

    private static void TreeView_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TreeView treeView
            || FindActionItem(e.OriginalSource as DependencyObject) is not { IsCategory: false } item
            || string.IsNullOrWhiteSpace(item.ActionType))
        {
            return;
        }

        var command = GetItemDoubleClickCommand(treeView);
        if (command?.CanExecute(item.ActionType) == true)
        {
            command.Execute(item.ActionType);
            e.Handled = true;
        }
    }

    private static ActionTemplateItem? FindActionItem(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is TreeViewItem { DataContext: ActionTemplateItem item })
            {
                return item;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }
}
