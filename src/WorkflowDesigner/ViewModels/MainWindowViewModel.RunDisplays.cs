using WorkflowCore.WpfDemo.Editor;

namespace WorkflowCore.WpfDemo.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void ShowCreateRunDisplayDialog()
    {
        CloseSubmenu();
        IsCreateMenuOpen = false;
        _createDocumentKind = CreateDocumentKind.RunDisplay;
        NewMethodName = GetNextRunDisplayName();
        CreateMethodError = string.Empty;
        OnPropertyChanged(nameof(CreateDialogTitle));
        OnPropertyChanged(nameof(CreateNameLabel));
        IsCreateMethodDialogOpen = true;
    }

    private string GetNextRunDisplayName()
    {
        var index = 1;
        string name;
        do
        {
            name = $"RunDisplay{index++}";
        }
        while (Project.RunDisplays.Any(display => string.Equals(display.Name, name, StringComparison.OrdinalIgnoreCase)));
        return name;
    }

    private void CreateRunDisplayFromDialog()
    {
        var name = NewMethodName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            CreateMethodError = "Run Display name is required.";
            return;
        }

        if (Project.RunDisplays.Any(display => string.Equals(display.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            CreateMethodError = $"Run Display '{name}' already exists.";
            return;
        }

        var runDisplay = new WorkflowRunDisplay
        {
            Name = name,
            IsDefault = Project.RunDisplays.Count == 0
        };
        Project.RunDisplays.Add(runDisplay);
        RunDisplays.Add(runDisplay);
        OpenRunDisplay(runDisplay);
        CloseCreateMethodDialog();
        RefreshJsonPreview();
        StatusText = $"Created Run Display '{name}'.";
    }

    public void OpenRunDisplay(WorkflowRunDisplay? runDisplay)
    {
        _documents.OpenRunDisplay(runDisplay, this);
        CloseSubmenu();
    }

    private void DeleteRunDisplay(object? parameter)
    {
        if (parameter is not WorkflowRunDisplay runDisplay) return;
        if (!_dialogs.Confirm("Delete Run Display", $"Delete Run Display '{runDisplay.Name}'?")) return;

        _documents.CloseRunDisplay(runDisplay);
        Project.RunDisplays.Remove(runDisplay);
        RunDisplays.Remove(runDisplay);
        if (runDisplay.IsDefault && RunDisplays.FirstOrDefault() is { } replacement) replacement.IsDefault = true;
        RefreshJsonPreview();
        StatusText = $"Deleted Run Display '{runDisplay.Name}'.";
    }

    public void MarkRunDisplayChanged()
    {
        RefreshJsonPreview();
        RaiseCommandStates();
    }
}
