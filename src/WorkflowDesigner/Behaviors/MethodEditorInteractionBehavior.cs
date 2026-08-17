using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WorkflowCore.WpfDemo.Models;

namespace WorkflowCore.WpfDemo.Behaviors;

public static class MethodEditorInteractionBehavior
{
    public static readonly DependencyProperty LoadedCommandProperty = DependencyProperty.RegisterAttached(
        "LoadedCommand",
        typeof(ICommand),
        typeof(MethodEditorInteractionBehavior),
        new PropertyMetadata(null, OnLoadedCommandChanged));

    public static readonly DependencyProperty DropActionCommandProperty = DependencyProperty.RegisterAttached(
        "DropActionCommand",
        typeof(ICommand),
        typeof(MethodEditorInteractionBehavior),
        new PropertyMetadata(null, OnDropActionCommandChanged));

    public static readonly DependencyProperty ItemDoubleClickCommandProperty = DependencyProperty.RegisterAttached(
        "ItemDoubleClickCommand",
        typeof(ICommand),
        typeof(MethodEditorInteractionBehavior),
        new PropertyMetadata(null, OnItemDoubleClickCommandChanged));

    public static readonly DependencyProperty CommitEditCommandProperty = DependencyProperty.RegisterAttached(
        "CommitEditCommand",
        typeof(ICommand),
        typeof(MethodEditorInteractionBehavior),
        new PropertyMetadata(null, OnCommitEditCommandChanged));

    public static readonly DependencyProperty SelectRowOnRightClickProperty = DependencyProperty.RegisterAttached(
        "SelectRowOnRightClick",
        typeof(bool),
        typeof(MethodEditorInteractionBehavior),
        new PropertyMetadata(false, OnSelectRowOnRightClickChanged));

    public static void SetLoadedCommand(DependencyObject element, ICommand? value) =>
        element.SetValue(LoadedCommandProperty, value);

    public static ICommand? GetLoadedCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(LoadedCommandProperty);

    public static void SetDropActionCommand(DependencyObject element, ICommand? value) =>
        element.SetValue(DropActionCommandProperty, value);

    public static ICommand? GetDropActionCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(DropActionCommandProperty);

    public static void SetItemDoubleClickCommand(DependencyObject element, ICommand? value) =>
        element.SetValue(ItemDoubleClickCommandProperty, value);

    public static ICommand? GetItemDoubleClickCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(ItemDoubleClickCommandProperty);

    public static void SetCommitEditCommand(DependencyObject element, ICommand? value) =>
        element.SetValue(CommitEditCommandProperty, value);

    public static ICommand? GetCommitEditCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(CommitEditCommandProperty);

    public static void SetSelectRowOnRightClick(DependencyObject element, bool value) =>
        element.SetValue(SelectRowOnRightClickProperty, value);

    public static bool GetSelectRowOnRightClick(DependencyObject element) =>
        (bool)element.GetValue(SelectRowOnRightClickProperty);

    private static void OnLoadedCommandChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        if (e.OldValue != null)
        {
            element.Loaded -= Element_OnLoaded;
        }

        if (e.NewValue != null)
        {
            element.Loaded += Element_OnLoaded;
        }
    }

    private static void Element_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            Execute(GetLoadedCommand(element), null);
        }
    }

    private static void OnDropActionCommandChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not DataGrid dataGrid)
        {
            return;
        }

        if (e.OldValue != null)
        {
            dataGrid.Drop -= DataGrid_OnDrop;
        }

        if (e.NewValue != null)
        {
            dataGrid.AllowDrop = true;
            dataGrid.Drop += DataGrid_OnDrop;
        }
    }

    private static void DataGrid_OnDrop(object sender, DragEventArgs e)
    {
        if (sender is not DataGrid dataGrid
            || !e.Data.GetDataPresent(DataFormats.StringFormat)
            || e.Data.GetData(DataFormats.StringFormat) is not string actionType
            || string.IsNullOrWhiteSpace(actionType))
        {
            return;
        }

        var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
        var insertBeforeLineNo = (row?.Item as MethodLineViewItem)?.Line.LineNo;
        if (Execute(GetDropActionCommand(dataGrid), new ActionDropRequest(actionType, insertBeforeLineNo)))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private static void OnItemDoubleClickCommandChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not DataGrid dataGrid)
        {
            return;
        }

        if (e.OldValue != null)
        {
            dataGrid.MouseDoubleClick -= DataGrid_OnMouseDoubleClick;
        }

        if (e.NewValue != null)
        {
            dataGrid.MouseDoubleClick += DataGrid_OnMouseDoubleClick;
        }
    }

    private static void DataGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid dataGrid
            || FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject) is not { } row)
        {
            return;
        }

        dataGrid.SelectedItem = row.Item;
        dataGrid.CurrentItem = row.Item;
        row.Focus();
        if (Execute(GetItemDoubleClickCommand(dataGrid), row.Item))
        {
            e.Handled = true;
        }
    }

    private static void OnSelectRowOnRightClickChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not DataGrid dataGrid)
        {
            return;
        }

        if ((bool)e.OldValue)
        {
            dataGrid.PreviewMouseRightButtonDown -= DataGrid_OnPreviewMouseRightButtonDown;
        }

        if ((bool)e.NewValue)
        {
            dataGrid.PreviewMouseRightButtonDown += DataGrid_OnPreviewMouseRightButtonDown;
        }
    }

    private static void DataGrid_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid dataGrid
            || FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject) is not { } row)
        {
            return;
        }

        dataGrid.SelectedItem = row.Item;
        dataGrid.CurrentItem = row.Item;
        row.Focus();
    }

    private static void OnCommitEditCommandChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not DataGrid dataGrid)
        {
            return;
        }

        if (e.OldValue != null)
        {
            dataGrid.CellEditEnding -= DataGrid_OnCellEditEnding;
        }

        if (e.NewValue != null)
        {
            dataGrid.CellEditEnding += DataGrid_OnCellEditEnding;
        }
    }

    private static void DataGrid_OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (sender is not DataGrid dataGrid)
        {
            return;
        }

        dataGrid.Dispatcher.BeginInvoke(() => Execute(GetCommitEditCommand(dataGrid), null));
    }

    private static bool Execute(ICommand? command, object? parameter)
    {
        if (command?.CanExecute(parameter) != true)
        {
            return false;
        }

        command.Execute(parameter);
        return true;
    }

    private static T? FindVisualParent<T>(DependencyObject? child)
        where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T typed)
            {
                return typed;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }
}
