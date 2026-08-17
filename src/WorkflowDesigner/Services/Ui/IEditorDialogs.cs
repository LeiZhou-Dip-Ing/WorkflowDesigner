namespace WorkflowCore.WpfDemo.Services.Ui;

public enum EditorDialogChoice
{
    Yes,
    No,
    Cancel
}

public enum DocumentImportConflictResolution
{
    Overwrite,
    CreateCopy,
    Cancel
}

public interface IEditorDialogs
{
    void ShowInformation(string title, string message);

    void ShowWarning(string title, string message);

    void ShowError(string title, string message);

    bool Confirm(string title, string message);

    EditorDialogChoice AskYesNoCancel(string title, string message);

    DocumentImportConflictResolution ResolveDocumentImportConflict(
        string documentType,
        string documentName);
}
