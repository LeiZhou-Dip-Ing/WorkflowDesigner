using System.Windows;
using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Services.Ui;
using WorkflowCore.WpfDemo.ViewModels;
using WorkflowCore.WpfDemo.Views;

namespace WorkflowCore.WpfDemo.Services.Scripting;

public sealed class SharpScriptLibraryManagerDialog : ISharpScriptLibraryManagerDialog
{
    private readonly IRuntimeApiClient _runtimeApi;
    private readonly ISharpScriptLibraryCache _cache;
    private readonly IEditorFileDialogs _fileDialogs;
    private readonly IEditorDialogs _dialogs;

    public SharpScriptLibraryManagerDialog(
        IRuntimeApiClient runtimeApi,
        ISharpScriptLibraryCache cache,
        IEditorFileDialogs fileDialogs,
        IEditorDialogs dialogs)
    {
        _runtimeApi = runtimeApi ?? throw new ArgumentNullException(nameof(runtimeApi));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _fileDialogs = fileDialogs ?? throw new ArgumentNullException(nameof(fileDialogs));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
    }

    public bool Show(WorkflowProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var viewModel = new SharpScriptLibraryManagerViewModel(
            project,
            _runtimeApi,
            _cache,
            _fileDialogs,
            _dialogs);
        var window = new SharpScriptLibraryManagerWindow
        {
            DataContext = viewModel,
            Owner = Application.Current?.MainWindow
        };
        window.ShowDialog();
        return viewModel.HasProjectChanges;
    }
}
