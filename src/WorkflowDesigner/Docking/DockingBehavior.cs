using System.Windows;
using System.Windows.Threading;
using AvalonDock;
using AvalonDock.Layout;
using WorkflowCore.WpfDemo.Models;

namespace WorkflowCore.WpfDemo.Docking;

public static class DockingBehavior
{
    public static readonly DependencyProperty SelectedEditorProperty =
        DependencyProperty.RegisterAttached(
            "SelectedEditor",
            typeof(DockPaneItem),
            typeof(DockingBehavior),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedEditorChanged));

    private static readonly DependencyProperty IsAttachedProperty =
        DependencyProperty.RegisterAttached(
            "IsAttached",
            typeof(bool),
            typeof(DockingBehavior),
            new PropertyMetadata(false));

    private static readonly DependencyProperty IsSynchronizingSelectionProperty =
        DependencyProperty.RegisterAttached(
            "IsSynchronizingSelection",
            typeof(bool),
            typeof(DockingBehavior),
            new PropertyMetadata(false));

    public static DockPaneItem? GetSelectedEditor(DependencyObject element) =>
        (DockPaneItem?)element.GetValue(SelectedEditorProperty);

    public static void SetSelectedEditor(DependencyObject element, DockPaneItem? value) =>
        element.SetValue(SelectedEditorProperty, value);

    private static bool GetIsAttached(DependencyObject element) =>
        (bool)element.GetValue(IsAttachedProperty);

    private static void SetIsAttached(DependencyObject element, bool value) =>
        element.SetValue(IsAttachedProperty, value);

    private static bool GetIsSynchronizingSelection(DependencyObject element) =>
        (bool)element.GetValue(IsSynchronizingSelectionProperty);

    private static void SetIsSynchronizingSelection(DependencyObject element, bool value) =>
        element.SetValue(IsSynchronizingSelectionProperty, value);

    private static void OnSelectedEditorChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not DockingManager dockingManager)
        {
            return;
        }

        if (!GetIsAttached(dockingManager))
        {
            dockingManager.ActiveContentChanged += DockingManager_OnActiveContentChanged;
            dockingManager.DocumentClosed += DockingManager_OnDocumentClosed;
            dockingManager.Unloaded += DockingManager_OnUnloaded;
            SetIsAttached(dockingManager, true);
        }

        if (GetIsSynchronizingSelection(dockingManager))
        {
            return;
        }

        if (e.NewValue is DockPaneItem pane)
        {
            dockingManager.Dispatcher.BeginInvoke(
                () => ActivateLayoutDocument(dockingManager, pane),
                DispatcherPriority.Loaded);
        }
    }

    private static void DockingManager_OnActiveContentChanged(object? sender, EventArgs e)
    {
        if (sender is not DockingManager dockingManager ||
            dockingManager.ActiveContent is not DockPaneItem pane ||
            ReferenceEquals(GetSelectedEditor(dockingManager), pane))
        {
            return;
        }

        try
        {
            SetIsSynchronizingSelection(dockingManager, true);
            SetSelectedEditor(dockingManager, pane);
        }
        finally
        {
            SetIsSynchronizingSelection(dockingManager, false);
        }
    }

    private static void DockingManager_OnDocumentClosed(object? sender, DocumentClosedEventArgs e)
        => SynchronizeClosedDocument(e.Document.Content);

    internal static void SynchronizeClosedDocument(object? documentContent)
    {
        if (documentContent is DockPaneItem pane)
        {
            // AvalonDock removes the LayoutDocument, but DocumentsSource is not a two-way
            // collection binding. Remove the corresponding view-model item as well so that
            // opening the same method later creates a fresh LayoutDocument.
            pane.CloseCommand?.Execute();
        }
    }

    private static void ActivateLayoutDocument(DockingManager dockingManager, DockPaneItem pane)
    {
        var layoutDocument = FindLayoutDocument(dockingManager.Layout, pane);
        try
        {
            SetIsSynchronizingSelection(dockingManager, true);
            if (layoutDocument != null)
            {
                layoutDocument.IsSelected = true;
                layoutDocument.IsActive = true;
            }

            dockingManager.ActiveContent = pane;
            pane.IsActive = true;
            pane.IsSelected = true;
        }
        finally
        {
            SetIsSynchronizingSelection(dockingManager, false);
        }
    }

    private static LayoutDocument? FindLayoutDocument(LayoutRoot? layoutRoot, DockPaneItem pane)
    {
        if (layoutRoot?.RootPanel == null)
        {
            return null;
        }

        return EnumerateLayoutDocuments(layoutRoot.RootPanel)
            .FirstOrDefault(document =>
                ReferenceEquals(document.Content, pane) ||
                string.Equals(document.ContentId, pane.ContentId, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<LayoutDocument> EnumerateLayoutDocuments(ILayoutContainer container)
    {
        if (container is LayoutDocumentPane documentPane)
        {
            foreach (var document in documentPane.Children.OfType<LayoutDocument>())
            {
                yield return document;
            }
        }

        foreach (var child in container.Children.OfType<ILayoutContainer>())
        {
            foreach (var document in EnumerateLayoutDocuments(child))
            {
                yield return document;
            }
        }
    }

    private static void DockingManager_OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is DockingManager dockingManager)
        {
            dockingManager.ActiveContentChanged -= DockingManager_OnActiveContentChanged;
            dockingManager.DocumentClosed -= DockingManager_OnDocumentClosed;
            dockingManager.Unloaded -= DockingManager_OnUnloaded;
            SetIsAttached(dockingManager, false);
        }
    }
}
