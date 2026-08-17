using WorkflowCore.WpfDemo.Services.Designer;
using WorkflowCore.WpfDemo.Services.Editing;
using WorkflowDesigner.Contracts;
using WorkflowDesigner.WpfSdk;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.ViewModels;

public sealed partial class MainWindowViewModel
{
    public WorkflowActionDescriptorDto? SelectedActionDescriptor
        => SelectedMethodLine?.Action is { } action ? ResolveActionDescriptor(action) : null;

    public string SelectedActionWorkspaceKey
        => ActionPresentationPolicy.GetWorkspaceKey(SelectedActionDescriptor);

    public string SelectedActionWorkspaceFallbackKey
        => ActionPresentationPolicy.GetWorkspaceFallbackKey(SelectedActionDescriptor);

    public bool IsSelectedPropertyWorkspace
        => ActionPresentationPolicy.UsesPropertyWorkspace(SelectedActionDescriptor);

    public bool IsSelectedCustomWorkspace
        => !IsSelectedPropertyWorkspace;

    // V4 compatibility alias used by tests and existing bindings.
    public bool IsSelectedImageWorkspace
        => string.Equals(SelectedActionWorkspaceFallbackKey, WorkflowWorkspaceKeys.Image, StringComparison.OrdinalIgnoreCase)
           && IsSelectedCustomWorkspace;

    public IWorkflowDesignerActionContext? SelectedDesignerActionContext
        => SelectedActionDescriptor == null
            ? null
            : new WorkflowDesignerActionContextAdapter(this, SelectedActionDescriptor);

    public bool CanOpenSelectedActionEditor
        => ActionPresentationPolicy.CanOpenOnDoubleClick(SelectedActionDescriptor);

    public string SelectedActionDoubleClickEditor
        => ActionPresentationPolicy.GetDoubleClickEditor(SelectedActionDescriptor);

    private void NotifySelectedActionPresentationChanged()
    {
        OnPropertyChanged(nameof(SelectedActionDescriptor));
        OnPropertyChanged(nameof(SelectedActionWorkspaceKey));
        OnPropertyChanged(nameof(SelectedActionWorkspaceFallbackKey));
        OnPropertyChanged(nameof(IsSelectedPropertyWorkspace));
        OnPropertyChanged(nameof(IsSelectedCustomWorkspace));
        OnPropertyChanged(nameof(IsSelectedImageWorkspace));
        OnPropertyChanged(nameof(SelectedDesignerActionContext));
        OnPropertyChanged(nameof(CanOpenSelectedActionEditor));
        OnPropertyChanged(nameof(SelectedActionDoubleClickEditor));
    }
}
