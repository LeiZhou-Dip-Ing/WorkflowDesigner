using System.Windows;

namespace WorkflowCore.WpfDemo.Services.Ui;

public sealed class WindowsEditorDialogs : IEditorDialogs
{
    public void ShowInformation(string title, string message)
        => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowWarning(string title, string message)
        => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public void ShowError(string title, string message)
        => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public bool Confirm(string title, string message)
        => Show(
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    public EditorDialogChoice AskYesNoCancel(string title, string message)
        => Show(
            message,
            title,
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel) switch
        {
            MessageBoxResult.Yes => EditorDialogChoice.Yes,
            MessageBoxResult.No => EditorDialogChoice.No,
            _ => EditorDialogChoice.Cancel
        };

    public DocumentImportConflictResolution ResolveDocumentImportConflict(
        string documentType,
        string documentName)
        => Show(
            $"A {documentType} named '{documentName}' already exists.\n\n"
            + "Yes: overwrite the existing document\n"
            + $"No: import a copy such as '{documentName}(1)'\n"
            + "Cancel: do not import",
            "Document already exists",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel) switch
        {
            MessageBoxResult.Yes => DocumentImportConflictResolution.Overwrite,
            MessageBoxResult.No => DocumentImportConflictResolution.CreateCopy,
            _ => DocumentImportConflictResolution.Cancel
        };

    // Dialogs may be requested after an API call resumed on a worker thread. Always marshal
    // them back to WPF and attach the main window so they cannot hide behind the sync overlay.
    private static MessageBoxResult Show(
        string message,
        string title,
        MessageBoxButton buttons,
        MessageBoxImage image,
        MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        var application = Application.Current;
        var dispatcher = application?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            return dispatcher.Invoke(() => Show(message, title, buttons, image, defaultResult));
        }

        return application?.MainWindow is { } owner
            ? MessageBox.Show(owner, message, title, buttons, image, defaultResult)
            : MessageBox.Show(message, title, buttons, image, defaultResult);
    }
}
