using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Services.Grafana;
using System.Collections.ObjectModel;

namespace WorkflowCore.WpfDemo.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly IRuntimeDisplayProjectService _runtimeDisplayProjectService;
    private readonly GrafanaDashboardUrlBuilder _grafanaDashboardUrlBuilder;
    private RuntimeDisplayDefinition? _runtimeDisplayBeingRenamed;

    public ObservableCollection<RuntimeDisplayDefinition> RuntimeDisplays { get; } = new();
    public RelayCommand OpenRuntimeDisplayCommand { get; }
    public RelayCommand RenameRuntimeDisplayCommand { get; }
    public RelayCommand DeleteRuntimeDisplayCommand { get; }

    private enum CreateDocumentKind
    {
        Method,
        CSharpScript,
        RuntimeDisplay,
        RuntimeDisplayRename
    }

    private void ShowCreateRuntimeDisplayDialog()
    {
        CloseSubmenu();
        IsCreateMenuOpen = false;
        _runtimeDisplayBeingRenamed = null;
        _createDocumentKind = CreateDocumentKind.RuntimeDisplay;
        NewMethodName = GetNextRuntimeDisplayName();
        CreateMethodError = string.Empty;
        OnPropertyChanged(nameof(CreateDialogTitle));
        OnPropertyChanged(nameof(CreateNameLabel));
        IsCreateMethodDialogOpen = true;
    }

    private void ShowRenameRuntimeDisplayDialog(object? parameter)
    {
        if (parameter is not RuntimeDisplayDefinition runtimeDisplay) return;
        CloseSubmenu();
        _runtimeDisplayBeingRenamed = runtimeDisplay;
        _createDocumentKind = CreateDocumentKind.RuntimeDisplayRename;
        NewMethodName = runtimeDisplay.Name;
        CreateMethodError = string.Empty;
        OnPropertyChanged(nameof(CreateDialogTitle));
        OnPropertyChanged(nameof(CreateNameLabel));
        IsCreateMethodDialogOpen = true;
    }

    private string GetNextRuntimeDisplayName()
    {
        var index = 1;
        string name;
        do name = $"RuntimeDisplay{index++}";
        while (Project.RuntimeDisplays.Any(display =>
                   string.Equals(display.Name, name, StringComparison.OrdinalIgnoreCase)));
        return name;
    }

    private async Task SaveRuntimeDisplayDialogAsync()
    {
        var name = NewMethodName.Trim();
        if (name.Length == 0)
        {
            CreateMethodError = "Runtime Display name is required.";
            return;
        }

        try
        {
            if (_createDocumentKind == CreateDocumentKind.RuntimeDisplayRename
                && _runtimeDisplayBeingRenamed is { } existing)
            {
                await _runtimeDisplayProjectService.RenameAsync(Project, existing, name);
                PersistProjectAfterRuntimeDisplayChange();
                StatusText = $"Renamed Runtime Display to '{name}'.";
            }
            else
            {
                var created = await _runtimeDisplayProjectService.CreateAsync(Project, name);
                try
                {
                    PersistProjectAfterRuntimeDisplayChange();
                }
                catch
                {
                    await _runtimeDisplayProjectService.DeleteAsync(Project, created);
                    throw;
                }
                RuntimeDisplays.Add(created);
                OpenRuntimeDisplay(created);
                StatusText = $"Created Grafana Runtime Display '{name}'.";
            }

            CloseCreateMethodDialog();
            _runtimeDisplayBeingRenamed = null;
            RefreshJsonPreview();
        }
        catch (Exception error)
        {
            CreateMethodError = error.Message;
            _dialogs.ShowError("Runtime Display", error.Message);
        }
    }

    public void OpenRuntimeDisplay(RuntimeDisplayDefinition? runtimeDisplay)
    {
        if (runtimeDisplay == null) return;
        try
        {
            _documents.OpenRuntimeDisplay(
                Project,
                runtimeDisplay,
                _grafanaDashboardUrlBuilder.Build(runtimeDisplay.GrafanaDashboardUid));
            CloseSubmenu();
        }
        catch (Exception error)
        {
            _dialogs.ShowError("Open Runtime Display", error.Message);
        }
    }

    private async Task DeleteRuntimeDisplayAsync(RuntimeDisplayDefinition? runtimeDisplay)
    {
        if (runtimeDisplay == null) return;
        if (!_dialogs.Confirm(
                "Delete Runtime Display",
                $"Delete Runtime Display '{runtimeDisplay.Name}' and its Grafana Dashboard?")) return;

        try
        {
            await _runtimeDisplayProjectService.DeleteAsync(Project, runtimeDisplay);
            _documents.CloseRuntimeDisplay(runtimeDisplay);
            RuntimeDisplays.Remove(runtimeDisplay);
            PersistProjectAfterRuntimeDisplayChange();
            RefreshJsonPreview();
            StatusText = $"Deleted Runtime Display '{runtimeDisplay.Name}'.";
        }
        catch (Exception error)
        {
            _dialogs.ShowError("Delete Runtime Display", error.Message);
            StatusText = $"Runtime Display was not deleted: {error.Message}";
        }
    }

    private void PersistProjectAfterRuntimeDisplayChange()
    {
        if (_projectFilePath == null || _projectFileService == null)
            throw new InvalidOperationException("Save the Project before managing Runtime Displays.");
        _projectFileService.Save(_projectFilePath, Project);
        _session.SavedProjectJson = _documentPersistence.Serialize(Project);
        _documents.MarkAllDocumentsSaved(Project);
    }
}
