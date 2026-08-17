namespace WorkflowCore.WpfDemo.Services.Ui;

public interface IEditorFileDialogs
{
    string? SelectNewProjectPath() => SelectProjectExportPath();

    string? SelectProjectOpenFile() => SelectProjectImportFile();

    string? SelectDocumentImportFile();

    string? SelectProjectImportFile();

    string? SelectDocumentExportPath(string documentName, string suggestedFileName);

    string? SelectProjectExportPath();

    string? SelectManagedAssemblyFile() => null;
}
