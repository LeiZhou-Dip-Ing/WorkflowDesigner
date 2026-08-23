using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Text.Json;
using WorkflowCore.WpfDemo.ViewModels;
using WorkflowDesigner.WpfSdk;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.Services.Designer;

/// <summary>
/// Adapts the host ViewModel to the narrow public Designer SDK context without exposing
/// MainWindowViewModel to external extensions.
/// </summary>
public sealed class WorkflowDesignerActionContextAdapter : IWorkflowDesignerActionContext
{
    private readonly MainWindowViewModel _owner;
    private readonly ResourcePreviewCapability _preview;

    public WorkflowDesignerActionContextAdapter(
        MainWindowViewModel owner,
        WorkflowActionDescriptorDto descriptor)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        ArgumentNullException.ThrowIfNull(descriptor);
        Descriptor = new WorkflowDesignerActionDescriptor(
            descriptor.ActionId,
            descriptor.ActionType,
            descriptor.DisplayName,
            descriptor.Description,
            descriptor.PluginId,
            descriptor.PluginVersion);
        _preview = new ResourcePreviewCapability(owner);
        PropertyChangedEventManager.AddHandler(_owner, OwnerOnPropertyChanged, string.Empty);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public WorkflowDesignerActionDescriptor Descriptor { get; }

    public IReadOnlyList<IWorkflowPropertyEditorModel> Properties
        => _owner.SelectedActionProperties.Cast<IWorkflowPropertyEditorModel>().ToArray();

    public TCapability? GetCapability<TCapability>() where TCapability : class
        => _preview as TCapability;

    public Task<WorkflowDesignerCommandResult> ExecuteCommandAsync(
        WorkflowDesignerCommandRequest request,
        CancellationToken cancellationToken = default)
        => _owner.RunDesignerCommandAsync(request, cancellationToken);

    public ICommand CreateValueCommand => _owner.CreatePropertyValueCommand;

    public ICommand ClearValueCommand => _owner.ClearPropertyValueCommand;

    private void OwnerOnPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (string.IsNullOrEmpty(eventArgs.PropertyName)
            || eventArgs.PropertyName is nameof(MainWindowViewModel.SelectedResourcePreview)
                or nameof(MainWindowViewModel.HasSelectedResourcePreview)
                or nameof(MainWindowViewModel.IsRunning)
                or nameof(MainWindowViewModel.IsRuntimeOnline))
        {
            _preview.NotifyChanged();
        }

        if (string.IsNullOrEmpty(eventArgs.PropertyName)
            || eventArgs.PropertyName == nameof(MainWindowViewModel.SelectedActionPropertiesView))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Properties)));
        }
    }

    private sealed class ResourcePreviewCapability : IWorkflowDesignerResourcePreviewCapability
    {
        private readonly MainWindowViewModel _owner;
        public ResourcePreviewCapability(MainWindowViewModel owner) => _owner = owner;
        public event PropertyChangedEventHandler? PropertyChanged;
        public WorkflowDesignerResourcePreview? Current => _owner.SelectedResourcePreview;
        public bool HasContent => Current?.Content is { Length: > 0 };
        public async Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _owner.RunDesignerPreviewAsync();
        }
        public void NotifyChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Current)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasContent)));
        }
    }
}
