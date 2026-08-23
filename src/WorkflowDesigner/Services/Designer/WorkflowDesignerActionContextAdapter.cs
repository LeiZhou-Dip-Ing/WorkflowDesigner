using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;
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

    public WorkflowDesignerActionContextAdapter(
        MainWindowViewModel owner,
        WorkflowActionDescriptorDto descriptor)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        PropertyChangedEventManager.AddHandler(_owner, OwnerOnPropertyChanged, string.Empty);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public WorkflowActionDescriptorDto Descriptor { get; }

    public IReadOnlyList<IWorkflowPropertyEditorModel> Properties
        => _owner.SelectedActionProperties.Cast<IWorkflowPropertyEditorModel>().ToArray();

    public ImageSource? PreviewImage => _owner.SelectedVisionPreviewImage;

    public bool HasPreview => _owner.HasSelectedVisionPreview;

    public string PreviewInfo => _owner.SelectedVisionPreviewInfo;

    public bool CanRunPreview => _owner.CanRunDesignerPreview;

    public Task RunPreviewAsync() => _owner.RunDesignerPreviewAsync();

    public Task RunCommandAsync(WorkflowDesignerCommandRequest request)
        => _owner.RunDesignerCommandAsync(request);

    public ICommand CreateValueCommand => _owner.CreatePropertyValueCommand;

    public ICommand ClearValueCommand => _owner.ClearPropertyValueCommand;

    private void OwnerOnPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (string.IsNullOrEmpty(eventArgs.PropertyName)
            || eventArgs.PropertyName is nameof(MainWindowViewModel.SelectedVisionPreviewImage)
                or nameof(MainWindowViewModel.HasSelectedVisionPreview)
                or nameof(MainWindowViewModel.SelectedVisionPreviewInfo)
                or nameof(MainWindowViewModel.IsRunning)
                or nameof(MainWindowViewModel.IsRuntimeOnline))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PreviewImage)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPreview)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PreviewInfo)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRunPreview)));
        }

        if (string.IsNullOrEmpty(eventArgs.PropertyName)
            || eventArgs.PropertyName == nameof(MainWindowViewModel.SelectedActionPropertiesView))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Properties)));
        }
    }
}
