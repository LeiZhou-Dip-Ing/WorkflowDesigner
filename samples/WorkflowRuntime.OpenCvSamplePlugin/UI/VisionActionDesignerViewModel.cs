using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using WorkflowDesigner.WpfSdk;

namespace WorkflowRuntime.OpenCvSamplePlugin.UI;

/// <summary>
/// Action-specific view model used by external OpenCV workspaces/dialogs. It edits the same
/// property models as the generic metadata Property Panel, and asks the host to run the current
/// editor method so every Vision Action can publish its own line-scoped preview.
/// </summary>
internal sealed class VisionActionDesignerViewModel : INotifyPropertyChanged
{
    private readonly IWorkflowDesignerActionContext _context;
    private readonly IWorkflowDesignerResourcePreviewCapability? _preview;
    private ImageSource? _previewImage;

    public VisionActionDesignerViewModel(
        IWorkflowDesignerActionContext context,
        string title,
        string description,
        IEnumerable<string>? preferredFields = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _preview = context.GetCapability<IWorkflowDesignerResourcePreviewCapability>();
        _previewImage = OpenCvPreviewDecoder.Decode(_preview?.Current);
        Title = title;
        Description = description;
        var names = preferredFields?.ToArray() ?? Array.Empty<string>();
        Properties = names.Length == 0
            ? context.Properties
            : names.Select(name => context.Properties.FirstOrDefault(property =>
                    string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)))
                .Where(property => property != null)
                .Cast<IWorkflowPropertyEditorModel>()
                .ToArray();
        InputProperties = Properties.Where(property => !property.IsOutputBinding).ToArray();
        OutputProperties = Properties.Where(property => property.IsOutputBinding).ToArray();
        RunCommand = new AsyncRelayCommand(RefreshPreviewAsync, () => CanRunPreview);
        foreach (var property in Properties)
        {
            property.PropertyChanged += PropertyOnPropertyChanged;
        }
        if (_preview != null)
        {
            _preview.PropertyChanged += PreviewOnPropertyChanged;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IWorkflowDesignerActionContext Context => _context;
    public string Title { get; }
    public string Description { get; }
    public IReadOnlyList<IWorkflowPropertyEditorModel> Properties { get; }
    public IReadOnlyList<IWorkflowPropertyEditorModel> InputProperties { get; }
    public IReadOnlyList<IWorkflowPropertyEditorModel> OutputProperties { get; }
    public ImageSource? PreviewImage => _previewImage;
    public bool HasPreview => _previewImage != null;
    public string PreviewInfo => _preview?.Current?.Description ?? "No preview yet";
    public bool CanRunPreview => _preview != null;
    public ICommand RunCommand { get; }

    public string ConfigurationSummary
        => string.Join("  •  ", InputProperties
            .Where(property => !string.Equals(property.Name, "InputImage", StringComparison.OrdinalIgnoreCase)
                               && !string.Equals(property.Name, "SourceImage", StringComparison.OrdinalIgnoreCase)
                               && !string.Equals(property.Name, "MaskImage", StringComparison.OrdinalIgnoreCase)
                               && !string.Equals(property.Name, "PublishPreview", StringComparison.OrdinalIgnoreCase))
            .Select(property => $"{property.DisplayName}: {FormatPropertyValue(property)}"));

    public string OutputSummary
        => OutputProperties.Count == 0
            ? "No output bindings"
            : string.Join("  •  ", OutputProperties.Select(property => $"{property.DisplayName}: {FormatPropertyValue(property)}"));

    public IWorkflowPropertyEditorModel? FindProperty(string name)
        => Properties.FirstOrDefault(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase));

    private void PropertyOnPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (string.IsNullOrWhiteSpace(eventArgs.PropertyName)
            || eventArgs.PropertyName is nameof(IWorkflowPropertyEditorModel.ValueText)
                or nameof(IWorkflowPropertyEditorModel.SelectedValue)
                or nameof(IWorkflowPropertyEditorModel.BooleanValue))
        {
            OnPropertyChanged(nameof(ConfigurationSummary));
            OnPropertyChanged(nameof(OutputSummary));
        }
    }

    private static string FormatPropertyValue(IWorkflowPropertyEditorModel property)
    {
        if (string.Equals(property.EditorKey, WorkflowRuntime.ActionSdk.WorkflowPropertyEditorKeys.Checkbox, StringComparison.OrdinalIgnoreCase))
        {
            return property.BooleanValue ? "On" : "Off";
        }

        if (string.Equals(property.EditorKey, WorkflowRuntime.ActionSdk.WorkflowPropertyEditorKeys.Select, StringComparison.OrdinalIgnoreCase))
        {
            return property.SelectedValue ?? property.ValueText;
        }

        return string.IsNullOrWhiteSpace(property.ValueText) ? "—" : property.ValueText;
    }

    private async Task RefreshPreviewAsync()
    {
        if (_preview != null)
        {
            await _preview.RefreshAsync().ConfigureAwait(true);
        }
    }

    private void PreviewOnPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (!string.IsNullOrWhiteSpace(eventArgs.PropertyName)
            && eventArgs.PropertyName is not nameof(IWorkflowDesignerResourcePreviewCapability.Current)
                and not nameof(IWorkflowDesignerResourcePreviewCapability.HasContent))
        {
            return;
        }

        _previewImage = OpenCvPreviewDecoder.Decode(_preview?.Current);
        OnPropertyChanged(nameof(PreviewImage));
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(PreviewInfo));
        OnPropertyChanged(nameof(CanRunPreview));
        (RunCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

}
