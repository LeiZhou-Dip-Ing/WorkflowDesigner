using Microsoft.Win32;

namespace WorkflowCore.WpfDemo.Services.Ui;

public sealed class WindowsEditorFileDialogs : IEditorFileDialogs
{
    public string? SelectNewProjectPath()
        => SelectSavePath(
            "Create Workflow Project",
            "Workflow project JSON (*.json)|*.json",
            "New Workflow Project.json");

    public string? SelectProjectOpenFile()
        => SelectOpenPath("Open Workflow Project", "Workflow project JSON (*.json)|*.json");

    public string? SelectDocumentImportFile()
        => SelectOpenPath("Import Workflow Document", "Workflow document JSON (*.json)|*.json|All files (*.*)|*.*");

    public string? SelectProjectImportFile()
        => SelectOpenPath("Import Workflow Project", "Workflow project JSON (*.json)|*.json|All files (*.*)|*.*");

    public string? SelectDocumentExportPath(string documentName, string suggestedFileName)
        => SelectSavePath(
            $"Export Current Document - {documentName}",
            "Workflow document JSON (*.json)|*.json|All files (*.*)|*.*",
            suggestedFileName);

    public string? SelectProjectExportPath()
        => SelectSavePath(
            "Export Workflow Project",
            "Workflow project JSON (*.json)|*.json|All files (*.*)|*.*",
            "workflow-project.json");

    public string? SelectManagedAssemblyFile()
        => SelectOpenPath("Import Managed Script Library", "Managed assemblies (*.dll)|*.dll");

    private static string? SelectOpenPath(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static string? SelectSavePath(string title, string filter, string fileName)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = fileName };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
