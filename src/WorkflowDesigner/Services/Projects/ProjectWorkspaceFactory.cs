using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Models;
using WorkflowCore.WpfDemo.Services.Editing;
using WorkflowCore.WpfDemo.Services.Runtime;
using WorkflowCore.WpfDemo.Services.Scripting;
using WorkflowCore.WpfDemo.Services.Ui;
using WorkflowCore.WpfDemo.ViewModels;

namespace WorkflowCore.WpfDemo.Services.Projects;

/// <summary>Builds the isolated editor state owned by one opened project file.</summary>
public sealed class ProjectWorkspaceFactory : IProjectWorkspaceFactory
{
    private readonly IMethodEditorViewModelFactory _methodEditorFactory;
    private readonly ICSharpScriptEditorViewModelFactory _scriptEditorFactory;
    private readonly IEditorDocumentPersistence _persistence;
    private readonly IRuntimeApiClient _runtimeApi;
    private readonly IEditorActionCatalog _actionCatalog;
    private readonly ILocalDraftStore _draftStore;
    private readonly IEditorDialogs _dialogs;
    private readonly IEditorFileDialogs _fileDialogs;
    private readonly IUiDispatcher _dispatcher;
    private readonly IUiTimerFactory _timerFactory;
    private readonly ISharpScriptTemplateFactory _scriptTemplateFactory;
    private readonly ISharpScriptLibraryManagerDialog _scriptLibraryManagerDialog;
    private readonly IWorkflowProjectFileService _projectFileService;
    private readonly IProtectedWorkflowImportService _protectedWorkflowImporter;

    public ProjectWorkspaceFactory(
        IMethodEditorViewModelFactory methodEditorFactory,
        ICSharpScriptEditorViewModelFactory scriptEditorFactory,
        IEditorDocumentPersistence persistence,
        IRuntimeApiClient runtimeApi,
        IEditorActionCatalog actionCatalog,
        ILocalDraftStore draftStore,
        IEditorDialogs dialogs,
        IEditorFileDialogs fileDialogs,
        IUiDispatcher dispatcher,
        IUiTimerFactory timerFactory,
        ISharpScriptTemplateFactory scriptTemplateFactory,
        ISharpScriptLibraryManagerDialog scriptLibraryManagerDialog,
        IWorkflowProjectFileService projectFileService,
        IProtectedWorkflowImportService protectedWorkflowImporter)
    {
        _methodEditorFactory = methodEditorFactory;
        _scriptEditorFactory = scriptEditorFactory;
        _persistence = persistence;
        _runtimeApi = runtimeApi;
        _actionCatalog = actionCatalog;
        _draftStore = draftStore;
        _dialogs = dialogs;
        _fileDialogs = fileDialogs;
        _dispatcher = dispatcher;
        _timerFactory = timerFactory;
        _scriptTemplateFactory = scriptTemplateFactory;
        _scriptLibraryManagerDialog = scriptLibraryManagerDialog;
        _projectFileService = projectFileService;
        _protectedWorkflowImporter = protectedWorkflowImporter;
    }

    public IProjectWorkspace Create(OpenedWorkflowProject openedProject)
    {
        ArgumentNullException.ThrowIfNull(openedProject);
        return new MainWindowViewModel(
            _methodEditorFactory,
            _scriptEditorFactory,
            _persistence,
            _runtimeApi,
            _actionCatalog,
            _draftStore,
            _dialogs,
            _fileDialogs,
            _dispatcher,
            _timerFactory,
            editorSession: new EditorSession(openedProject.Project),
            scriptTemplateFactory: _scriptTemplateFactory,
            scriptLibraryManagerDialog: _scriptLibraryManagerDialog,
            projectFileService: _projectFileService,
            projectFilePath: openedProject.FullPath,
            protectedWorkflowImporter: _protectedWorkflowImporter);
    }
}
