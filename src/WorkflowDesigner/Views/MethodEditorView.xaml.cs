using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WorkflowCore.WpfDemo.Models;
using WorkflowCore.WpfDemo.Services.Designer;
using WorkflowCore.WpfDemo.Services.Editing;
using WorkflowDesigner.WpfSdk;
using WorkflowCore.WpfDemo.ViewModels;

namespace WorkflowCore.WpfDemo.Views;

public partial class MethodEditorView : UserControl
{
    private MethodEditorViewModel? _subscribedViewModel;

    public MethodEditorView()
    {
        InitializeComponent();
        DataContextChanged += MethodEditorView_OnDataContextChanged;
        Loaded += MethodEditorView_OnLoaded;
        Unloaded += MethodEditorView_OnUnloaded;
    }

    private void MethodEditorView_OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        => AttachViewModel(e.NewValue as MethodEditorViewModel);

    private void MethodEditorView_OnLoaded(object sender, RoutedEventArgs e)
        => AttachViewModel(DataContext as MethodEditorViewModel);

    private void MethodEditorView_OnUnloaded(object sender, RoutedEventArgs e)
        => AttachViewModel(null);

    private void AttachViewModel(MethodEditorViewModel? viewModel)
    {
        if (ReferenceEquals(_subscribedViewModel, viewModel))
        {
            return;
        }

        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.ActionEditorRequested -= ViewModel_OnActionEditorRequested;
        }

        _subscribedViewModel = viewModel;
        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.ActionEditorRequested += ViewModel_OnActionEditorRequested;
        }
    }

    private void ViewModel_OnActionEditorRequested(object? sender, ActionEditorRequestEventArgs e)
    {
        if (_subscribedViewModel == null)
        {
            return;
        }

        var context = new WorkflowDesignerActionContextAdapter(_subscribedViewModel.Owner, e.Descriptor);
        var registry = WorkflowDesignerRegistryHost.Current;
        if (!registry.TryCreateActionEditor(e.EditorKey, context, out var dialog) || dialog == null)
        {
            var fallbackKey = ActionPresentationPolicy.GetDoubleClickEditorFallback(e.Descriptor);
            if (!registry.TryCreateActionEditor(fallbackKey, context, out dialog) || dialog == null)
            {
                return;
            }
        }

        if (Window.GetWindow(this) is { } ownerWindow)
        {
            dialog.Owner = ownerWindow;
        }

        dialog.ShowDialog();
    }

    private void PropertyLookupLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs eventArgs)
        => CommitLookupText(sender);

    private void PropertyLookupKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.Enter)
        {
            return;
        }

        CommitLookupText(sender);
        eventArgs.Handled = true;
    }

    private static void CommitLookupText(object sender)
    {
        if (sender is ComboBox { DataContext: ActionPropertyItem property } comboBox
            && !string.Equals(property.ValueText, comboBox.Text, StringComparison.Ordinal))
        {
            property.ValueText = comboBox.Text;
        }
    }

}
