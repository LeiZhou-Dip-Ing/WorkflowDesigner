using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Data;
using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Models;
using WorkflowCore.WpfDemo.Services;
using WorkflowCore.WpfDemo.Services.Editing;
using WorkflowCore.WpfDemo.Services.Drafts;
using WorkflowCore.WpfDemo.Services.Ui;
using WorkflowCore.WpfDemo.Services.Workspace;
using WorkflowCore.WpfDemo.Services.Runtime;
using WorkflowCore.WpfDemo.Services.Scripting;
using WorkflowCore.WpfDemo.Services.Projects;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IProjectWorkspace
{
    /// <summary>Raised when method content or action bindings change so additive visual editors can refresh.</summary>
    public event EventHandler? CanvasContentChanged;

    private readonly IMethodEditorViewModelFactory _methodEditorViewModelFactory;
    private readonly ICSharpScriptEditorViewModelFactory _cSharpScriptEditorViewModelFactory;
    private readonly IEditorDocumentPersistence _documentPersistence;
    private readonly IRuntimeApiClient _runtimeApi;
    private readonly IEditorActionCatalog _actionCatalog;
    private readonly ProjectActionCatalog _projectActionCatalog;
    private readonly IEditorDialogs _dialogs;
    private readonly IEditorFileDialogs _fileDialogs;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly EditorSession _session;
    private readonly IMethodLineEditor _methodLines;
    private readonly IVariableEditor _variables;
    private readonly IActionPropertyEditor _actionProperties;
    private readonly EditorDocumentWorkspace _documents;
    private readonly LocalDraftAutosave _draftAutosave;
    private readonly ActionRunLog _actionRunLog;
    private readonly RuntimeRunSession _runSession;
    private readonly RuntimeWorkspaceSync _runtimeSync;
    private readonly RuntimeDeployment _deployment;
    private readonly MethodDeploymentTracker _methodDeploymentTracker;
    private readonly ISharpScriptTemplateFactory _scriptTemplateFactory;
    private readonly ISharpScriptLibraryManagerDialog? _scriptLibraryManagerDialog;
    private readonly IWorkflowProjectFileService? _projectFileService;
    private readonly string? _projectFilePath;
    private MethodLineViewItem? _selectedMethodLineItem;
    private readonly List<MethodLineViewItem> _allMethodLineItems = new();
    private readonly Dictionary<Guid, bool> _methodLineExpansionStates = new();
    private string _jsonPreview = string.Empty;
    private string _statusText = "Ready";
    private bool _isSynchronizingRuntime;
    private string _runtimeSynchronizationMessage = string.Empty;
    private bool _isRuntimeOnline;
    private bool _isManualSaveRunning;
    private bool _hasUnavailableActions;
    private string _unavailableActionsMessage = string.Empty;
    private bool _hasRuntimeValidationIssues;
    private string _runtimeValidationMessage = string.Empty;
    private bool _isExplorerExpanded;
    private bool _isCreateMenuOpen;
    private bool _isCreateMethodDialogOpen;
    private string _newMethodName = string.Empty;
    private string _createMethodError = string.Empty;
    private MethodVariableOverviewItem? _selectedMethodVariable;
    private WorkflowMethodParameter? _selectedMethodInput;
    private WorkflowMethodParameter? _selectedMethodOutput;
    private bool _isRenameVariableDialogOpen;
    private bool _isMethodVariablesDialogOpen;
    private string _renameVariableOriginalName = string.Empty;
    private string _renameVariableName = string.Empty;
    private string _renameVariableError = string.Empty;
    private bool _renameVariableAcrossAllMethods;
    private bool _canRenameVariableAcrossAllMethods;
    private HamburgerMenuItem? _selectedHamburgerMenuItem;
    private CreateDocumentKind _createDocumentKind = CreateDocumentKind.Method;
    private bool _isDeploymentOperationRunning;
    private bool _hasUnsavedLocalChanges;
    private bool _hasUndeployedSavedChanges;
    private bool _isDeploymentComparisonOpen;
    private string _deploymentComparisonSummary = string.Empty;
    private bool _disposed;
    private bool _isDebugPaused;

    public MainWindowViewModel(
        IMethodEditorViewModelFactory methodEditorViewModelFactory,
        ICSharpScriptEditorViewModelFactory cSharpScriptEditorViewModelFactory,
        IEditorDocumentPersistence documentPersistence,
        IRuntimeApiClient runtimeApi,
        IEditorActionCatalog actionCatalog,
        ILocalDraftStore localDraftStore,
        IEditorDialogs dialogs,
        IEditorFileDialogs fileDialogs,
        IUiDispatcher uiDispatcher,
        IUiTimerFactory timerFactory,
        EditorSession? editorSession = null,
        IMethodLineEditor? methodLines = null,
        IVariableEditor? variables = null,
        IActionPropertyEditor? actionProperties = null,
        EditorDocumentWorkspace? documents = null,
        LocalDraftAutosave? draftAutosave = null,
        ActionRunLog? actionRunLog = null,
        RuntimeRunSession? runSession = null,
        RuntimeWorkspaceSync? runtimeSync = null,
        RuntimeDeployment? deployment = null,
        MethodDeploymentTracker? methodDeploymentTracker = null,
        ISharpScriptTemplateFactory? scriptTemplateFactory = null,
        ISharpScriptLibraryManagerDialog? scriptLibraryManagerDialog = null,
        IWorkflowProjectFileService? projectFileService = null,
        string? projectFilePath = null, IProtectedWorkflowImportService? protectedWorkflowImporter = null)
    {
        _methodEditorViewModelFactory = methodEditorViewModelFactory
            ?? throw new ArgumentNullException(nameof(methodEditorViewModelFactory));
        _cSharpScriptEditorViewModelFactory = cSharpScriptEditorViewModelFactory
            ?? throw new ArgumentNullException(nameof(cSharpScriptEditorViewModelFactory));
        _documentPersistence = documentPersistence
            ?? throw new ArgumentNullException(nameof(documentPersistence));
        _runtimeApi = runtimeApi
            ?? throw new ArgumentNullException(nameof(runtimeApi));
        _actionCatalog = actionCatalog ?? throw new ArgumentNullException(nameof(actionCatalog));
        ArgumentNullException.ThrowIfNull(localDraftStore);
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _fileDialogs = fileDialogs ?? throw new ArgumentNullException(nameof(fileDialogs));
        _protectedWorkflowImporter = protectedWorkflowImporter;
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        ArgumentNullException.ThrowIfNull(timerFactory);
        _jsonPreviewRefreshTimer = timerFactory.Create(TimeSpan.FromMilliseconds(200));
        _jsonPreviewRefreshTimer.Tick += JsonPreviewRefreshTimerOnTick;
        _session = editorSession ?? new EditorSession();
        _methodLines = methodLines ?? new MethodLineEditor();
        _variables = variables ?? new VariableEditor();
        _projectActionCatalog = new ProjectActionCatalog(_actionCatalog);
        _projectActionCatalog.BindProject(_session.Project, runtimeCatalogBelongsToProject: false);
        _actionProperties = actionProperties
            ?? new ActionPropertyEditor(_projectActionCatalog, _variables);
        _documents = documents ?? new EditorDocumentWorkspace(
            _methodEditorViewModelFactory,
            _cSharpScriptEditorViewModelFactory,
            _documentPersistence,
            _session);
        _draftAutosave = draftAutosave ?? new LocalDraftAutosave(
            localDraftStore,
            _documentPersistence,
            _session,
            timerFactory);
        _actionRunLog = actionRunLog ?? new ActionRunLog(
            _actionCatalog,
            _actionProperties,
            timerFactory);
        _runSession = runSession ?? new RuntimeRunSession(
            _runtimeApi,
            _documentPersistence,
            _session);
        _runtimeSync = runtimeSync
            ?? new RuntimeWorkspaceSync(
                _runtimeApi,
                _actionCatalog,
                _documentPersistence,
                _actionProperties,
                _session);
        _deployment = deployment ?? new RuntimeDeployment(
            _runtimeApi,
            _documentPersistence,
            _dialogs,
            _session,
            _runtimeSync);
        _methodDeploymentTracker = methodDeploymentTracker
            ?? new MethodDeploymentTracker(_documentPersistence, _session);
        _scriptTemplateFactory = scriptTemplateFactory ?? new SharpScriptTemplateFactory();
        _scriptLibraryManagerDialog = scriptLibraryManagerDialog;
        _projectFileService = projectFileService;
        _projectFilePath = string.IsNullOrWhiteSpace(projectFilePath)
            ? null
            : ProjectPathIdentity.Normalize(projectFilePath);
        _runtimeApi.RuntimeEventReceived += RuntimeApiOnRuntimeEventReceived;
        _runtimeApi.ActionCatalogChanged += RuntimeApiOnActionCatalogChanged;
        _runtimeApi.ConnectionStateChanged += RuntimeApiOnConnectionStateChanged;
        var openedFromProjectFile = _projectFilePath != null;
        if (openedFromProjectFile)
        {
            _session.SavedProjectJson = _documentPersistence.Serialize(_session.Project);
            var projectFileSavedAtUtc = File.GetLastWriteTimeUtc(_projectFilePath!);
            _statusText = _draftAutosave.RestoreProjectDraft(
                _session.Project.ProjectId,
                _session.SavedProjectJson,
                projectFileSavedAtUtc);
        }
        else
        {
            LoadLocalDraft();
            if (!_draftAutosave.HasLocalDraft && !_draftAutosave.HasLoadFailure)
            {
                _session.Project = CreateEmptyLocalProject();
                _statusText = "Created an empty local Project.";
            }
        }
        var createdNewLocalProject = !openedFromProjectFile
                                     && !_draftAutosave.HasLocalDraft
                                     && !_draftAutosave.HasLoadFailure;

        _session.SelectedMethod = _session.Project.Methods.FirstOrDefault();

        // Commands must be initialized before any refresh method can trigger
        // RaiseCommandStates through SelectedMethod / SelectedMethodLine changes.
        NewMethodCommand = new RelayCommand(ToggleCreateMenu, () => !IsRunning);
        SelectCreateItemCommand = new RelayCommand(SelectCreateItem, _ => !IsRunning);
        DeleteMethodCommand = new RelayCommand(
            DeleteMethod,
            parameter => !IsRunning && (parameter is WorkflowMethod || SelectedMethod != null));
        AddEmptyLineCommand = new RelayCommand(AddEmptyLogLine, () => !IsRunning && SelectedMethod != null);
        DeleteLineCommand = new RelayCommand(
            DeleteSelectedLine,
            () => !IsRunning
                  && SelectedMethod != null
                  && CanDeleteSelectedLine());
        MoveLineUpCommand = new RelayCommand(() => MoveSelectedLine(-1), () => !IsRunning && SelectedMethodLine != null);
        MoveLineDownCommand = new RelayCommand(() => MoveSelectedLine(1), () => !IsRunning && SelectedMethodLine != null);
        ActivateLineCommand = new RelayCommand(
            () => SetSelectedLineActive(true),
            () => !IsRunning && SelectedMethodLine != null && !IsSelectedLineActive());
        DeactivateLineCommand = new RelayCommand(
            () => SetSelectedLineActive(false),
            () => !IsRunning && IsSelectedLineActive());
        AddIfBlockCommand = CreateAddBlockCommand(MethodBlockKind.If);
        AddForBlockCommand = CreateAddBlockCommand(MethodBlockKind.For);
        AddWhileBlockCommand = CreateAddBlockCommand(MethodBlockKind.While);
        AddElseBranchCommand = new RelayCommand(
            AddElseBranch,
            () => !IsRunning
                  && SelectedMethod != null
                  && _methodLines.CanAddElseBranch(SelectedMethod, SelectedMethodLine));
        CopyLineCommand = new RelayCommand(CopySelectedLine, CanCopySelectedLine);
        CutLineCommand = new RelayCommand(CutSelectedLine, () => !IsRunning && CanCopySelectedLine());
        PasteLineCommand = new RelayCommand(PasteLineAfterSelection, () => !IsRunning && SelectedMethod != null && _methodLines.HasCopiedLine);
        RunCommand = new RelayCommand(
            () => _ = RunSelectedMethodAsync(),
            () => !IsRunning && IsRuntimeOnline && SelectedMethod != null);
        StepRunCommand = new RelayCommand(
            () => _ = RunSelectedMethodAsync(stepMode: true),
            () => !IsRunning && IsRuntimeOnline && SelectedMethod != null);
        StepCommand = new RelayCommand(
            () => _ = StepAsync(),
            () => IsRunning && IsStepRun && IsDebugPaused);
        ContinueCommand = new RelayCommand(
            () => _ = ContinueAsync(),
            () => IsRunning && IsStepRun && IsDebugPaused);
        PauseCommand = new RelayCommand(
            () => _ = PauseAsync(),
            () => IsRunning && IsStepRun && !IsDebugPaused);
        CancelCommand = new RelayCommand(() => _ = CancelRunAsync(), () => IsRunning);
        SaveWorkflowCommand = new RelayCommand(
            () => _ = SaveSelectedDocumentAsync(),
            () => !IsRunning && !_isManualSaveRunning && IsSelectedDocumentDirty());
        SaveAllWorkflowCommand = new RelayCommand(
            () => _ = SaveAllWorkflowAsync(),
            () => !IsRunning && !_isManualSaveRunning && (HasUnsavedDocuments() || _draftAutosave.IsDirty));
        DeployWorkflowCommand = new RelayCommand(
            () => _ = DeployWorkflowAsync(),
            () => IsRuntimeOnline && !IsRunning && !_isManualSaveRunning && !_isDeploymentOperationRunning);
        DeploySelectedDocumentCommand = new RelayCommand(
            parameter => _ = DeploySelectedDocumentAsync(parameter),
            parameter => IsRuntimeOnline
                         && IsCurrentProjectActive
                         && !IsRunning
                         && !_isManualSaveRunning
                         && !_isDeploymentOperationRunning
                         && ResolveEditableDocument(parameter) != null);
        DownloadWorkflowCommand = new RelayCommand(
            () => _ = DownloadWorkflowFromRuntimeAsync(),
            () => IsRuntimeOnline && !IsRunning && !_isManualSaveRunning && !_isDeploymentOperationRunning);
        CompareWorkflowCommand = new RelayCommand(
            () => _ = CompareWorkflowWithRuntimeAsync(),
            () => IsRuntimeOnline && IsCurrentProjectActive && !_isDeploymentOperationRunning);
        CloseDeploymentComparisonCommand = new RelayCommand(() => IsDeploymentComparisonOpen = false);
        UndoCommand = new RelayCommand(
            UndoSelectedDocument,
            parameter => !IsRunning && !_isManualSaveRunning && CanUndoSelectedDocument(parameter));
        ExportJsonCommand = new RelayCommand(
            ExportJson,
            () => !IsRunning && SelectedDockPane?.Content is IExportableDockDocument);
        ImportJsonCommand = new RelayCommand(ImportJson, () => !IsRunning);
        ExportProjectJsonCommand = new RelayCommand(ExportProjectJson, () => !IsRunning);
        ImportProjectJsonCommand = new RelayCommand(() => _ = ImportProjectAsync(), () => !IsRunning);
        ClearLogCommand = new RelayCommand(ClearLog);
        ToggleExplorerCommand = new RelayCommand(ToggleExplorer);
        ExpandExplorerCommand = new RelayCommand(() => IsExplorerExpanded = true);
        CollapseExplorerCommand = new RelayCommand(CollapseExplorer);
        SetMethodTypeCommand = new RelayCommand(SetSelectedMethodType, _ => !IsRunning && SelectedMethod != null);
        RefreshVariablesCommand = new RelayCommand(RefreshMethodVariablesFromActions);
        RenameVariableCommand = new RelayCommand(ShowRenameVariableDialog, () => !IsRunning && SelectedMethodVariable != null);
        CreatePropertyValueCommand = new RelayCommand(
            CreatePropertyValue,
            parameter => !IsRunning && parameter is ActionPropertyItem { AllowCreate: true });
        ClearPropertyValueCommand = new RelayCommand(
            ClearPropertyValue,
            parameter => !IsRunning && parameter is ActionPropertyItem { AllowClear: true, IsReadOnly: false });
        OpenSelectedMethodCommand = new RelayCommand(() => OpenMethod(SelectedMethod), () => SelectedMethod != null);
        OpenMethodCommand = new RelayCommand(parameter => OpenMethod(parameter as WorkflowMethod));
        OpenScriptCommand = new RelayCommand(parameter => OpenScript(parameter as WorkflowScript));
        DeleteScriptCommand = new RelayCommand(
            DeleteScript,
            parameter => !IsRunning && parameter is WorkflowScript);
        ManageScriptLibrariesCommand = new RelayCommand(ManageScriptLibraries, () => !IsRunning);
        SelectHamburgerMenuCommand = new RelayCommand(SelectHamburgerMenuItem);
        CloseSubmenuCommand = new RelayCommand(CloseSubmenu);
        ConfirmCreateMethodCommand = new RelayCommand(CreateMethodFromDialog, () => !IsRunning);
        CancelCreateMethodCommand = new RelayCommand(CloseCreateMethodDialog);
        ConfirmRenameVariableCommand = new RelayCommand(RenameSelectedMethodVariable, () => !IsRunning && IsRenameVariableDialogOpen);
        CancelRenameVariableCommand = new RelayCommand(CloseRenameVariableDialog);
        ConfigureMethodVariablesCommand = new RelayCommand(
            ShowMethodVariablesDialog,
            () => !IsRunning && SelectedMethod != null);
        CloseMethodVariablesCommand = new RelayCommand(CloseMethodVariablesDialog);
        AddMethodInputCommand = new RelayCommand(AddMethodInput, () => !IsRunning && SelectedMethod != null);
        AddMethodOutputCommand = new RelayCommand(AddMethodOutput, () => !IsRunning && SelectedMethod != null);
        DeleteMethodInputCommand = new RelayCommand(DeleteMethodInput, () => !IsRunning && SelectedMethodInput != null);
        DeleteMethodOutputCommand = new RelayCommand(DeleteMethodOutput, () => !IsRunning && SelectedMethodOutput != null);

        ActionToolbox = CreateActionToolbox();
        HamburgerMenuItems = CreateHamburgerMenuItems();
        SelectedActionPropertiesView = CollectionViewSource.GetDefaultView(SelectedActionProperties);
        SelectedActionPropertiesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ActionPropertyItem.Category)));
        SelectedActionPropertiesView.SortDescriptions.Add(new SortDescription(nameof(ActionPropertyItem.Category), ListSortDirection.Ascending));
        SelectedActionPropertiesView.SortDescriptions.Add(new SortDescription(nameof(ActionPropertyItem.Order), ListSortDirection.Ascending));
        _documents.Changed += DocumentsOnChanged;
        _runSession.StateChanged += RunSessionOnStateChanged;
        _draftAutosave.StateChanged += DraftAutosaveOnStateChanged;
        _draftAutosave.SaveFailed += DraftAutosaveOnFailed;
        ResetDocumentEditStates(string.IsNullOrWhiteSpace(_session.SavedProjectJson) ? null : _session.SavedProjectJson);
        RefreshMethodDeploymentNotices(allowClearAgainstLocalBaseline: true);
        RefreshMethods();
        RefreshSelectedMethodLines();
        RefreshActionProperties();
        RefreshSelectedMethodVariables();
        OpenInitialProjectDocument(closeExistingDocuments: false);
        JsonPreview = _documentPersistence.Serialize(_session.Project);
        _serializedProjectJson = JsonPreview;
        _serializedContentRevision = Interlocked.Read(ref _contentRevision);
        _draftAutosave.Start(JsonPreview);
        if (createdNewLocalProject)
        {
            _ = PersistNewLocalProjectAsync(JsonPreview);
        }

        RaiseCommandStates();
        _ = InitializeRuntimeAsync();
    }

    public WorkflowProject Project
    {
        get => _session.Project;
        private set
        {
            if (!ReferenceEquals(_session.Project, value))
            {
                _session.Project = value;
                _session.ClearRuntimeProjectState();
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCurrentProjectActive));
                ResetDocumentEditStates();
                RefreshMethods();
                RefreshSelectedMethodLines();
                RefreshActionProperties();
                RefreshSelectedMethodVariables();
                RefreshJsonPreview();
            }
        }
    }

    public WorkflowMethod? SelectedMethod
    {
        get => _session.SelectedMethod;
        set
        {
            if (!ReferenceEquals(_session.SelectedMethod, value))
            {
                _session.SelectedMethod = value;
                OnPropertyChanged();
                RefreshSelectedMethodLines();
                RefreshActionProperties();
                RefreshSelectedMethodVariables();
                RefreshJsonPreview();
                RefreshSelectedVisionPreview();
                NotifySelectedMethodDeploymentNoticeChanged();
                RaiseCommandStates();
            }
        }
    }

    public MethodLine? SelectedMethodLine
    {
        get => _session.SelectedMethodLine;
        set
        {
            if (!ReferenceEquals(_session.SelectedMethodLine, value))
            {
                _session.SelectedMethodLine = value;
                OnPropertyChanged();
                SynchronizeSelectedMethodLineItem();
                RefreshActionProperties();
                RefreshSelectedVisionPreview();
                RaiseCommandStates();
            }
        }
    }

    public MethodLineViewItem? SelectedMethodLineItem
    {
        get => _selectedMethodLineItem;
        set
        {
            if (SetProperty(ref _selectedMethodLineItem, value))
            {
                SelectedMethodLine = value?.Line;
            }
        }
    }

    public ObservableCollection<WorkflowMethod> Methods { get; } = new();

    public ObservableCollection<WorkflowScript> Scripts { get; } = new();

    public ResettableObservableCollection<MethodLine> SelectedMethodLines { get; } = new();

    public ResettableObservableCollection<MethodLineViewItem> VisibleMethodLineItems { get; } = new();

    public ObservableCollection<ActionTemplateItem> ActionToolbox { get; }

    public ObservableCollection<ActionPropertyItem> SelectedActionProperties { get; } = new();

    public ICollectionView SelectedActionPropertiesView { get; }

    public ObservableCollection<RuntimeEventItem> RuntimeEvents => _actionRunLog.Events;

    public ObservableCollection<WorkflowDifferenceItem> DeploymentDifferences { get; } = new();

    public ObservableCollection<VariableItem> Variables => _actionRunLog.Variables;

    public ObservableCollection<MethodVariableOverviewItem> SelectedMethodVariables { get; } = new();

    public ObservableCollection<MethodVariableOverviewItem> SelectedMethodInputVariables { get; } = new();

    public ObservableCollection<WorkflowMethodParameter> SelectedMethodInputs { get; } = new();

    public ObservableCollection<WorkflowMethodParameter> SelectedMethodOutputs { get; } = new();

    public ObservableCollection<DockPaneItem> OpenedEditors => _documents.OpenedEditors;

    public ObservableCollection<HamburgerMenuItem> HamburgerMenuItems { get; }

    public HamburgerMenuItem? SelectedHamburgerMenuItem
    {
        get => _selectedHamburgerMenuItem;
        set
        {
            if (SetProperty(ref _selectedHamburgerMenuItem, value))
            {
                IsCreateMenuOpen = false;
                OnPropertyChanged(nameof(IsMethodsSubmenuOpen));
                OnPropertyChanged(nameof(IsScriptsSubmenuOpen));
                OnPropertyChanged(nameof(IsSubmenuOpen));
            }
        }
    }

    public MethodVariableOverviewItem? SelectedMethodVariable
    {
        get => _selectedMethodVariable;
        set
        {
            if (SetProperty(ref _selectedMethodVariable, value))
            {
                RenameVariableCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public WorkflowMethodParameter? SelectedMethodInput
    {
        get => _selectedMethodInput;
        set
        {
            if (SetProperty(ref _selectedMethodInput, value))
            {
                DeleteMethodInputCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public WorkflowMethodParameter? SelectedMethodOutput
    {
        get => _selectedMethodOutput;
        set
        {
            if (SetProperty(ref _selectedMethodOutput, value))
            {
                DeleteMethodOutputCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string JsonPreview
    {
        get => _jsonPreview;
        private set => SetProperty(ref _jsonPreview, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Updates the shell status text from additive editor controls.</summary>
    internal void SetStatusText(string message)
    {
        StatusText = message ?? string.Empty;
    }

    public bool IsRuntimeOnline
    {
        get => _isRuntimeOnline;
        private set
        {
            if (SetProperty(ref _isRuntimeOnline, value))
            {
                OnPropertyChanged(nameof(RuntimeConnectionText));
                OnPropertyChanged(nameof(DeploymentStatusText));
                OnPropertyChanged(nameof(IsDeploymentSynchronized));
                UpdateDeploymentState();
                RaiseCommandStates();
            }
        }
    }

    public string RuntimeConnectionText => IsRuntimeOnline ? "Runtime Online" : "Offline Draft";

    public bool HasUnsavedLocalChanges
    {
        get => _hasUnsavedLocalChanges;
        private set
        {
            if (SetProperty(ref _hasUnsavedLocalChanges, value))
            {
                OnPropertyChanged(nameof(DeploymentStatusText));
                OnPropertyChanged(nameof(IsDeploymentSynchronized));
            }
        }
    }

    public bool HasUndeployedSavedChanges
    {
        get => _hasUndeployedSavedChanges;
        private set
        {
            if (SetProperty(ref _hasUndeployedSavedChanges, value))
            {
                OnPropertyChanged(nameof(DeploymentStatusText));
                OnPropertyChanged(nameof(IsDeploymentSynchronized));
            }
        }
    }

    public bool IsDeploymentSynchronized
        => IsRuntimeOnline
           && IsCurrentProjectActive
           && _session.RuntimeProjectJson != null
           && !HasUnsavedLocalChanges
           && !HasUndeployedSavedChanges;

    public bool IsCurrentProjectActive => _session.IsCurrentProjectActive;

    public bool IsDeploymentComparisonOpen
    {
        get => _isDeploymentComparisonOpen;
        private set => SetProperty(ref _isDeploymentComparisonOpen, value);
    }

    public string DeploymentComparisonSummary
    {
        get => _deploymentComparisonSummary;
        private set => SetProperty(ref _deploymentComparisonSummary, value);
    }

    public bool HasUnavailableActions
    {
        get => _hasUnavailableActions;
        private set
        {
            if (SetProperty(ref _hasUnavailableActions, value))
            {
                OnPropertyChanged(nameof(HasWorkflowIssues));
                OnPropertyChanged(nameof(WorkflowIssuesMessage));
                RaiseCommandStates();
            }
        }
    }

    public string UnavailableActionsMessage
    {
        get => _unavailableActionsMessage;
        private set
        {
            if (SetProperty(ref _unavailableActionsMessage, value))
            {
                OnPropertyChanged(nameof(WorkflowIssuesMessage));
            }
        }
    }

    public bool HasRuntimeValidationIssues
    {
        get => _hasRuntimeValidationIssues;
        private set
        {
            if (SetProperty(ref _hasRuntimeValidationIssues, value))
            {
                OnPropertyChanged(nameof(HasWorkflowIssues));
                OnPropertyChanged(nameof(WorkflowIssuesMessage));
                RaiseCommandStates();
            }
        }
    }

    public string RuntimeValidationMessage
    {
        get => _runtimeValidationMessage;
        private set
        {
            if (SetProperty(ref _runtimeValidationMessage, value))
            {
                OnPropertyChanged(nameof(WorkflowIssuesMessage));
            }
        }
    }

    public bool HasWorkflowIssues => HasUnavailableActions || HasRuntimeValidationIssues;

    public string WorkflowIssuesMessage
        => string.Join(
            Environment.NewLine,
            new[] { UnavailableActionsMessage, RuntimeValidationMessage }.Where(message => !string.IsNullOrWhiteSpace(message)));

    public bool HasSelectedMethodDeploymentWarning
        => GetSelectedMethodDeploymentNotice().Kind != MethodDeploymentNoticeKind.None;

    public string SelectedMethodDeploymentWarningText
    {
        get
        {
            var notice = GetSelectedMethodDeploymentNotice();
            return notice.Kind switch
            {
                MethodDeploymentNoticeKind.Renamed =>
                    $"Method renamed from '{notice.RuntimeName}' to '{SelectedMethod?.Name}'. "
                    + "The Runtime still has the deployed name. Save this method, then deploy the current method by UID.",
                MethodDeploymentNoticeKind.New =>
                    $"Method '{SelectedMethod?.Name}' exists only in the local Project. "
                    + "Save it, then deploy the current method to add its UID to Runtime.",
                _ => string.Empty
            };
        }
    }

    public bool IsRunning => _runSession.IsRunning;
    public bool IsStepRun => _runSession.IsStepRun;

    public bool IsDebugPaused
    {
        get => _isDebugPaused;
        private set
        {
            if (SetProperty(ref _isDebugPaused, value)) RaiseCommandStates();
        }
    }

    public bool IsExplorerExpanded
    {
        get => _isExplorerExpanded;
        private set
        {
            if (SetProperty(ref _isExplorerExpanded, value))
            {
                OnPropertyChanged(nameof(ExplorerWidth));
                OnPropertyChanged(nameof(ExplorerPanelWidth));
            }
        }
    }

    public bool IsCreateMenuOpen
    {
        get => _isCreateMenuOpen;
        set => SetProperty(ref _isCreateMenuOpen, value);
    }

    public bool IsCreateMethodDialogOpen
    {
        get => _isCreateMethodDialogOpen;
        private set => SetProperty(ref _isCreateMethodDialogOpen, value);
    }

    public string NewMethodName
    {
        get => _newMethodName;
        set
        {
            if (SetProperty(ref _newMethodName, value))
            {
                CreateMethodError = string.Empty;
            }
        }
    }

    public string CreateMethodError
    {
        get => _createMethodError;
        private set => SetProperty(ref _createMethodError, value);
    }

    public string CreateDialogTitle
        => _createDocumentKind == CreateDocumentKind.Method
            ? "Create method"
            : "Create CSharp Script";

    public string CreateNameLabel
        => _createDocumentKind == CreateDocumentKind.Method
            ? "Method name"
            : "Script name";

    public bool IsRenameVariableDialogOpen
    {
        get => _isRenameVariableDialogOpen;
        private set
        {
            if (SetProperty(ref _isRenameVariableDialogOpen, value))
            {
                ConfirmRenameVariableCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsMethodVariablesDialogOpen
    {
        get => _isMethodVariablesDialogOpen;
        private set => SetProperty(ref _isMethodVariablesDialogOpen, value);
    }

    public string RenameVariableName
    {
        get => _renameVariableName;
        set
        {
            if (SetProperty(ref _renameVariableName, value))
            {
                RenameVariableError = string.Empty;
            }
        }
    }

    public string RenameVariableError
    {
        get => _renameVariableError;
        private set => SetProperty(ref _renameVariableError, value);
    }

    public bool RenameVariableAcrossAllMethods
    {
        get => _renameVariableAcrossAllMethods;
        set => SetProperty(ref _renameVariableAcrossAllMethods, value);
    }

    public bool CanRenameVariableAcrossAllMethods
    {
        get => _canRenameVariableAcrossAllMethods;
        private set => SetProperty(ref _canRenameVariableAcrossAllMethods, value);
    }

    public GridLength ExplorerWidth => IsExplorerExpanded ? new GridLength(220) : new GridLength(48);

    public double ExplorerPanelWidth => IsExplorerExpanded ? 220d : 48d;

    public bool IsMethodsSubmenuOpen => SelectedHamburgerMenuItem?.Key == "Methods";

    public bool IsScriptsSubmenuOpen => SelectedHamburgerMenuItem?.Key == "CSharpScripts";

    public bool IsSubmenuOpen => IsMethodsSubmenuOpen || IsScriptsSubmenuOpen;

    public DockPaneItem? SelectedDockPane
    {
        get => _documents.SelectedDockPane;
        set => _documents.SelectedDockPane = value;
    }

    public string SelectedActionTitle
        => SelectedMethodLine?.Action is { } action
            ? FindActionDescriptor(action)?.DisplayName ?? $"{action.ActionType} (Unavailable)"
            : "No action selected";

    public string SelectedActionDescription
        => SelectedMethodLine?.Action == null
            ? "Select a method line to edit action properties."
            : FindActionDescriptor(SelectedMethodLine.Action) == null
                ? "This action is not available in the current Runtime catalog. Its JSON configuration is preserved."
                : FindActionDescriptor(SelectedMethodLine.Action)!.Description;

    public string RecordStatus
        => SelectedMethodLines.Count == 0
            ? "Record 0 of 0"
            : $"Record {Math.Max(SelectedMethodLines.IndexOf(SelectedMethodLine!) + 1, 1)} of {SelectedMethodLines.Count}";

    public RelayCommand NewMethodCommand { get; }

    public RelayCommand SelectCreateItemCommand { get; }

    public RelayCommand DeleteMethodCommand { get; }

    public RelayCommand AddEmptyLineCommand { get; }

    public RelayCommand DeleteLineCommand { get; }

    public RelayCommand MoveLineUpCommand { get; }

    public RelayCommand MoveLineDownCommand { get; }

    public RelayCommand ActivateLineCommand { get; }

    public RelayCommand DeactivateLineCommand { get; }

    public RelayCommand AddIfBlockCommand { get; }

    public RelayCommand AddForBlockCommand { get; }

    public RelayCommand AddWhileBlockCommand { get; }

    public RelayCommand AddElseBranchCommand { get; }

    public RelayCommand CopyLineCommand { get; }

    public RelayCommand CutLineCommand { get; }

    public RelayCommand PasteLineCommand { get; }

    public RelayCommand RunCommand { get; }

    public RelayCommand StepRunCommand { get; }

    public RelayCommand StepCommand { get; }

    public RelayCommand ContinueCommand { get; }

    public RelayCommand PauseCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand SaveWorkflowCommand { get; }

    public RelayCommand SaveAllWorkflowCommand { get; }

    public RelayCommand DeployWorkflowCommand { get; }

    public RelayCommand DeploySelectedDocumentCommand { get; }

    public RelayCommand DownloadWorkflowCommand { get; }

    public RelayCommand CompareWorkflowCommand { get; }

    public RelayCommand CloseDeploymentComparisonCommand { get; }

    public RelayCommand UndoCommand { get; }

    public RelayCommand ExportJsonCommand { get; }

    public RelayCommand ImportJsonCommand { get; }

    public RelayCommand ExportProjectJsonCommand { get; }

    public RelayCommand ImportProjectJsonCommand { get; }

    public RelayCommand ClearLogCommand { get; }

    public RelayCommand ToggleExplorerCommand { get; }

    public RelayCommand ExpandExplorerCommand { get; }

    public RelayCommand CollapseExplorerCommand { get; }

    public RelayCommand SetMethodTypeCommand { get; }

    public RelayCommand RefreshVariablesCommand { get; }

    public RelayCommand RenameVariableCommand { get; }

    public RelayCommand CreatePropertyValueCommand { get; }

    public RelayCommand ClearPropertyValueCommand { get; }

    public RelayCommand OpenSelectedMethodCommand { get; }

    public RelayCommand OpenMethodCommand { get; }

    public RelayCommand OpenScriptCommand { get; }

    public RelayCommand DeleteScriptCommand { get; }

    public RelayCommand ManageScriptLibrariesCommand { get; }

    public RelayCommand SelectHamburgerMenuCommand { get; }

    public RelayCommand CloseSubmenuCommand { get; }

    public RelayCommand ConfirmCreateMethodCommand { get; }

    public RelayCommand CancelCreateMethodCommand { get; }

    public RelayCommand ConfirmRenameVariableCommand { get; }

    public RelayCommand CancelRenameVariableCommand { get; }

    public RelayCommand ConfigureMethodVariablesCommand { get; }

    public RelayCommand CloseMethodVariablesCommand { get; }

    public RelayCommand AddMethodInputCommand { get; }

    public RelayCommand AddMethodOutputCommand { get; }

    public RelayCommand DeleteMethodInputCommand { get; }

    public RelayCommand DeleteMethodOutputCommand { get; }

    public void AddActionFromToolbox(string actionType, int? insertBeforeLineNo = null)
    {
        if (SelectedMethod == null || IsRunning)
        {
            return;
        }

        var action = CreateDefaultAction(actionType);
        if (action == null)
        {
            StatusText = $"Action '{actionType}' is not available in the Runtime catalog.";
            return;
        }

        var line = _methodLines.AddAction(
            SelectedMethod,
            action,
            insertBeforeLineNo,
            OpensChildScope);
        CompleteMethodLinesChange(line);
        StatusText = $"Added action '{action.ActionType}' to method '{SelectedMethod.Name}'.";
    }

    public void AddActionFromToolboxAfterSelection(string actionType)
    {
        if (SelectedMethod == null || IsRunning)
        {
            return;
        }

        var action = CreateDefaultAction(actionType);
        if (action == null)
        {
            StatusText = $"Action '{actionType}' is not available in the Runtime catalog.";
            return;
        }

        var line = _methodLines.AddActionAfter(
            SelectedMethod,
            SelectedMethodLine,
            action,
            OpensChildScope);
        CompleteMethodLinesChange(line);
        StatusText = $"Added action '{action.ActionType}' after the current line.";
    }

    public void MarkProjectChanged()
    {
        if (SelectedMethod != null)
        {
            _methodLines.Renumber(SelectedMethod);
        }

        PrepareProject();
        RefreshSelectedMethodLines(keepSelection: true);
        RefreshActionProperties();
        RefreshSelectedMethodVariables();
        RefreshJsonPreview();
        ScheduleCanvasContentChanged();
    }

    public void MarkDocumentChanged(WorkflowMethod method)
    {
        if (!Project.Methods.Contains(method))
        {
            return;
        }

        UpdateMethodDeploymentNotice(method, allowClear: true);
        RefreshJsonPreview();
    }

    private ObservableCollection<ActionTemplateItem> CreateActionToolbox()
    {
        var categories = _projectActionCatalog.Current.Actions
            .Where(descriptor => !descriptor.IsDeprecated)
            .GroupBy(descriptor => descriptor.Category)
            .OrderBy(group => group.Key)
            .Select(group => new ActionTemplateItem
            {
                DisplayName = group.Key,
                Children = new ObservableCollection<ActionTemplateItem>(group
                    .OrderBy(descriptor => descriptor.DisplayName)
                    .Select(CreateActionTemplateItem))
            });

        return new ObservableCollection<ActionTemplateItem>(categories);
    }

    private void ReplaceActionToolbox()
    {
        var updatedToolbox = CreateActionToolbox();
        ActionToolbox.Clear();
        foreach (var category in updatedToolbox)
        {
            ActionToolbox.Add(category);
        }
    }

    private static ObservableCollection<HamburgerMenuItem> CreateHamburgerMenuItems()
    {
        return new ObservableCollection<HamburgerMenuItem>
        {
            new()
            {
                Key = "Methods",
                Title = "Methods",
                IconKey = DocumentIconKeys.Method,
                HasSubmenu = true
            },
            new()
            {
                Key = "CSharpScripts",
                Title = "CSharp Scripts",
                IconKey = DocumentIconKeys.CSharpScript,
                HasSubmenu = true
            }
        };
    }

    private bool OpensChildScope(MethodLine line)
    {
        var role = line.Action == null ? null : FindActionDescriptor(line.Action)?.BlockRole;
        return string.Equals(role, "begin", StringComparison.OrdinalIgnoreCase)
               || string.Equals(role, "branch", StringComparison.OrdinalIgnoreCase);
    }

    private WorkflowAction? CreateDefaultAction(string actionType)
        => _actionProperties.CreateDefaultAction(actionType);

    private void CompleteMethodLinesChange(MethodLine? selection)
    {
        PrepareProject();
        RefreshSelectedMethodLines();
        SelectedMethodLine = selection;
        RefreshActionProperties();
        RefreshSelectedMethodVariables();
        RefreshJsonPreview();
        RaiseCommandStates();
    }

    private void ToggleCreateMenu()
    {
        var shouldOpen = !IsCreateMenuOpen;
        SelectedHamburgerMenuItem = null;
        IsCreateMenuOpen = shouldOpen;
    }

    private void SelectCreateItem(object? parameter)
    {
        if (parameter is not string itemKind)
        {
            return;
        }

        if (string.Equals(itemKind, "Method", StringComparison.OrdinalIgnoreCase))
        {
            ShowCreateMethodDialog();
        }
        else if (string.Equals(itemKind, "CSharpScript", StringComparison.OrdinalIgnoreCase))
        {
            ShowCreateScriptDialog();
        }
    }

    private void ShowCreateMethodDialog()
    {
        CloseSubmenu();
        IsCreateMenuOpen = false;
        _createDocumentKind = CreateDocumentKind.Method;
        NewMethodName = GetNextMethodName();
        CreateMethodError = string.Empty;
        OnPropertyChanged(nameof(CreateDialogTitle));
        OnPropertyChanged(nameof(CreateNameLabel));
        IsCreateMethodDialogOpen = true;
    }

    private void ShowCreateScriptDialog()
    {
        CloseSubmenu();
        IsCreateMenuOpen = false;
        _createDocumentKind = CreateDocumentKind.CSharpScript;
        NewMethodName = GetNextScriptName();
        CreateMethodError = string.Empty;
        OnPropertyChanged(nameof(CreateDialogTitle));
        OnPropertyChanged(nameof(CreateNameLabel));
        IsCreateMethodDialogOpen = true;
    }

    private void CloseCreateMethodDialog()
    {
        IsCreateMethodDialogOpen = false;
        CreateMethodError = string.Empty;
    }

    private void ShowRenameVariableDialog()
    {
        var variable = SelectedMethodVariable;
        if (variable == null)
        {
            return;
        }

        _renameVariableOriginalName = variable.VariableName;
        RenameVariableName = variable.VariableName;
        RenameVariableError = string.Empty;
        CanRenameVariableAcrossAllMethods = WorkflowVariableNaming.IsGlobal(variable.VariableScope);
        RenameVariableAcrossAllMethods = CanRenameVariableAcrossAllMethods;
        IsRenameVariableDialogOpen = true;
    }

    private void CloseRenameVariableDialog()
    {
        IsRenameVariableDialogOpen = false;
        RenameVariableError = string.Empty;
        _renameVariableOriginalName = string.Empty;
    }

    private void ShowMethodVariablesDialog()
    {
        if (SelectedMethod == null)
        {
            return;
        }

        RefreshSelectedMethodVariables();
        RefreshSelectedMethodContract();
        IsMethodVariablesDialogOpen = true;
        StatusText = $"Configure public inputs and outputs for '{SelectedMethod.Name}'.";
    }

    private void CloseMethodVariablesDialog()
    {
        IsMethodVariablesDialogOpen = false;
        PrepareProject();
        RefreshJsonPreview();
        StatusText = "Method input/output contract updated.";
    }

    private string GetNextMethodName()
    {
        var index = 1;
        string name;
        do
        {
            name = $"Method{index++}";
        }
        while (Project.Methods.Any(method => string.Equals(method.Name, name, StringComparison.OrdinalIgnoreCase)));

        return name;
    }

    private string GetNextScriptName()
    {
        var index = 1;
        string name;
        do
        {
            name = $"Script{index++}";
        }
        while (Project.Scripts.Any(script => string.Equals(script.Name, name, StringComparison.OrdinalIgnoreCase)));

        return name;
    }

    private void CreateMethodFromDialog()
    {
        if (_createDocumentKind == CreateDocumentKind.CSharpScript)
        {
            CreateScriptFromDialog();
            return;
        }

        var name = NewMethodName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            CreateMethodError = "Method name is required.";
            return;
        }

        if (Project.Methods.Any(method => string.Equals(method.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            CreateMethodError = $"Method '{name}' already exists.";
            return;
        }

        var method = new WorkflowMethod
        {
            Name = name,
            MethodType = WorkflowMethodType.Normal
        };

        Project.Methods.Add(method);
        _methodDeploymentTracker.MarkNew(method);
        Methods.Add(method);
        RefreshPropertyEditorSuggestions();
        SelectedMethod = method;
        OpenMethod(method);
        CloseCreateMethodDialog();
        RefreshJsonPreview();
        StatusText = $"Created method '{name}'.";
        NotifySelectedMethodDeploymentNoticeChanged();
    }

    private void DeleteMethod(object? parameter)
    {
        var method = parameter as WorkflowMethod ?? SelectedMethod;
        if (method == null)
        {
            return;
        }

        var confirmed = _dialogs.Confirm(
            "Delete method",
            $"Delete method '{method.Name}' and all of its lines?");
        if (!confirmed)
        {
            return;
        }

        var removedName = method.Name;
        CloseMethodEditor(method);
        _methodDeploymentTracker.Remove(method);
        Project.Methods.Remove(method);
        Methods.Remove(method);
        RefreshPropertyEditorSuggestions();
        if (ReferenceEquals(SelectedMethod, method))
        {
            SelectedMethod = Methods.FirstOrDefault();
        }

        RefreshJsonPreview();
        StatusText = $"Deleted method '{removedName}'.";
    }

    private void AddEmptyLogLine()
    {
        AddActionFromToolbox("log");
    }

    private void DeleteSelectedLine()
    {
        if (SelectedMethod == null || SelectedMethodLine == null)
        {
            return;
        }

        var nextSelection = _methodLines.Delete(
            SelectedMethod,
            SelectedMethodLine,
            out var deletedCount);
        if (deletedCount == 0)
        {
            return;
        }
        CompleteMethodLinesChange(nextSelection);
        StatusText = deletedCount == 1
            ? "Deleted the selected action."
            : $"Deleted the selected block ({deletedCount} actions).";
    }

    private bool CanDeleteSelectedLine()
    {
        if (SelectedMethod == null || SelectedMethodLine?.Action == null)
        {
            return false;
        }

        var role = FindActionDescriptor(SelectedMethodLine.Action)?.BlockRole;
        if (string.Equals(role, "end", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(role)
            && !IsSupportedStructuralAction(SelectedMethodLine.Action.ActionType))
        {
            return false;
        }

        return _methodLines.CanDelete(SelectedMethod, SelectedMethodLine);
    }

    private RelayCommand CreateAddBlockCommand(MethodBlockKind blockKind)
        => new(
            parameter => AddBlock(blockKind, IsSurroundParameter(parameter)),
            parameter => !IsRunning
                         && SelectedMethod != null
                         && (!IsSurroundParameter(parameter)
                             || CanSurroundSelectedLine()));

    private bool CanSurroundSelectedLine()
    {
        if (!_methodLines.CanSurround(SelectedMethodLine)
            || SelectedMethodLine?.Action == null)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(
            FindActionDescriptor(SelectedMethodLine.Action)?.BlockRole);
    }

    private void AddBlock(MethodBlockKind blockKind, bool surroundCurrent)
    {
        if (SelectedMethod == null)
        {
            return;
        }

        var inserted = _methodLines.InsertBlock(
            SelectedMethod,
            SelectedMethodLine,
            blockKind,
            surroundCurrent,
            actionType => CreateDefaultAction(actionType) ?? WorkflowAction.Create(actionType));
        if (inserted == null)
        {
            return;
        }

        CompleteMethodLinesChange(inserted);
        StatusText = surroundCurrent
            ? $"Wrapped the current action in a {blockKind.ToString().ToUpperInvariant()} block."
            : $"Inserted an empty {blockKind.ToString().ToUpperInvariant()} block.";
    }

    private void AddElseBranch()
    {
        if (SelectedMethod == null)
        {
            return;
        }

        var inserted = _methodLines.AddElseBranch(
            SelectedMethod,
            SelectedMethodLine,
            actionType => CreateDefaultAction(actionType) ?? WorkflowAction.Create(actionType));
        if (inserted == null)
        {
            return;
        }

        CompleteMethodLinesChange(inserted);
        StatusText = "Added an ELSE branch to the selected condition.";
    }

    private void SetSelectedLineActive(bool isActive)
    {
        if (SelectedMethodLine == null)
        {
            return;
        }

        CaptureSelectedMethodUndoBaseline();
        _methodLines.SetActive(SelectedMethodLine, isActive);

        CompleteMethodLinesChange(SelectedMethodLine);
        StatusText = isActive ? "Activated the selected action." : "Deactivated the selected action.";
    }

    private bool IsSelectedLineActive()
        => SelectedMethodLine is { IsActive: true }
           && SelectedMethodLine.Action?.IsActive != false;

    private bool CanCopySelectedLine()
    {
        if (SelectedMethodLine?.Action == null)
        {
            return false;
        }

        var blockRole = FindActionDescriptor(SelectedMethodLine.Action)?.BlockRole;
        return string.IsNullOrWhiteSpace(blockRole);
    }

    private void CopySelectedLine()
    {
        if (!CanCopySelectedLine() || SelectedMethodLine?.Action == null)
        {
            return;
        }

        _methodLines.Copy(SelectedMethodLine);
        PasteLineCommand.RaiseCanExecuteChanged();
        StatusText = "Copied the selected action.";
    }

    private void CutSelectedLine()
    {
        if (!CanCopySelectedLine())
        {
            return;
        }

        CopySelectedLine();
        DeleteSelectedLine();
        StatusText = "Cut the selected action.";
    }

    private void PasteLineAfterSelection()
    {
        if (SelectedMethod == null || !_methodLines.HasCopiedLine)
        {
            return;
        }

        var line = _methodLines.PasteAfter(SelectedMethod, SelectedMethodLine);
        if (line == null)
        {
            return;
        }
        CompleteMethodLinesChange(line);
        StatusText = "Pasted the action after the current line.";
    }

    private static bool IsSurroundParameter(object? parameter)
        => parameter is bool value
           ? value
           : bool.TryParse(parameter?.ToString(), out var parsed) && parsed;

    private static bool IsSupportedStructuralAction(string actionType)
        => actionType.Equals("if", StringComparison.OrdinalIgnoreCase)
           || actionType.Equals("for", StringComparison.OrdinalIgnoreCase)
           || actionType.Equals("while", StringComparison.OrdinalIgnoreCase)
           || actionType.Equals("else", StringComparison.OrdinalIgnoreCase);

    private void MoveSelectedLine(int direction)
    {
        if (SelectedMethod == null || SelectedMethodLine == null)
        {
            return;
        }

        if (!_methodLines.Move(SelectedMethod, SelectedMethodLine, direction))
        {
            return;
        }
        PrepareProject();
        RefreshSelectedMethodLines(keepSelection: true);
        RefreshJsonPreview();
    }

    internal bool CanRunDesignerPreview
        => !IsRunning && IsRuntimeOnline && SelectedMethod != null;

    internal Task RunDesignerPreviewAsync()
        => RunSelectedMethodAsync();

    public bool IsSynchronizingRuntime
    {
        get => _isSynchronizingRuntime;
        private set => SetProperty(ref _isSynchronizingRuntime, value);
    }

    public string RuntimeSynchronizationMessage
    {
        get => _runtimeSynchronizationMessage;
        private set => SetProperty(ref _runtimeSynchronizationMessage, value);
    }

    private async Task CancelRunAsync()
    {
        await _runSession.CancelAsync();
        StatusText = "Cancellation requested...";
    }

    private async Task SaveSelectedDocumentAsync()
    {
        if (_isManualSaveRunning
            || IsRunning
            || SelectedDockPane?.Content is not IEditableDockDocument selectedDocument)
        {
            return;
        }

        _isManualSaveRunning = true;
        RaiseCommandStates();
        try
        {
            NormalizeActionNames(selectedDocument.CreateExportDocument());
            PrepareProject();
            ObserveDocumentChanges();
            if (!IsDocumentDirty(selectedDocument.ContentId))
            {
                StatusText = $"Document '{GetDocumentDisplayName(selectedDocument)}' has no unsaved changes.";
                return;
            }

            var savedProject = BuildProjectWithSavedDocument(selectedDocument.CreateExportDocument());
            var savedProjectJson = _documentPersistence.Serialize(savedProject);

            if (_projectFilePath != null && _projectFileService != null)
            {
                _projectFileService.Save(_projectFilePath, savedProject);
            }

            _session.SavedProjectJson = savedProjectJson;
            MarkDocumentSaved(selectedDocument.ContentId);
            var workingProjectJson = SerializeCurrentProjectSnapshot(force: true);
            await _draftAutosave.SaveSnapshotAsync(
                workingProjectJson,
                isDirty: HasUnsavedDocuments());

            var documentName = GetDocumentDisplayName(selectedDocument);
            StatusText = $"Saved document '{documentName}' locally. Runtime was not changed.";
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            StatusText = $"Could not save the current document: {exception.Message}";
        }
        finally
        {
            _isManualSaveRunning = false;
            RaiseCommandStates();
        }
    }

    private void ExportJson()
    {
        if (SelectedDockPane?.Content is not IExportableDockDocument exportableDocument)
        {
            StatusText = "Select an AvalonDock document before exporting.";
            return;
        }

        var document = exportableDocument.CreateExportDocument();
        var exportTitle = exportableDocument is IEditableDockDocument editableDocument
            ? GetDocumentDisplayName(editableDocument)
            : exportableDocument.Title;
        var documentName = string.IsNullOrWhiteSpace(exportTitle)
            ? "workflow-document"
            : exportTitle.Trim();
        var filePath = _fileDialogs.SelectDocumentExportPath(
            documentName,
            $"{CreateSafeFileName(documentName)}.json");
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        try
        {
            NormalizeActionNames(document);
            PrepareProject();
            _documentPersistence.ExportDocument(document, filePath);
            StatusText = $"Exported document '{documentName}': {filePath}";
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            Debug.WriteLine(exception);
            StatusText = $"Could not export document '{documentName}': {exception.Message}";
        }
    }

    private async Task SaveAllWorkflowAsync()
    {
        if (_isManualSaveRunning || IsRunning)
        {
            return;
        }

        _isManualSaveRunning = true;
        RaiseCommandStates();
        try
        {
            NormalizeActionNames(Project.Methods);
            PrepareProject();
            ObserveDocumentChanges();
            var workflowJson = SerializeCurrentProjectSnapshot(force: true);

            if (_projectFilePath != null && _projectFileService != null)
            {
                _projectFileService.Save(_projectFilePath, Project);
            }

            _session.SavedProjectJson = workflowJson;
            MarkAllDocumentsSaved();
            await _draftAutosave.SaveSnapshotAsync(workflowJson, isDirty: false);
            StatusText = "Saved all documents locally. Runtime was not changed.";
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            StatusText = $"Could not save all documents: {exception.Message}";
        }
        finally
        {
            _isManualSaveRunning = false;
            RaiseCommandStates();
        }
    }

    private async Task DeploySelectedDocumentAsync(object? parameter)
    {
        var editableDocument = ResolveEditableDocument(parameter);
        if (editableDocument == null)
        {
            StatusText = "Open a method or CSharp script document before deploying it.";
            return;
        }

        _isDeploymentOperationRunning = true;
        WorkflowDocumentDeployResult? deploymentResult = null;
        RaiseCommandStates();
        try
        {
            await SetRuntimeSynchronizationStateAsync(
                true,
                $"Deploying saved document '{GetDocumentDisplayName(editableDocument)}' by UID...");
            var result = await _deployment.DeployDocumentAsync(
                editableDocument.CreateExportDocument(),
                IsDocumentDirty(editableDocument.ContentId));
            deploymentResult = result;
            if (result.ScriptPublication != null)
            {
                (editableDocument as CSharpScriptEditorViewModel)?.ApplyPublication(result.ScriptPublication);
            }

            if (result.Deployed && result.ScriptPublication != null)
            {
                await RefreshActionToolboxAfterScriptPublicationAsync(result.ScriptPublication);
            }
            UpdateDeploymentState();
            StatusText = result.Message;
        }
        catch (RuntimeDeploymentVerificationException exception)
        {
            Debug.WriteLine(exception);
            StatusText = exception.Message;
            _dialogs.ShowError("Document deployment verification failed", exception.Message);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            StatusText = deploymentResult?.Deployed == true
                ? $"Document was deployed, but the editor could not refresh its Action: {exception.Message}"
                : $"Document deploy failed; Runtime kept its previous revision: {exception.Message}";
            _dialogs.ShowError("Document deployment failed", StatusText);
        }
        finally
        {
            _isDeploymentOperationRunning = false;
            await SetRuntimeSynchronizationStateAsync(false, string.Empty);
            RaiseCommandStates();
        }
    }

    private async Task DownloadWorkflowFromRuntimeAsync()
    {
        _isDeploymentOperationRunning = true;
        RaiseCommandStates();
        try
        {
            await SetRuntimeSynchronizationStateAsync(true, "Downloading the Runtime Project...");
            var result = await _deployment.DownloadProjectAsync(Project, HasUnsavedDocuments());
            UpdateDeploymentState();
            if (result.Choice == WorkflowDownloadChoice.Compare && result.Comparison != null)
            {
                ShowDeploymentComparison(result.Comparison);
                StatusText = result.Message;
                return;
            }

            if (result.Choice != WorkflowDownloadChoice.Synchronize || result.DownloadedProject == null)
            {
                StatusText = result.Message;
                return;
            }

            var downloadedProjectJson = _documentPersistence.Serialize(result.DownloadedProject);
            var savedToProjectFile = PersistRuntimeDownloadToProjectFile(result.DownloadedProject);

            _draftAutosave.IsSuspended = true;
            try
            {
                CloseAllMethodEditors();
                Project = result.DownloadedProject;
                _runtimeSync.ApplyRuntimeSnapshot(result.RuntimeDocument);
                PrepareProject();
                _session.SavedProjectJson = downloadedProjectJson;
                ResetDocumentEditStates(downloadedProjectJson);
                OpenInitialProjectDocument(closeExistingDocuments: false);
                RefreshJsonPreview();
                IsDeploymentComparisonOpen = false;
                ClearRuntimeEvents();
                Variables.Clear();
                RefreshSelectedMethodVariables();
                await _draftAutosave.SaveSnapshotAsync(JsonPreview, isDirty: false);
            }
            finally
            {
                _draftAutosave.IsSuspended = false;
            }

            StatusText = !savedToProjectFile
                ? result.Message
                : $"{result.Message} Saved atomically to '{_projectFilePath}'.";
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            StatusText = $"Could not download the Runtime Project: {exception.Message}";
        }
        finally
        {
            _isDeploymentOperationRunning = false;
            await SetRuntimeSynchronizationStateAsync(false, string.Empty);
            RaiseCommandStates();        }
    }

    private async Task CompareWorkflowWithRuntimeAsync()
    {
        _isDeploymentOperationRunning = true;
        RaiseCommandStates();
        try
        {
            await SetRuntimeSynchronizationStateAsync(true, "Comparing the local Project with Runtime...");
            var result = await _deployment.CompareProjectAsync(Project, HasUnsavedDocuments());
            ShowDeploymentComparison(result);
            UpdateDeploymentState();
            StatusText = result.Differences.Count == 0
                ? $"The local Project matches Runtime revision {result.RuntimeDocument.Revision}."
                : $"Found {result.Differences.Count} Project difference(s).";
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            StatusText = $"Could not compare with Runtime: {exception.Message}";
        }
        finally
        {
            _isDeploymentOperationRunning = false;
            await SetRuntimeSynchronizationStateAsync(false, string.Empty);
            RaiseCommandStates();
        }
    }

    private void ShowDeploymentComparison(WorkflowComparisonResult comparison)
    {
        DeploymentDifferences.Clear();
        foreach (var difference in comparison.Differences)
        {
            DeploymentDifferences.Add(difference);
        }

        DeploymentComparisonSummary = comparison.Summary;
        IsDeploymentComparisonOpen = true;
    }

    private void UpdateDeploymentState()
    {
        RefreshMethodDeploymentNotices();
        var state = _deployment.GetState(HasUnsavedDocuments());
        HasUnsavedLocalChanges = state.HasUnsavedLocalChanges;
        HasUndeployedSavedChanges = state.HasUndeployedSavedChanges;
        OnPropertyChanged(nameof(DeploymentStatusText));
        OnPropertyChanged(nameof(IsDeploymentSynchronized));
        OnPropertyChanged(nameof(IsCurrentProjectActive));
        DeployWorkflowCommand?.RaiseCanExecuteChanged();
        DeploySelectedDocumentCommand?.RaiseCanExecuteChanged();
        DownloadWorkflowCommand?.RaiseCanExecuteChanged();
        CompareWorkflowCommand?.RaiseCanExecuteChanged();
        RefreshOpenScriptCommandStates();
    }

    private MethodDeploymentNotice GetSelectedMethodDeploymentNotice()
        => _methodDeploymentTracker.Get(SelectedMethod);

    private void RefreshMethodDeploymentNotices(bool allowClearAgainstLocalBaseline = false)
    {
        _methodDeploymentTracker.Refresh(Project, allowClearAgainstLocalBaseline);
        NotifySelectedMethodDeploymentNoticeChanged();
    }

    private void UpdateMethodDeploymentNotice(WorkflowMethod method, bool allowClear)
    {
        _methodDeploymentTracker.Update(method, allowClear);
        NotifySelectedMethodDeploymentNoticeChanged();
    }

    private void NotifySelectedMethodDeploymentNoticeChanged()
    {
        OnPropertyChanged(nameof(HasSelectedMethodDeploymentWarning));
        OnPropertyChanged(nameof(SelectedMethodDeploymentWarningText));
    }

    private void ImportJson()
    {
        var filePath = _fileDialogs.SelectDocumentImportFile();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        try
        {
            var document = _documentPersistence.ImportDocument(filePath);
            ImportDocument(document, filePath);
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            Debug.WriteLine(exception);
            StatusText = $"Could not import the document: {exception.Message}";
        }
    }

    private void ExportProjectJson()
    {
        var filePath = _fileDialogs.SelectProjectExportPath();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        try
        {
            RefreshJsonPreview();
            _documentPersistence.Export(Project, filePath);
            StatusText = $"Exported Project: {filePath}";
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            Debug.WriteLine(exception);
            StatusText = $"Could not export the Project: {exception.Message}";
        }
    }

    private async Task ImportProjectAsync()
    {
        IsCreateMenuOpen = false;
        var filePath = _fileDialogs.SelectProjectImportFile();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        try
        {
            if (string.Equals(Path.GetExtension(filePath), ".wflowx", StringComparison.OrdinalIgnoreCase))
            {
                await ImportProtectedProjectAsync(filePath);
                return;
            }

            if (TryImportStandaloneDocument(filePath))
            {
                return;
            }

            var importedProject = _documentPersistence.Import(filePath);
            var replaceProject = _dialogs.Confirm(
                "Replace entire workflow Project",
                $"Replace the entire current Project with '{importedProject.Name}'?\n\n"
                + $"The imported Project contains {importedProject.Methods.Count} method(s) and "
                + $"{importedProject.Scripts.Count} C# script(s).\n"
                + "All current Project documents will be replaced.");
            if (!replaceProject)
            {
                StatusText = "Project import cancelled; the current Project was not changed.";
                return;
            }

            Project = importedProject;
            PrepareProject();
            OpenInitialProjectDocument();
            RefreshJsonPreview();
            ClearRuntimeEvents();
            Variables.Clear();
            RefreshSelectedMethodVariables();
            StatusText = $"Imported Project: {filePath}";
            if (IsRuntimeOnline)
            {
                _ = SynchronizeRuntimeAsync();
            }
        }
        catch (Exception exception) when (exception is System.IO.IOException
                                          or UnauthorizedAccessException
                                          or JsonException
                                          or InvalidOperationException
                                          or System.Net.Http.HttpRequestException)
        {
            Debug.WriteLine(exception);
            StatusText = $"Could not import the Project: {exception.Message}";
        }
    }

    private void ImportDocument(WorkflowEditorDocument document, string filePath)
    {
        switch (document.Kind)
        {
            case WorkflowEditorDocumentKind.Method when document.Method != null:
                ImportMethod(document.Method, filePath);
                break;
            case WorkflowEditorDocumentKind.CSharpScript when document.Script != null:
                ImportScript(document.Script, filePath);
                break;
            default:
                throw new InvalidOperationException("The selected workflow document type is not supported.");
        }
    }

    private void ImportMethod(WorkflowMethod importedMethod, string filePath)
    {
        if (string.IsNullOrWhiteSpace(importedMethod.Name))
        {
            importedMethod.Name = GetNextMethodName();
        }

        var existingMethod = Project.Methods.FirstOrDefault(method =>
            method.Uid == importedMethod.Uid
            || string.Equals(method.Name, importedMethod.Name, StringComparison.OrdinalIgnoreCase));
        if (existingMethod != null)
        {
            var conflictResolution = ResolveDocumentConflict("method", importedMethod.Name);
            if (conflictResolution == DocumentImportConflictResolution.Cancel)
            {
                StatusText = $"Import cancelled; method '{importedMethod.Name}' was not changed.";
                return;
            }

            if (conflictResolution == DocumentImportConflictResolution.Overwrite)
            {
                var index = Project.Methods.IndexOf(existingMethod);
                importedMethod.Uid = existingMethod.Uid;
                CloseMethodEditor(existingMethod);
                Project.Methods[index] = importedMethod;
            }
            else
            {
                importedMethod.Name = CreateUniqueDocumentName(
                    importedMethod.Name,
                    Project.Methods.Select(method => method.Name));
                RegenerateDocumentIdentity(importedMethod);
                Project.Methods.Add(importedMethod);
            }
        }
        else
        {
            Project.Methods.Add(importedMethod);
        }

        PrepareProject();
        RefreshMethods();
        RefreshPropertyEditorSuggestions();
        SelectedMethod = importedMethod;
        OpenMethod(importedMethod);
        RefreshJsonPreview();
        StatusText = $"Imported method '{importedMethod.Name}': {filePath}";
    }

    private void ImportScript(WorkflowScript importedScript, string filePath)
    {
        if (string.IsNullOrWhiteSpace(importedScript.Name))
        {
            importedScript.Name = GetNextScriptName();
        }

        var existingScript = Project.Scripts.FirstOrDefault(script =>
            script.Uid == importedScript.Uid
            || string.Equals(script.Name, importedScript.Name, StringComparison.OrdinalIgnoreCase));
        if (existingScript != null)
        {
            var conflictResolution = ResolveDocumentConflict("C# script", importedScript.Name);
            if (conflictResolution == DocumentImportConflictResolution.Cancel)
            {
                StatusText = $"Import cancelled; C# script '{importedScript.Name}' was not changed.";
                return;
            }

            if (conflictResolution == DocumentImportConflictResolution.Overwrite)
            {
                var index = Project.Scripts.IndexOf(existingScript);
                importedScript.Uid = existingScript.Uid;
                CloseScriptEditor(existingScript);
                Project.Scripts[index] = importedScript;
            }
            else
            {
                importedScript.Name = CreateUniqueDocumentName(
                    importedScript.Name,
                    Project.Scripts.Select(script => script.Name));
                importedScript.Uid = Guid.NewGuid();
                Project.Scripts.Add(importedScript);
            }
        }
        else
        {
            Project.Scripts.Add(importedScript);
        }

        OpenScript(importedScript);
        RefreshJsonPreview();
        StatusText = $"Imported C# script '{importedScript.Name}': {filePath}";
    }

    private bool TryImportStandaloneDocument(string filePath)
    {
        if (!_documentPersistence.TryImportDocument(filePath, out var document) || document == null)
        {
            return false;
        }

        ImportDocument(document, filePath);
        return true;
    }

    private DocumentImportConflictResolution ResolveDocumentConflict(
        string documentType,
        string documentName)
        => _dialogs.ResolveDocumentImportConflict(documentType, documentName);

    internal static string CreateUniqueDocumentName(string requestedName, IEnumerable<string> existingNames)
    {
        var names = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(requestedName))
        {
            return requestedName;
        }

        for (var suffix = 1; ; suffix++)
        {
            var candidate = $"{requestedName}({suffix})";
            if (!names.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private static void RegenerateDocumentIdentity(WorkflowMethod method)
    {
        method.Uid = Guid.NewGuid();
        foreach (var line in method.MethodLines)
        {
            line.Uid = Guid.NewGuid();
            if (line.Action != null)
            {
                line.Action.Uid = Guid.NewGuid();
            }
        }

        foreach (var variable in method.MethodVariables)
        {
            variable.Uid = Guid.NewGuid();
        }
    }

    private static string CreateSafeFileName(string documentName)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var safeName = string.Concat(documentName.Select(character =>
            invalidCharacters.Contains(character) ? '_' : character)).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(safeName) ? "workflow-document" : safeName;
    }

    private void ClearLog()
    {
        ClearRuntimeEvents();
        StatusText = "Log cleared.";
    }

    private void ClearRuntimeEvents()
        => _actionRunLog.Clear();

    private void ToggleExplorer()
    {
        IsExplorerExpanded = !IsExplorerExpanded;
        CloseSubmenu();
    }

    private void CollapseExplorer()
    {
        IsExplorerExpanded = false;
        CloseSubmenu();
    }

    private void CloseSubmenu()
    {
        SelectedHamburgerMenuItem = null;
        IsCreateMenuOpen = false;
    }

    private void SelectHamburgerMenuItem(object? parameter)
    {
        if (parameter is not HamburgerMenuItem item)
        {
            return;
        }

        SelectedHamburgerMenuItem = item;
    }

    private void SetSelectedMethodType(object? parameter)
    {
        if (SelectedMethod == null || parameter is not string typeName)
        {
            return;
        }

        if (Enum.TryParse<WorkflowMethodType>(typeName, out var methodType))
        {
            SelectedMethod.MethodType = methodType;
            RefreshMethods();
            RefreshJsonPreview();
            StatusText = $"Method '{SelectedMethod.Name}' type set to {methodType}.";
        }
    }

    private void RenameSelectedMethodVariable()
    {
        if (SelectedMethod == null || string.IsNullOrWhiteSpace(_renameVariableOriginalName))
        {
            return;
        }

        var newName = RenameVariableName.Trim();
        if (!_variables.IsValidName(newName))
        {
            RenameVariableError = "Use a letter or underscore first, followed by letters, numbers, or underscores.";
            return;
        }

        if (string.Equals(newName, _renameVariableOriginalName, StringComparison.Ordinal))
        {
            CloseRenameVariableDialog();
            return;
        }

        var renameEverywhere = CanRenameVariableAcrossAllMethods && RenameVariableAcrossAllMethods;
        var methods = renameEverywhere ? Project.Methods : [SelectedMethod];
        var hasConflict = methods.Any(method =>
            method.MethodVariables.Any(variable =>
                string.Equals(variable.VariableName, newName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(variable.VariableName, _renameVariableOriginalName, StringComparison.OrdinalIgnoreCase))
            || _variables
                .Discover(method, FindActionDescriptor)
                .Any(variable => string.Equals(variable.VariableName, newName, StringComparison.OrdinalIgnoreCase)
                                 && !string.Equals(variable.VariableName, _renameVariableOriginalName, StringComparison.OrdinalIgnoreCase)));
        if (hasConflict)
        {
            RenameVariableError = $"Variable '{newName}' already exists in the selected scope.";
            return;
        }

        var oldName = _renameVariableOriginalName;
        var changes = _variables.Rename(
            Project,
            SelectedMethod,
            oldName,
            newName,
            renameEverywhere,
            FindActionDescriptor);

        CloseRenameVariableDialog();
        RefreshSelectedMethodLines(keepSelection: true);
        RefreshActionProperties();
        RefreshSelectedMethodVariables();
        SelectedMethodVariable = SelectedMethodVariables.FirstOrDefault(item =>
            string.Equals(item.VariableName, newName, StringComparison.OrdinalIgnoreCase));
        RefreshJsonPreview();
        StatusText = changes == 0
            ? $"No editable references to variable '{oldName}' were found."
            : $"Renamed variable '{oldName}' to '{newName}' in {changes} location(s).";
    }

    private async Task InitializeRuntimeAsync()
    {
        try
        {
            await _runtimeSync.ConnectAsync();
            await SetRuntimeConnectionStateAsync(true);
            await SynchronizeRuntimeAsync();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            await SetRuntimeConnectionStateAsync(false);
        }
    }

    private void RuntimeApiOnActionCatalogChanged(object? sender, ActionCatalogChangedDto catalogChanged)
    {
        if (IsRuntimeOnline
            && !string.Equals(_actionCatalog.Current.CatalogVersion, catalogChanged.CatalogVersion, StringComparison.Ordinal))
        {
            _ = ApplyActionCatalogChangeAsync(catalogChanged);
        }
    }

    private void RuntimeApiOnConnectionStateChanged(
        object? sender,
        RuntimeConnectionChangedEventArgs connectionState)
    {
        _uiDispatcher.Post(() =>
        {
            ApplyRuntimeConnectionState(connectionState.IsConnected);
            if (connectionState.IsConnected)
            {
                _ = SynchronizeRuntimeAsync();
            }
        });
    }

    private void ApplyRuntimeConnectionState(bool isConnected)
    {
        IsRuntimeOnline = isConnected;
        if (isConnected)
        {
            StatusText = "Workflow Runtime is online. Synchronizing Action Catalog and workflow checks...";
            return;
        }

        IsSynchronizingRuntime = false;
        RuntimeSynchronizationMessage = string.Empty;
        StatusText = "Offline draft mode. Methods remain editable; Runtime Run and synchronization are unavailable.";
    }

    private async Task SynchronizeRuntimeAsync()
    {
        while (IsRuntimeOnline && !_uiDispatcher.HasShutdownStarted)
        {
            try
            {
                await SetRuntimeSynchronizationStateAsync(true, "Loading Action Catalog...");
                var progress = new Progress<string>(message =>
                {
                    RuntimeSynchronizationMessage = message;
                    IsSynchronizingRuntime = true;
                });
                var result = await _runtimeSync.SynchronizeAsync(Project, progress);
                if (result == null)
                {
                    return;
                }

                await _uiDispatcher.InvokeAsync(() => ApplySynchronizationResult(result));
                await SetRuntimeSynchronizationStateAsync(false, string.Empty);
                return;
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
                if (!IsRuntimeOnline)
                {
                    await SetRuntimeSynchronizationStateAsync(false, string.Empty);
                    return;
                }

                await SetRuntimeSynchronizationStateAsync(
                    true,
                    $"Synchronization failed: {exception.Message} Retrying in 2 seconds...");
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }

    private void ApplySynchronizationResult(WorkflowSynchronizationResult result)
    {
        var runtimeCatalogBelongsToProject = IsCurrentProjectActive;
        var bindingScopeChanged = _runtimeCatalogBelongsToProject != runtimeCatalogBelongsToProject;
        _runtimeCatalogBelongsToProject = runtimeCatalogBelongsToProject;
        _projectActionCatalog.BindProject(Project, runtimeCatalogBelongsToProject);

        if (result.CatalogChanged || bindingScopeChanged)
        {
            ReplaceActionToolbox();
            RefreshSelectedMethodLines(keepSelection: true);
            var catalogCheck = _runtimeSync.CheckActionsAgainstCatalog(Project);
            ApplyCatalogCheck(catalogCheck);
            RefreshActionProperties();
            if (catalogCheck.IdentitiesChanged)
            {
                RefreshJsonPreview();
            }
        }

        UpdateDeploymentState();
        StatusText = !_session.RuntimeProjectId.HasValue
            ? "Local Project is open. Runtime Action Catalog is ready; no active Runtime Project is deployed."
            : !IsCurrentProjectActive
                ? "Local Project differs from the Runtime active Project. Only complete Project deployment is available."
                : _session.RuntimeProjectJson == null
                    ? $"This Project is active in Runtime revision {_session.RuntimeRevision}. Compare or Download can load its Runtime content."
                    : RuntimeDeployment.JsonDocumentsAreEquivalent(
                        _session.SavedProjectJson,
                        _session.RuntimeProjectJson)
                        ? $"Runtime revision {_session.RuntimeRevision} matches the saved local Project."
                        : $"Runtime revision {_session.RuntimeRevision} differs from the saved local Project. Use Compare, Deploy, or Download.";
    }

    private bool CheckWorkflowActionsAgainstCatalog()
    {
        var result = _runtimeSync.CheckActionsAgainstCatalog(Project);
        ApplyCatalogCheck(result);
        return result.IdentitiesChanged;
    }

    private void ApplyCatalogCheck(WorkflowActionCatalogCheckResult result)
    {
        HasUnavailableActions = result.HasUnavailableActions;
        UnavailableActionsMessage = result.Message;
        if (result.HasUnavailableActions)
        {
            StatusText = $"Runtime check found {result.UnavailableActions.Count} unavailable workflow Action reference(s).";
        }
    }

    private void NormalizeActionNames(WorkflowEditorDocument document)
        => _actionProperties.Normalize(document);

    private void NormalizeActionNames(IEnumerable<WorkflowMethod> methods)
        => _actionProperties.Normalize(methods);

    private void ApplyValidationSummary(WorkflowValidationSummary summary)
    {
        HasRuntimeValidationIssues = summary.HasIssues;
        RuntimeValidationMessage = summary.Message;
        if (!summary.HasIssues)
        {
            if (!HasUnavailableActions)
            {
                StatusText = "Runtime synchronization and workflow checks completed successfully.";
            }

            return;
        }

        StatusText = $"Runtime validation found {summary.IssueCount} workflow issue(s).";
    }

    private async Task SetRuntimeConnectionStateAsync(bool isConnected)
    {
        await _uiDispatcher.InvokeAsync(() => ApplyRuntimeConnectionState(isConnected));
    }

    private async Task SetRuntimeSynchronizationStateAsync(bool isSynchronizing, string message)
    {
        await _uiDispatcher.InvokeAsync(() =>
        {
            RuntimeSynchronizationMessage = message;
            IsSynchronizingRuntime = isSynchronizing;
        });
    }

    /// <summary>Provides the same catalog metadata used by the List view property editor.</summary>
    public WorkflowActionDescriptorDto? ResolveActionDescriptor(WorkflowAction action)
        => _actionProperties.FindDescriptor(action);

    private WorkflowActionDescriptorDto? FindActionDescriptor(string actionType)
        => _actionProperties.FindDescriptor(actionType);

    private WorkflowActionDescriptorDto? FindActionDescriptor(WorkflowAction action)
        => ResolveActionDescriptor(action);


    private void RefreshMethods()
    {
        Methods.Clear();
        foreach (var method in Project.Methods)
        {
            Methods.Add(method);
        }

        Scripts.Clear();
        foreach (var script in Project.Scripts)
        {
            Scripts.Add(script);
        }

        _projectActionCatalog.BindProject(Project, IsCurrentProjectActive);
        if (ActionToolbox != null)
        {
            ReplaceActionToolbox();
        }

        RefreshPropertyEditorSuggestions();
    }

    private void RefreshSelectedMethodLines(bool keepSelection = false)
    {
        var previousUid = keepSelection ? SelectedMethodLine?.Uid : null;
        var orderedLines = SelectedMethod?.MethodLines
            .OrderBy(line => line.LineNo)
            .ToArray()
            ?? Array.Empty<MethodLine>();
        SelectedMethodLines.ReplaceWith(orderedLines);
        _allMethodLineItems.Clear();
        foreach (var line in orderedLines)
        {
            var descriptor = line.Action == null ? null : FindActionDescriptor(line.Action);
            var actionTemplate = ActionToolbox
                .SelectMany(category => category.Children)
                .FirstOrDefault(item => string.Equals(
                    item.ActionType,
                    line.Action?.ActionType,
                    StringComparison.OrdinalIgnoreCase));
            var item = new MethodLineViewItem(
                line,
                descriptor,
                actionTemplate,
                !_methodLineExpansionStates.TryGetValue(line.Uid, out var expanded) || expanded,
                OnMethodLineItemChanged,
                ToggleMethodLineExpansion);
            _allMethodLineItems.Add(item);
        }

        MethodLineHierarchy.Apply(_allMethodLineItems);

        for (var index = 0; index < _allMethodLineItems.Count; index++)
        {
            var current = _allMethodLineItems[index];
            current.HasChildren = string.Equals(current.Descriptor?.BlockRole, "begin", StringComparison.OrdinalIgnoreCase)
                && index + 1 < _allMethodLineItems.Count
                && _allMethodLineItems[index + 1].DisplayNestingLevel > current.DisplayNestingLevel;
        }

        RebuildVisibleMethodLineItems();

        SelectedMethodLine = previousUid.HasValue
            ? SelectedMethodLines.FirstOrDefault(line => line.Uid == previousUid.Value) ?? SelectedMethodLines.FirstOrDefault()
            : SelectedMethodLines.FirstOrDefault();
        SynchronizeSelectedMethodLineItem();
        OnPropertyChanged(nameof(RecordStatus));
    }

    private void ToggleMethodLineExpansion(MethodLineViewItem item)
    {
        if (!item.HasChildren)
        {
            return;
        }

        item.IsExpanded = !item.IsExpanded;
        _methodLineExpansionStates[item.Line.Uid] = item.IsExpanded;
        RebuildVisibleMethodLineItems();
        if (!VisibleMethodLineItems.Contains(SelectedMethodLineItem!))
        {
            SelectedMethodLineItem = item;
        }
    }

    private void RebuildVisibleMethodLineItems()
    {
        var visibleItems = new List<MethodLineViewItem>(_allMethodLineItems.Count);
        int? collapsedLevel = null;
        foreach (var item in _allMethodLineItems)
        {
            if (collapsedLevel.HasValue)
            {
                if (item.DisplayNestingLevel > collapsedLevel.Value)
                {
                    continue;
                }

                collapsedLevel = null;
            }

            visibleItems.Add(item);
            if (item.HasChildren && !item.IsExpanded)
            {
                collapsedLevel = item.DisplayNestingLevel;
            }
        }

        VisibleMethodLineItems.ReplaceWith(visibleItems);
    }

    private void SynchronizeSelectedMethodLineItem()
    {
        var item = SelectedMethodLine == null
            ? null
            : VisibleMethodLineItems.FirstOrDefault(candidate => candidate.Line.Uid == SelectedMethodLine.Uid);
        if (!ReferenceEquals(_selectedMethodLineItem, item))
        {
            _selectedMethodLineItem = item;
            OnPropertyChanged(nameof(SelectedMethodLineItem));
        }
    }

    private void OnMethodLineItemChanged()
    {
        PrepareProject();
        RefreshSelectedMethodVariables();
        RefreshJsonPreview();
        OnPropertyChanged(nameof(RecordStatus));
    }

    public void OpenMethod(WorkflowMethod? method)
    {
        CloseSubmenu();
        _documents.OpenMethod(method, this);
    }

    public void OpenScript(WorkflowScript? script)
    {
        CloseSubmenu();
        _documents.OpenScript(script, this);
    }

    private static string GetMethodContentId(WorkflowMethod method)
        => EditorDocumentWorkspace.GetMethodContentId(method);

    private static string GetScriptContentId(WorkflowScript script)
        => EditorDocumentWorkspace.GetScriptContentId(script);

    private void ActivateDockPane(DockPaneItem pane)
        => _documents.Activate(pane);

    private void CloseDockPane(DockPaneItem pane)
        => _documents.Close(pane);

    private void CloseMethodEditor(WorkflowMethod method)
        => _documents.CloseMethod(method);

    private void CloseScriptEditor(WorkflowScript script)
        => _documents.CloseScript(script);

    private void CloseAllMethodEditors()
        => _documents.CloseAll();

    private void RefreshActionProperties()
    {
        SelectedActionProperties.Clear();
        SelectedActionPropertiesView.Refresh();
        NotifySelectedActionPresentationChanged();
        if (SelectedMethodLine is not { Action: { } action } selectedLine)
        {
            OnPropertyChanged(nameof(SelectedActionTitle));
            OnPropertyChanged(nameof(SelectedActionDescription));
            OnPropertyChanged(nameof(RecordStatus));
            return;
        }

        foreach (var property in _actionProperties.BuildProperties(
                     selectedLine,
                     SelectedMethod!,
                     Project.FindMethod,
                     GetEditorSuggestions,
                     OnSelectedActionPropertyChanged,
                     CaptureSelectedMethodUndoBaseline))
        {
            if (string.Equals(property.Name, "MethodName", StringComparison.OrdinalIgnoreCase))
            {
                property.ValueApplied += OnSelectedTargetMethodChanged;
            }
            SelectedActionProperties.Add(property);
        }
        SelectedActionPropertiesView.Refresh();
        NotifySelectedActionPresentationChanged();

        OnPropertyChanged(nameof(SelectedActionTitle));
        OnPropertyChanged(nameof(SelectedActionDescription));
        OnPropertyChanged(nameof(RecordStatus));
    }

    private void OnSelectedTargetMethodChanged(object? sender, EventArgs eventArgs)
    {
        if (sender is not ActionPropertyItem property)
        {
            return;
        }

        _uiDispatcher.Post(
            () =>
            {
                if (SelectedActionProperties.Contains(property))
                {
                    RefreshActionProperties();
                }
            },
            UiDispatchPriority.DataBinding);
    }

    private void OnSelectedActionPropertyChanged()
    {
        if (SelectedMethodLine != null)
        {
            _allMethodLineItems
                .FirstOrDefault(item => item.Line.Uid == SelectedMethodLine.Uid)
                ?.Refresh();
        }

        PrepareProject();
        RefreshSelectedMethodVariables();
        RefreshJsonPreview();
        CompleteSelectedMethodUndoEditAfterBindings();
        OnPropertyChanged(nameof(SelectedActionDescription));
        OnPropertyChanged(nameof(RecordStatus));
        StatusText = "Action properties updated.";
        ScheduleCanvasContentChanged();
    }

    private void CaptureSelectedMethodUndoBaseline()
    {
        if (SelectedMethod == null)
        {
            return;
        }

        _documents.BeginEdit(SelectedMethod);
    }

    private void CompleteSelectedMethodUndoEdit()
    {
        if (SelectedMethod == null)
        {
            return;
        }

        _documents.CompleteEdit(SelectedMethod);
    }

    private void CompleteSelectedMethodUndoEditAfterBindings()
    {
        if (_uiDispatcher.HasShutdownStarted)
        {
            CompleteSelectedMethodUndoEdit();
            return;
        }

        _uiDispatcher.Post(
            CompleteSelectedMethodUndoEdit,
            UiDispatchPriority.DataBinding);
    }

    private void RefreshSelectedMethodVariables()
    {
        var previousName = SelectedMethodVariable?.VariableName;
        SelectedMethodVariables.Clear();
        SelectedMethodInputVariables.Clear();
        if (SelectedMethod == null)
        {
            SelectedMethodVariable = null;
            ConfigureMethodVariablesCommand.RaiseCanExecuteChanged();
            return;
        }

        ThreadTaskVariables.EnsureDeclarations(SelectedMethod);
        _variables.EnsureDeclarations(SelectedMethod, FindActionDescriptor);

        foreach (var variable in _variables.Discover(SelectedMethod, FindActionDescriptor))
        {
            variable.ValueChanged += OnMethodVariableValueChanged;
            SelectedMethodVariables.Add(variable);
            if (SelectedMethod.Inputs.Any(input => string.Equals(
                    input.VariableName,
                    variable.VariableName,
                    StringComparison.OrdinalIgnoreCase)))
            {
                SelectedMethodInputVariables.Add(variable);
            }
        }

        SelectedMethodVariable = !string.IsNullOrWhiteSpace(previousName)
            ? SelectedMethodVariables.FirstOrDefault(variable =>
                  string.Equals(variable.VariableName, previousName, StringComparison.OrdinalIgnoreCase))
              ?? SelectedMethodVariables.FirstOrDefault()
            : SelectedMethodVariables.FirstOrDefault();

        RefreshPropertyEditorSuggestions();
        RefreshSelectedMethodContract();
        ConfigureMethodVariablesCommand.RaiseCanExecuteChanged();
    }

    private void RefreshMethodVariablesFromActions()
    {
        var previousCount = SelectedMethod?.MethodVariables.Count ?? 0;
        RefreshSelectedMethodVariables();
        var addedCount = Math.Max(0, (SelectedMethod?.MethodVariables.Count ?? 0) - previousCount);
        StatusText = addedCount == 0
            ? "Method variable references refreshed."
            : $"Method variable references refreshed; {addedCount} missing declaration(s) were added.";
        RefreshJsonPreview();
    }

    private void OnMethodVariableValueChanged(object? sender, EventArgs eventArgs)
    {
        PrepareProject();
        RefreshJsonPreview();
        StatusText = "Method variable settings updated.";
        RaiseCommandStates();
    }

    private IReadOnlyList<string> GetEditorSuggestions(string? dataSource)
        => dataSource?.ToLowerInvariant() switch
        {
            "methodvariables" => SelectedMethodVariables
                .Select(variable => variable.VariableName)
                .ToArray(),
            "methodvariableexpressions" => SelectedMethodVariables
                .Select(variable => variable.VariableName)
                .ToArray(),
            "threadtaskvariables" when SelectedMethod != null
                => ThreadTaskVariables.GetDeclaredNames(SelectedMethod),
            "methods" => Project.Methods
                .Select(method => method.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            _ => Array.Empty<string>()
        };

    private void RefreshPropertyEditorSuggestions()
        => _actionProperties.RefreshSuggestions(
            SelectedActionProperties,
            GetEditorSuggestions);

    private void CreatePropertyValue(object? parameter)
    {
        if (parameter is not ActionPropertyItem property || SelectedMethod == null)
        {
            return;
        }

        // Creating a property value can add a method variable and then bind the property to it.
        // The shared editor transaction keeps both model mutations in one Undo step.
        using var editScope = _documents.BeginEditScope(SelectedMethod);
        var result = _actionProperties.CreatePropertyVariable(SelectedMethod, property);
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.VariableName))
        {
            StatusText = result.Message;
            return;
        }

        RefreshSelectedMethodVariables();
        SelectedMethodVariable = SelectedMethodVariables.FirstOrDefault(item =>
            string.Equals(item.VariableName, result.VariableName, StringComparison.OrdinalIgnoreCase));
        RefreshJsonPreview();
        StatusText = result.Message;
    }

    private void ClearPropertyValue(object? parameter)
    {
        if (parameter is not ActionPropertyItem property)
        {
            return;
        }

        property.ClearValue();
        StatusText = property.Required
            ? $"{property.DisplayName} is required."
            : $"{property.DisplayName} cleared.";
    }

    private void ResetDocumentEditStates(string? savedProjectJson = null)
    {
        // Method order fields are editor-owned metadata. Normalize both sides before creating
        // the Save baseline so the first edit and Undo cannot produce a false dirty marker.
        _methodLines.Prepare(Project);
        if (!string.IsNullOrWhiteSpace(savedProjectJson))
        {
            var savedProject = _documentPersistence.Deserialize(savedProjectJson);
            _methodLines.Prepare(savedProject);
            savedProjectJson = _documentPersistence.Serialize(savedProject);
        }

        _documents.Reset(Project, savedProjectJson);
    }

    private void ObserveDocumentChanges()
        => _documents.Observe(Project);

    private bool HasUnsavedDocuments()
        => _documents.HasUnsavedDocuments(Project);

    private bool IsSelectedDocumentDirty()
        => _documents.IsSelectedDocumentDirty();

    private bool IsDocumentDirty(string contentId)
        => _documents.IsDirty(contentId);

    private IReadOnlyList<string> GetUnsavedDocumentNames()
        => _documents.GetUnsavedDocumentNames(Project);

    private bool CanUndoSelectedDocument(object? parameter)
        => _documents.CanUndo(parameter);

    private void UndoSelectedDocument(object? parameter)
    {
        var result = _documents.Undo(parameter, Project);
        if (result.Kind == WorkflowDocumentUndoKind.None)
        {
            return;
        }

        PrepareProject();
        if (result.Kind == WorkflowDocumentUndoKind.CreationRemoved)
        {
            RefreshMethods();
            RefreshPropertyEditorSuggestions();
            if (SelectedMethod != null && !Project.Methods.Contains(SelectedMethod))
            {
                SelectedMethod = Methods.FirstOrDefault();
            }
        }
        else if (result.Method != null)
        {
            RefreshSelectedMethodLines();
            RefreshActionProperties();
            RefreshSelectedMethodVariables();
        }

        RefreshJsonPreview();
        RaiseCommandStates();
        StatusText = result.Kind == WorkflowDocumentUndoKind.CreationRemoved
            ? $"Undid creation of '{result.DocumentName}'."
            : $"Undid the last unsaved change in '{result.DocumentName}'.";
    }

    private IEditableDockDocument? ResolveEditableDocument(object? parameter)
        => _documents.ResolveEditableDocument(parameter);

    private void MarkDocumentSaved(string contentId)
        => _documents.MarkDocumentSaved(Project, contentId);

    private void MarkAllDocumentsSaved()
        => _documents.MarkAllDocumentsSaved(Project);

    private WorkflowProject BuildProjectWithSavedDocument(WorkflowEditorDocument document)
        => _documents.BuildProjectWithSavedDocument(Project, document);

    internal static void UpsertSavedDocument(WorkflowProject savedProject, WorkflowEditorDocument document)
        => EditorDocumentWorkspace.UpsertSavedDocument(savedProject, document);

    private void UpdateOpenDocumentStates()
        => _documents.UpdateOpenDocumentStates();

    private string GetDocumentDisplayName(IEditableDockDocument document)
        => _documents.GetDocumentDisplayName(document);

    private void DocumentsOnChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(SelectedDockPane));
        OnPropertyChanged(nameof(OpenedEditors));
        ExportJsonCommand?.RaiseCanExecuteChanged();
        UndoCommand?.RaiseCanExecuteChanged();
        SaveWorkflowCommand?.RaiseCanExecuteChanged();
        SaveAllWorkflowCommand?.RaiseCanExecuteChanged();
        UpdateDeploymentState();
    }

    private void RefreshJsonPreview()
    {
        ObserveDocumentChanges();
        ScheduleJsonPreviewRefresh();
    }

    private void PrepareProject()
    {
        _methodLines.Prepare(Project);
    }

    private void RaiseCommandStates()
    {
        NewMethodCommand.RaiseCanExecuteChanged();
        DeleteMethodCommand.RaiseCanExecuteChanged();
        AddEmptyLineCommand.RaiseCanExecuteChanged();
        DeleteLineCommand.RaiseCanExecuteChanged();
        MoveLineUpCommand.RaiseCanExecuteChanged();
        MoveLineDownCommand.RaiseCanExecuteChanged();
        ActivateLineCommand.RaiseCanExecuteChanged();
        DeactivateLineCommand.RaiseCanExecuteChanged();
        AddIfBlockCommand.RaiseCanExecuteChanged();
        AddForBlockCommand.RaiseCanExecuteChanged();
        AddWhileBlockCommand.RaiseCanExecuteChanged();
        AddElseBranchCommand.RaiseCanExecuteChanged();
        CopyLineCommand.RaiseCanExecuteChanged();
        CutLineCommand.RaiseCanExecuteChanged();
        PasteLineCommand.RaiseCanExecuteChanged();
        RunCommand.RaiseCanExecuteChanged();
        StepRunCommand.RaiseCanExecuteChanged();
        StepCommand.RaiseCanExecuteChanged();
        ContinueCommand.RaiseCanExecuteChanged();
        PauseCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        SaveWorkflowCommand.RaiseCanExecuteChanged();
        SaveAllWorkflowCommand.RaiseCanExecuteChanged();
        DeployWorkflowCommand.RaiseCanExecuteChanged();
        DeploySelectedDocumentCommand.RaiseCanExecuteChanged();
        DownloadWorkflowCommand.RaiseCanExecuteChanged();
        CompareWorkflowCommand.RaiseCanExecuteChanged();
        UndoCommand.RaiseCanExecuteChanged();
        ExportJsonCommand.RaiseCanExecuteChanged();
        ImportJsonCommand.RaiseCanExecuteChanged();
        ExportProjectJsonCommand.RaiseCanExecuteChanged();
        ImportProjectJsonCommand.RaiseCanExecuteChanged();
        SetMethodTypeCommand.RaiseCanExecuteChanged();
        RenameVariableCommand.RaiseCanExecuteChanged();
        ConfigureMethodVariablesCommand.RaiseCanExecuteChanged();
        AddMethodInputCommand.RaiseCanExecuteChanged();
        AddMethodOutputCommand.RaiseCanExecuteChanged();
        DeleteMethodInputCommand.RaiseCanExecuteChanged();
        DeleteMethodOutputCommand.RaiseCanExecuteChanged();
        CreatePropertyValueCommand.RaiseCanExecuteChanged();
        ClearPropertyValueCommand.RaiseCanExecuteChanged();
        OpenSelectedMethodCommand.RaiseCanExecuteChanged();
        OpenScriptCommand.RaiseCanExecuteChanged();
        DeleteScriptCommand.RaiseCanExecuteChanged();
        ManageScriptLibrariesCommand.RaiseCanExecuteChanged();
        SelectCreateItemCommand.RaiseCanExecuteChanged();
        ConfirmCreateMethodCommand.RaiseCanExecuteChanged();
        ConfirmRenameVariableCommand.RaiseCanExecuteChanged();
        RefreshOpenScriptCommandStates();
    }

    private void RefreshOpenScriptCommandStates()
    {
        foreach (var scriptEditor in OpenedEditors
                     .Select(pane => pane.Content)
                     .OfType<CSharpScriptEditorViewModel>())
        {
            scriptEditor.RefreshOwnerDependentCommandStates();
        }
    }

    private enum CreateDocumentKind
    {
        Method,
        CSharpScript
    }

}
