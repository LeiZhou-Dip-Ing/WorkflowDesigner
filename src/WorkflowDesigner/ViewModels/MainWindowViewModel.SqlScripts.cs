using System.Collections.ObjectModel;
using System.Text.Json;
using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Services;

namespace WorkflowCore.WpfDemo.ViewModels;

public sealed partial class MainWindowViewModel
{
    public ObservableCollection<WorkflowSqlScript> SqlScripts { get; } = new();
    public RelayCommand OpenSqlScriptCommand { get; }
    public RelayCommand DeleteSqlScriptCommand { get; }

    private void ShowCreateSqlScriptDialog()
    {
        CloseSubmenu();
        IsCreateMenuOpen = false;
        _createDocumentKind = CreateDocumentKind.SqlScript;
        var index = 1;
        while (Project.SqlScripts.Any(script => string.Equals(script.Name, $"SqlScript{index}", StringComparison.OrdinalIgnoreCase))) index++;
        NewMethodName = $"SqlScript{index}";
        CreateMethodError = string.Empty;
        OnPropertyChanged(nameof(CreateDialogTitle));
        OnPropertyChanged(nameof(CreateNameLabel));
        IsCreateMethodDialogOpen = true;
    }

    public void OpenSqlScript(WorkflowSqlScript? script)
    {
        CloseSubmenu();
        _documents.OpenSqlScript(script, this);
    }

    private IReadOnlyList<string> GetEditorSuggestions(string? dataSource)
        => dataSource?.ToLowerInvariant() switch
        {
            "methodvariables" or "methodvariableexpressions" => SelectedMethodVariables
                .Select(variable => variable.VariableName).ToArray(),
            "threadtaskvariables" when SelectedMethod != null
                => ThreadTaskVariables.GetDeclaredNames(SelectedMethod),
            "methods" => Project.Methods.Select(method => method.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray(),
            "sqlscripts" => Project.SqlScripts.Select(script => script.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray(),
            _ => Array.Empty<string>()
        };

    private void RefreshPropertyEditorSuggestions()
        => _actionProperties.RefreshSuggestions(SelectedActionProperties, GetEditorSuggestions);

    internal void MarkSqlScriptChanged(WorkflowSqlScript script)
    {
        if (Project.SqlScripts.Contains(script)) RefreshJsonPreview();
    }

    internal Task<SqlScriptPreviewResult?> PreviewSqlAsync(
        string sql,
        IReadOnlyDictionary<string, JsonElement> parameters,
        bool validateOnly)
    {
        if (!validateOnly && !_dialogs.Confirm(
                "Execute SQL",
                "Execute this SQL against the database configured on the local Runtime? This may change data."))
            return Task.FromResult<SqlScriptPreviewResult?>(null);

        return PreviewSqlCoreAsync(sql, parameters, validateOnly);
    }

    private async Task<SqlScriptPreviewResult?> PreviewSqlCoreAsync(
        string sql,
        IReadOnlyDictionary<string, JsonElement> parameters,
        bool validateOnly)
        => await _runtimeApi.PreviewSqlAsync(sql, parameters, validateOnly);

    private void CreateSqlScriptFromDialog()
    {
        var name = NewMethodName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            CreateMethodError = "SQL script name is required.";
            return;
        }

        if (Project.SqlScripts.Any(script => string.Equals(script.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            CreateMethodError = $"SQL script '{name}' already exists.";
            return;
        }

        var script = new WorkflowSqlScript { Name = name, Content = "SELECT 1 AS Value;" };
        Project.SqlScripts.Add(script);
        SqlScripts.Add(script);
        OpenSqlScript(script);
        CloseCreateMethodDialog();
        RefreshJsonPreview();
        RefreshPropertyEditorSuggestions();
        StatusText = $"Created SQL script '{name}'.";
    }

    private void DeleteSqlScript(object? parameter)
    {
        if (parameter is not WorkflowSqlScript script || !_dialogs.Confirm(
                "Delete SQL script",
                $"Delete local SQL script '{script.Name}'? Runtime will not change until deployment.")) return;

        _documents.CloseSqlScript(script);
        Project.SqlScripts.Remove(script);
        SqlScripts.Remove(script);
        RefreshJsonPreview();
        RefreshPropertyEditorSuggestions();
        StatusText = $"Deleted local SQL script '{script.Name}'.";
    }

    private void ImportSqlScript(WorkflowSqlScript imported, string filePath)
    {
        var existing = Project.SqlScripts.FirstOrDefault(script => script.Uid == imported.Uid
            || string.Equals(script.Name, imported.Name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            if (!_dialogs.Confirm("Replace SQL script", $"Replace local SQL script '{existing.Name}'?")) return;
            _documents.CloseSqlScript(existing);
            Project.SqlScripts.Remove(existing);
            SqlScripts.Remove(existing);
        }

        Project.SqlScripts.Add(imported);
        SqlScripts.Add(imported);
        OpenSqlScript(imported);
        RefreshJsonPreview();
        RefreshPropertyEditorSuggestions();
        StatusText = $"Imported SQL script from {filePath}.";
    }
}
