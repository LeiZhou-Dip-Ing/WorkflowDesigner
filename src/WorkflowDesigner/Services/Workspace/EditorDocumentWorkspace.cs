using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Nodes;
using Prism.Commands;
using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Models;
using WorkflowCore.WpfDemo.ViewModels;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.Services.Workspace;

/// <summary>Owns open AvalonDock documents and each document's dirty and undo baseline.</summary>
public sealed class EditorDocumentWorkspace
{
    private readonly IMethodEditorViewModelFactory _methodEditorFactory;
    private readonly ICSharpScriptEditorViewModelFactory _scriptEditorFactory;
    private readonly IEditorDocumentPersistence _persistence;
    private readonly EditorSession _session;
    private readonly Dictionary<string, WorkflowDocumentEditState> _editStates =
        new(StringComparer.OrdinalIgnoreCase);
    private DockPaneItem? _selectedDockPane;
    private bool _suppressHistory;

    public EditorDocumentWorkspace(
        IMethodEditorViewModelFactory methodEditorFactory,
        ICSharpScriptEditorViewModelFactory scriptEditorFactory,
        IEditorDocumentPersistence persistence,
        EditorSession session)
    {
        _methodEditorFactory = methodEditorFactory ?? throw new ArgumentNullException(nameof(methodEditorFactory));
        _scriptEditorFactory = scriptEditorFactory ?? throw new ArgumentNullException(nameof(scriptEditorFactory));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public event EventHandler? Changed;

    public ObservableCollection<DockPaneItem> OpenedEditors { get; } = new();

    public DockPaneItem? SelectedDockPane
    {
        get => _selectedDockPane;
        set
        {
            if (ReferenceEquals(_selectedDockPane, value))
            {
                return;
            }

            _selectedDockPane = value;
            if (value?.Content is MethodEditorViewModel methodEditor)
            {
                methodEditor.Activate();
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void OpenMethod(WorkflowMethod? method, MainWindowViewModel owner)
    {
        if (method == null)
        {
            return;
        }

        var contentId = GetMethodContentId(method);
        var existing = FindPane(contentId);
        if (existing != null)
        {
            Activate(existing);
            return;
        }

        var editor = _methodEditorFactory.Create(method, owner);
        editor.IsDirty = IsDirty(contentId);
        PropertyChangedEventHandler titleChanged = (_, args) =>
        {
            if (args.PropertyName == nameof(WorkflowMethod.Name))
            {
                UpdatePaneTitle(contentId, method.Name);
            }
        };
        var pane = new DockPaneItem
        {
            ContentId = contentId,
            Title = FormatTitle(method.Name, editor.IsDirty),
            Content = editor,
            ActivatedCallback = _ => editor.Activate(),
            ClosedCallback = _ =>
            {
                method.PropertyChanged -= titleChanged;
                _methodEditorFactory.Release(editor);
            }
        };
        method.PropertyChanged += titleChanged;
        pane.CloseCommand = new DelegateCommand(() => Close(pane));
        OpenedEditors.Add(pane);
        Activate(pane);
    }

    public void OpenScript(WorkflowScript? script, MainWindowViewModel owner)
    {
        if (script == null)
        {
            return;
        }

        var contentId = GetScriptContentId(script);
        var existing = FindPane(contentId);
        if (existing != null)
        {
            Activate(existing);
            return;
        }

        var editor = _scriptEditorFactory.Create(script, owner);
        editor.IsDirty = IsDirty(contentId);
        PropertyChangedEventHandler titleChanged = (_, args) =>
        {
            if (args.PropertyName == nameof(WorkflowScript.Name))
            {
                UpdatePaneTitle(contentId, script.DisplayFileName);
            }
        };
        var pane = new DockPaneItem
        {
            ContentId = contentId,
            Title = FormatTitle(script.DisplayFileName, editor.IsDirty),
            IconKey = DocumentIconKeys.CSharpScript,
            Content = editor,
            ClosedCallback = _ =>
            {
                script.PropertyChanged -= titleChanged;
                _scriptEditorFactory.Release(editor);
            }
        };
        script.PropertyChanged += titleChanged;
        pane.CloseCommand = new DelegateCommand(() => Close(pane));
        OpenedEditors.Add(pane);
        Activate(pane);
    }

    public void Activate(DockPaneItem pane)
    {
        ArgumentNullException.ThrowIfNull(pane);
        foreach (var editor in OpenedEditors)
        {
            var isTarget = ReferenceEquals(editor, pane);
            editor.IsActive = isTarget;
            editor.IsSelected = isTarget;
        }

        pane.ActivatedCallback?.Invoke(pane);
        SelectedDockPane = pane;
    }

    public void Close(DockPaneItem pane)
    {
        ArgumentNullException.ThrowIfNull(pane);
        if (!OpenedEditors.Remove(pane))
        {
            return;
        }

        pane.ClosedCallback?.Invoke(pane);
        if (ReferenceEquals(SelectedDockPane, pane))
        {
            SelectedDockPane = OpenedEditors.LastOrDefault();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void CloseMethod(WorkflowMethod method) => CloseByContentId(GetMethodContentId(method));

    public void CloseScript(WorkflowScript script) => CloseByContentId(GetScriptContentId(script));

    public void CloseAll()
    {
        foreach (var pane in OpenedEditors.ToList())
        {
            Close(pane);
        }
    }

    public void Reset(WorkflowProject project, string? savedProjectJson = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        _editStates.Clear();
        var baselineJson = string.IsNullOrWhiteSpace(savedProjectJson)
            ? _persistence.Serialize(project)
            : savedProjectJson;
        var savedProject = _persistence.Deserialize(baselineJson);
        var savedMethods = savedProject.Methods
            .GroupBy(method => method.Uid)
            .ToDictionary(group => group.Key, group => group.First());
        var savedScripts = savedProject.Scripts
            .GroupBy(script => script.Uid)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var method in project.Methods)
        {
            var snapshot = Serialize(method);
            _editStates[GetMethodContentId(method)] = savedMethods.TryGetValue(method.Uid, out var savedMethod)
                ? CreateSavedState(Serialize(savedMethod), snapshot)
                : WorkflowDocumentEditState.CreateUnsaved(snapshot);
        }

        foreach (var script in project.Scripts)
        {
            var snapshot = Serialize(script);
            _editStates[GetScriptContentId(script)] = savedScripts.TryGetValue(script.Uid, out var savedScript)
                ? CreateSavedState(Serialize(savedScript), snapshot)
                : WorkflowDocumentEditState.CreateUnsaved(snapshot);
        }

        _session.SavedProjectJson = baselineJson;
        UpdateOpenDocumentStates();
    }

    /// <summary>
    /// Applies the downloaded runtime state document by document. Matching documents keep their
    /// object identity, so unchanged AvalonDock pages remain open and only real differences refresh.
    /// </summary>
    public void SynchronizeWithRuntimeProject(
        WorkflowProject localProject,
        WorkflowProject runtimeProject)
    {
        ArgumentNullException.ThrowIfNull(localProject);
        ArgumentNullException.ThrowIfNull(runtimeProject);

        _suppressHistory = true;
        try
        {
            localProject.Name = runtimeProject.Name;
            localProject.Version = runtimeProject.Version;
            localProject.ExtensionData = (JsonObject)runtimeProject.ExtensionData.DeepClone();
            localProject.Methods = SynchronizeMethods(localProject.Methods, runtimeProject.Methods);
            localProject.Scripts = SynchronizeScripts(localProject.Scripts, runtimeProject.Scripts);
        }
        finally
        {
            _suppressHistory = false;
        }

        UpdateOpenDocumentStates();
    }

    /// <summary>
    /// Overwrites one matching local method or script from Runtime without touching any other document.
    /// </summary>
    public WorkflowEditorDocument SynchronizeDocumentWithRuntime(
        WorkflowProject localProject,
        WorkflowEditorDocument runtimeDocument)
    {
        ArgumentNullException.ThrowIfNull(localProject);
        ArgumentNullException.ThrowIfNull(runtimeDocument);

        _suppressHistory = true;
        try
        {
            if (runtimeDocument.Method is { } runtimeMethod)
            {
                var localMethod = localProject.Methods.FirstOrDefault(method => method.Uid == runtimeMethod.Uid)
                    ?? throw new InvalidOperationException($"Local method '{runtimeMethod.Name}' no longer exists.");
                if (!DocumentsAreEquivalent(Serialize(localMethod), Serialize(runtimeMethod)))
                {
                    RestoreMethod(localMethod, runtimeMethod);
                }

                return WorkflowEditorDocument.FromMethod(localMethod);
            }

            if (runtimeDocument.Script is { } runtimeScript)
            {
                var localScript = localProject.Scripts.FirstOrDefault(script => script.Uid == runtimeScript.Uid)
                    ?? throw new InvalidOperationException($"Local script '{runtimeScript.Name}' no longer exists.");
                if (!DocumentsAreEquivalent(Serialize(localScript), Serialize(runtimeScript)))
                {
                    RestoreScript(localScript, runtimeScript);
                }

                return WorkflowEditorDocument.FromScript(localScript);
            }

            throw new InvalidOperationException("Runtime document has neither a method nor a script.");
        }
        finally
        {
            _suppressHistory = false;
            UpdateOpenDocumentStates();
        }
    }

    public void Observe(WorkflowProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (_suppressHistory)
        {
            return;
        }

        var activeContentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var method in project.Methods)
        {
            Observe(GetMethodContentId(method), Serialize(method), activeContentIds);
        }

        foreach (var script in project.Scripts)
        {
            Observe(GetScriptContentId(script), Serialize(script), activeContentIds);
        }

        foreach (var removedContentId in _editStates.Keys
                     .Where(contentId => !activeContentIds.Contains(contentId))
                     .ToList())
        {
            _editStates.Remove(removedContentId);
        }

        UpdateOpenDocumentStates();
    }

    public void BeginEdit(WorkflowMethod method)
    {
        if (_suppressHistory)
        {
            return;
        }

        var contentId = GetMethodContentId(method);
        var snapshot = Serialize(method);
        if (!_editStates.TryGetValue(contentId, out var state))
        {
            state = WorkflowDocumentEditState.CreateUnsaved(snapshot);
            _editStates[contentId] = state;
        }

        state.BeginEdit(snapshot);
    }

    /// <summary>
    /// Opens one method-level Undo transaction. Every model mutation performed before the returned
    /// scope is disposed is restored by a single Undo, including changes made by nested property editors.
    /// </summary>
    public IDisposable BeginEditScope(WorkflowMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        BeginEdit(method);
        return new MethodEditScope(this, method);
    }

    public void CompleteEdit(WorkflowMethod method)
    {
        if (_suppressHistory)
        {
            return;
        }

        if (_editStates.TryGetValue(GetMethodContentId(method), out var state))
        {
            state.CompleteEdit(Serialize(method));
            UpdateOpenDocumentStates();
        }
    }

    public bool HasUnsavedDocuments(WorkflowProject project)
        => _editStates.Values.Any(state => state.IsDirty) || HasUnsavedProjectStructure(project);

    public bool IsSelectedDocumentDirty()
        => SelectedDockPane?.Content is IEditableDockDocument document && IsDirty(document.ContentId);

    public bool IsDirty(string contentId)
        => _editStates.TryGetValue(contentId, out var state) && state.IsDirty;

    public IReadOnlyList<string> GetUnsavedDocumentNames(WorkflowProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var names = new List<string>();
        names.AddRange(project.Methods
            .Where(method => IsDirty(GetMethodContentId(method)))
            .Select(method => method.Name));
        names.AddRange(project.Scripts
            .Where(script => IsDirty(GetScriptContentId(script)))
            .Select(script => script.Name));

        if (!string.IsNullOrWhiteSpace(_session.SavedProjectJson))
        {
            var savedProject = _persistence.Deserialize(_session.SavedProjectJson);
            var currentMethodIds = project.Methods.Select(method => method.Uid).ToHashSet();
            var currentScriptIds = project.Scripts.Select(script => script.Uid).ToHashSet();
            names.AddRange(savedProject.Methods
                .Where(method => !currentMethodIds.Contains(method.Uid))
                .Select(method => $"{method.Name} (deleted)"));
            names.AddRange(savedProject.Scripts
                .Where(script => !currentScriptIds.Contains(script.Uid))
                .Select(script => $"{script.Name} (deleted)"));
        }

        return names;
    }

    public IEditableDockDocument? ResolveEditableDocument(object? parameter)
        => parameter switch
        {
            IEditableDockDocument document => document,
            DockPaneItem { Content: IEditableDockDocument document } => document,
            _ => SelectedDockPane?.Content as IEditableDockDocument
        };

    public string GetDocumentDisplayName(IEditableDockDocument document)
        => GetDisplayName(document);

    public bool CanUndo(object? parameter)
        => ResolveEditableDocument(parameter) is { } document
           && _editStates.TryGetValue(document.ContentId, out var state)
           && state.IsDirty
           && (state.CanUndo || state.IsUnsavedCreation);

    public WorkflowDocumentUndoResult Undo(object? parameter, WorkflowProject project)
    {
        var document = ResolveEditableDocument(parameter);
        if (document == null || !_editStates.TryGetValue(document.ContentId, out var state))
        {
            return WorkflowDocumentUndoResult.None;
        }

        var documentName = GetDisplayName(document);
        var snapshot = state.Undo();
        if (snapshot == null)
        {
            if (!state.IsUnsavedCreation)
            {
                return WorkflowDocumentUndoResult.None;
            }

            _suppressHistory = true;
            try
            {
                switch (document)
                {
                    case MethodEditorViewModel methodEditor:
                        CloseMethod(methodEditor.Method);
                        project.Methods.Remove(methodEditor.Method);
                        break;
                    case CSharpScriptEditorViewModel scriptEditor:
                        CloseScript(scriptEditor.Script);
                        project.Scripts.Remove(scriptEditor.Script);
                        break;
                    default:
                        throw new InvalidOperationException("The selected document cannot be removed by Undo.");
                }

                _editStates.Remove(document.ContentId);
            }
            finally
            {
                _suppressHistory = false;
            }

            UpdateOpenDocumentStates();
            return new WorkflowDocumentUndoResult(
                WorkflowDocumentUndoKind.CreationRemoved,
                documentName,
                null,
                null);
        }

        var restored = _persistence.DeserializeDocument(snapshot);
        _suppressHistory = true;
        try
        {
            switch (document)
            {
                case MethodEditorViewModel methodEditor when restored.Method != null:
                    RestoreMethod(methodEditor.Method, restored.Method);
                    _session.SelectedMethod = methodEditor.Method;
                    break;
                case CSharpScriptEditorViewModel scriptEditor when restored.Script != null:
                    RestoreScript(scriptEditor.Script, restored.Script);
                    break;
                default:
                    throw new InvalidOperationException("The Undo snapshot does not match the selected document type.");
            }
        }
        finally
        {
            _suppressHistory = false;
        }

        UpdateOpenDocumentStates();
        return new WorkflowDocumentUndoResult(
            WorkflowDocumentUndoKind.Restored,
            documentName,
            restored.Method,
            restored.Script);
    }

    public void MarkDocumentSaved(WorkflowProject project, string contentId)
    {
        var document = FindDocument(project, contentId);
        if (document == null)
        {
            return;
        }

        MarkSaved(contentId, _persistence.SerializeDocument(document));
        UpdateOpenDocumentStates();
    }

    public void MarkAllDocumentsSaved(WorkflowProject project)
    {
        foreach (var method in project.Methods)
        {
            MarkSaved(GetMethodContentId(method), Serialize(method));
        }

        foreach (var script in project.Scripts)
        {
            MarkSaved(GetScriptContentId(script), Serialize(script));
        }

        UpdateOpenDocumentStates();
    }

    public WorkflowProject BuildProjectWithSavedDocument(
        WorkflowProject currentProject,
        WorkflowEditorDocument document)
    {
        var savedProject = string.IsNullOrWhiteSpace(_session.SavedProjectJson)
            ? new WorkflowProject { Name = currentProject.Name, Version = currentProject.Version }
            : _persistence.Deserialize(_session.SavedProjectJson);
        var clonedDocument = _persistence.DeserializeDocument(_persistence.SerializeDocument(document));
        UpsertSavedDocument(savedProject, clonedDocument);
        return savedProject;
    }

    public void UpdateOpenDocumentStates()
    {
        foreach (var pane in OpenedEditors)
        {
            if (pane.Content is not IEditableDockDocument document)
            {
                continue;
            }

            document.IsDirty = IsDirty(document.ContentId);
            pane.Title = FormatTitle(GetDisplayName(document), document.IsDirty);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public static void UpsertSavedDocument(WorkflowProject savedProject, WorkflowEditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(savedProject);
        ArgumentNullException.ThrowIfNull(document);
        if (document.Method is { } method)
        {
            var index = savedProject.Methods.FindIndex(existing => existing.Uid == method.Uid);
            if (index >= 0) savedProject.Methods[index] = method;
            else savedProject.Methods.Add(method);
        }
        else if (document.Script is { } script)
        {
            var index = savedProject.Scripts.FindIndex(existing => existing.Uid == script.Uid);
            if (index >= 0) savedProject.Scripts[index] = script;
            else savedProject.Scripts.Add(script);
        }
    }

    public static string GetMethodContentId(WorkflowMethod method) => $"method:{method.Uid:N}";

    public static string GetScriptContentId(WorkflowScript script) => $"script:{script.Uid:N}";

    private static WorkflowDocumentEditState CreateSavedState(string savedSnapshot, string currentSnapshot)
    {
        var state = WorkflowDocumentEditState.CreateSaved(savedSnapshot);
        state.Observe(currentSnapshot);
        return state;
    }

    private void Observe(string contentId, string snapshot, ISet<string> activeContentIds)
    {
        activeContentIds.Add(contentId);
        if (_editStates.TryGetValue(contentId, out var state)) state.Observe(snapshot);
        else _editStates[contentId] = WorkflowDocumentEditState.CreateUnsaved(snapshot);
    }

    private bool HasUnsavedProjectStructure(WorkflowProject project)
    {
        if (string.IsNullOrWhiteSpace(_session.SavedProjectJson))
        {
            return project.Methods.Count > 0 || project.Scripts.Count > 0 || project.ScriptLibraries.Count > 0;
        }

        var savedProject = _persistence.Deserialize(_session.SavedProjectJson);
        return !savedProject.Methods.Select(method => method.Uid).ToHashSet()
                   .SetEquals(project.Methods.Select(method => method.Uid))
               || !savedProject.Scripts.Select(script => script.Uid).ToHashSet()
                   .SetEquals(project.Scripts.Select(script => script.Uid))
               || !savedProject.ScriptLibraries.Select(CreateLibraryIdentity).ToHashSet(StringComparer.OrdinalIgnoreCase)
                   .SetEquals(project.ScriptLibraries.Select(CreateLibraryIdentity));
    }

    private static string CreateLibraryIdentity(SharpScriptLibraryReferenceDto reference)
        => $"{reference.LibraryId}|{reference.Version}";

    private WorkflowEditorDocument? FindDocument(WorkflowProject project, string contentId)
        => project.Methods
            .Where(method => string.Equals(GetMethodContentId(method), contentId, StringComparison.OrdinalIgnoreCase))
            .Select(WorkflowEditorDocument.FromMethod)
            .Concat(project.Scripts
                .Where(script => string.Equals(GetScriptContentId(script), contentId, StringComparison.OrdinalIgnoreCase))
                .Select(WorkflowEditorDocument.FromScript))
            .FirstOrDefault();

    private void MarkSaved(string contentId, string snapshot)
    {
        if (_editStates.TryGetValue(contentId, out var state)) state.MarkSaved(snapshot);
        else _editStates[contentId] = WorkflowDocumentEditState.CreateSaved(snapshot);
    }

    private string Serialize(WorkflowMethod method)
        => _persistence.SerializeDocument(WorkflowEditorDocument.FromMethod(method));

    private string Serialize(WorkflowScript script)
        => _persistence.SerializeDocument(WorkflowEditorDocument.FromScript(script));

    private List<WorkflowMethod> SynchronizeMethods(
        IReadOnlyCollection<WorkflowMethod> localMethods,
        IReadOnlyCollection<WorkflowMethod> runtimeMethods)
    {
        var localByUid = localMethods
            .GroupBy(method => method.Uid)
            .ToDictionary(group => group.Key, group => group.First());
        var runtimeUids = runtimeMethods.Select(method => method.Uid).ToHashSet();

        foreach (var removedMethod in localMethods.Where(method => !runtimeUids.Contains(method.Uid)).ToList())
        {
            CloseMethod(removedMethod);
            _editStates.Remove(GetMethodContentId(removedMethod));
        }

        var synchronized = new List<WorkflowMethod>(runtimeMethods.Count);
        foreach (var runtimeMethod in runtimeMethods)
        {
            if (!localByUid.TryGetValue(runtimeMethod.Uid, out var localMethod))
            {
                synchronized.Add(runtimeMethod);
                continue;
            }

            if (!DocumentsAreEquivalent(Serialize(localMethod), Serialize(runtimeMethod)))
            {
                RestoreMethod(localMethod, runtimeMethod);
            }

            synchronized.Add(localMethod);
        }

        return synchronized;
    }

    private List<WorkflowScript> SynchronizeScripts(
        IReadOnlyCollection<WorkflowScript> localScripts,
        IReadOnlyCollection<WorkflowScript> runtimeScripts)
    {
        var localByUid = localScripts
            .GroupBy(script => script.Uid)
            .ToDictionary(group => group.Key, group => group.First());
        var runtimeUids = runtimeScripts.Select(script => script.Uid).ToHashSet();

        foreach (var removedScript in localScripts.Where(script => !runtimeUids.Contains(script.Uid)).ToList())
        {
            CloseScript(removedScript);
            _editStates.Remove(GetScriptContentId(removedScript));
        }

        var synchronized = new List<WorkflowScript>(runtimeScripts.Count);
        foreach (var runtimeScript in runtimeScripts)
        {
            if (!localByUid.TryGetValue(runtimeScript.Uid, out var localScript))
            {
                synchronized.Add(runtimeScript);
                continue;
            }

            if (!DocumentsAreEquivalent(Serialize(localScript), Serialize(runtimeScript)))
            {
                RestoreScript(localScript, runtimeScript);
            }

            synchronized.Add(localScript);
        }

        return synchronized;
    }

    private static bool DocumentsAreEquivalent(string first, string second)
        => WorkflowJsonComparer.AreEquivalent(JsonNode.Parse(first), JsonNode.Parse(second));

    private DockPaneItem? FindPane(string contentId)
        => OpenedEditors.FirstOrDefault(editor =>
            string.Equals(editor.ContentId, contentId, StringComparison.OrdinalIgnoreCase));

    private void CloseByContentId(string contentId)
    {
        var pane = FindPane(contentId);
        if (pane != null) Close(pane);
    }

    private void UpdatePaneTitle(string contentId, string title)
    {
        var pane = FindPane(contentId);
        if (pane != null) pane.Title = FormatTitle(title, IsDirty(contentId));
    }

    private static string GetDisplayName(IEditableDockDocument document)
        => document switch
        {
            MethodEditorViewModel methodEditor => methodEditor.Method.Name,
            CSharpScriptEditorViewModel scriptEditor => scriptEditor.Script.DisplayFileName,
            _ => document.Title.TrimEnd(' ', '*')
        };

    private static string FormatTitle(string title, bool isDirty) => isDirty ? $"{title} *" : title;

    private sealed class MethodEditScope(EditorDocumentWorkspace workspace, WorkflowMethod method) : IDisposable
    {
        private EditorDocumentWorkspace? _workspace = workspace;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _workspace, null);
            owner?.CompleteEdit(method);
        }
    }

    private static void RestoreMethod(WorkflowMethod target, WorkflowMethod source)
    {
        target.Name = source.Name;
        target.MethodType = source.MethodType;
        target.InitAtStart = source.InitAtStart;
        target.InitMethodName = source.InitMethodName;
        target.LastExecution = source.LastExecution;
        target.MethodLines = source.MethodLines;
        target.MethodVariables = source.MethodVariables;
        target.Inputs = source.Inputs;
        target.Outputs = source.Outputs;
        target.ExtensionData = (JsonObject)source.ExtensionData.DeepClone();
    }

    private static void RestoreScript(WorkflowScript target, WorkflowScript source)
    {
        target.Name = source.Name;
        target.Language = source.Language;
        target.Content = source.Content;
        target.ExtensionData = (JsonObject)source.ExtensionData.DeepClone();
    }
}

public enum WorkflowDocumentUndoKind
{
    None,
    Restored,
    CreationRemoved
}

public sealed record WorkflowDocumentUndoResult(
    WorkflowDocumentUndoKind Kind,
    string DocumentName,
    WorkflowMethod? Method,
    WorkflowScript? Script)
{
    public static WorkflowDocumentUndoResult None { get; } =
        new(WorkflowDocumentUndoKind.None, string.Empty, null, null);
}
