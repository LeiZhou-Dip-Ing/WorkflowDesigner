using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Services.Ui;

namespace WorkflowCore.WpfDemo.Services.Drafts;

/// <summary>Persists the local editor draft after a quiet period without changing the explicit Save baseline.</summary>
public sealed class LocalDraftAutosave : IDisposable
{
    private readonly ILocalDraftStore _draftStore;
    private readonly IEditorDocumentPersistence _persistence;
    private readonly EditorSession _session;
    private readonly IUiTimer _timer;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly object _pendingSnapshotLock = new();
    private bool _disposed;
    private bool _enabled;
    private string _lastWorkflowJson = string.Empty;
    private string? _pendingWorkflowJson;
    private bool _pendingIsDirty;

    public LocalDraftAutosave(
        ILocalDraftStore draftStore,
        IEditorDocumentPersistence persistence,
        EditorSession session,
        IUiTimerFactory timerFactory)
    {
        _draftStore = draftStore ?? throw new ArgumentNullException(nameof(draftStore));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        ArgumentNullException.ThrowIfNull(timerFactory);
        _timer = timerFactory.Create(TimeSpan.FromMilliseconds(750));
        _timer.Tick += TimerOnTick;
    }

    public event EventHandler? StateChanged;
    public event EventHandler? AutosaveCompleted;
    public event EventHandler<LocalDraftSaveFailedEventArgs>? SaveFailed;

    public bool HasLocalDraft { get; private set; }
    public bool HasLoadFailure { get; private set; }
    public bool IsDirty { get; private set; }
    public bool IsSuspended { get; set; }
    public bool IsSaving => _saveLock.CurrentCount == 0;

    public string LoadMostRecent()
    {
        LocalDraftSnapshot? draft;
        try
        {
            draft = _draftStore.LoadMostRecent();
        }
        catch (InvalidDataException exception)
        {
            Debug.WriteLine(exception);
            HasLoadFailure = true;
            return exception.Message;
        }

        if (draft == null)
        {
            return "Offline. Waiting for Workflow Runtime; local method editing remains available.";
        }

        return RestoreSnapshot(draft, savedProjectJson: null);
    }

    public string RestoreProjectDraft(
        Guid projectId,
        string savedProjectJson,
        DateTimeOffset projectFileSavedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(savedProjectJson);
        LocalDraftSnapshot? draft;
        try
        {
            draft = _draftStore.Load(projectId.ToString("D"));
        }
        catch (InvalidDataException exception)
        {
            Debug.WriteLine(exception);
            HasLoadFailure = true;
            return $"Opened the local Project file. {exception.Message}";
        }

        if (draft == null)
        {
            return "Opened the local Project file; no recovery draft exists for this Project.";
        }

        HasLocalDraft = true;
        if (draft.SavedAtUtc <= projectFileSavedAtUtc)
        {
            IsDirty = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return "Opened the local Project file; its recovery draft is not newer.";
        }

        return RestoreSnapshot(draft, savedProjectJson);
    }

    private string RestoreSnapshot(LocalDraftSnapshot draft, string? savedProjectJson)
    {
        try
        {
            var workingProject = _persistence.Deserialize(draft.Workflow.ToJsonString());
            var workingProjectJson = _persistence.Serialize(workingProject);
            var baselineJson = savedProjectJson;
            if (string.IsNullOrWhiteSpace(baselineJson))
            {
                var savedProject = _persistence.Deserialize(
                    (draft.SavedWorkflow ?? draft.Workflow).ToJsonString());
                baselineJson = _persistence.Serialize(savedProject);
            }

            _session.Project = workingProject;
            _session.SavedProjectJson = baselineJson;
            HasLocalDraft = true;
            HasLoadFailure = false;
            IsDirty = !string.Equals(workingProjectJson, baselineJson, StringComparison.Ordinal);
            _lastWorkflowJson = workingProjectJson;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return IsDirty
                ? $"Recovered newer local draft '{draft.WorkflowId}' with unsaved changes."
                : $"Loaded saved local draft '{draft.WorkflowId}'.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException)
        {
            Debug.WriteLine(exception);
            HasLoadFailure = true;
            return $"The local workflow draft '{draft.WorkflowId}' could not be restored: {exception.Message}";
        }
    }

    public void Start(string initialWorkflowJson)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _lastWorkflowJson = initialWorkflowJson ?? string.Empty;
        lock (_pendingSnapshotLock)
        {
            _pendingWorkflowJson = null;
            _pendingIsDirty = false;
        }
        _enabled = true;
        _timer.Start();
    }

    public void QueueSnapshot(string workflowJson, bool isDirty)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowJson);
        if (!_enabled || IsSuspended)
        {
            return;
        }

        lock (_pendingSnapshotLock)
        {
            if (string.Equals(workflowJson, _lastWorkflowJson, StringComparison.Ordinal))
            {
                _pendingWorkflowJson = null;
                _pendingIsDirty = isDirty;
            }
            else
            {
                _pendingWorkflowJson = workflowJson;
                _pendingIsDirty = isDirty;
            }
        }

        IsDirty = isDirty;
        StateChanged?.Invoke(this, EventArgs.Empty);
        _timer.Stop();
        _timer.Start();
    }

    public async Task PersistIfChangedAsync(CancellationToken cancellationToken = default)
    {
        if (!_enabled || IsSuspended || IsSaving)
        {
            return;
        }

        string? workflowJson;
        bool isDirty;
        lock (_pendingSnapshotLock)
        {
            workflowJson = _pendingWorkflowJson;
            isDirty = _pendingIsDirty;
        }

        if (string.IsNullOrWhiteSpace(workflowJson)
            || string.Equals(workflowJson, _lastWorkflowJson, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            await SaveSnapshotAsync(
                    workflowJson,
                    isDirty,
                    cancellationToken)
                .ConfigureAwait(false);
            AutosaveCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            SaveFailed?.Invoke(this, new LocalDraftSaveFailedEventArgs(exception));
        }
    }

    public async Task SaveSnapshotAsync(
        string workflowJson,
        bool isDirty,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var workflow = JsonNode.Parse(workflowJson)
                ?? throw new InvalidOperationException("The editor produced an empty workflow draft.");
            var savedWorkflow = JsonNode.Parse(
                string.IsNullOrWhiteSpace(_session.SavedProjectJson)
                    ? workflowJson
                    : _session.SavedProjectJson)
                ?? throw new InvalidOperationException("The saved local Project snapshot is empty.");
            await _draftStore.SaveAsync(
                    GetCurrentProjectId(),
                    workflow,
                    savedWorkflow,
                    isDirty,
                    cancellationToken)
                .ConfigureAwait(false);
            _lastWorkflowJson = workflowJson;
            lock (_pendingSnapshotLock)
            {
                if (string.Equals(_pendingWorkflowJson, workflowJson, StringComparison.Ordinal))
                {
                    _pendingWorkflowJson = null;
                    _pendingIsDirty = isDirty;
                }
            }
            HasLocalDraft = true;
            IsDirty = isDirty;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || !_enabled)
        {
            return;
        }

        _timer.Stop();
        string? workflowJson;
        lock (_pendingSnapshotLock)
        {
            workflowJson = _pendingWorkflowJson;
        }

        workflowJson ??= _persistence.Serialize(_session.Project);
        if (!IsSaving && string.Equals(workflowJson, _lastWorkflowJson, StringComparison.Ordinal))
        {
            return;
        }

        var savedProjectJson = _session.SavedProjectJson;
        var isDirty = string.IsNullOrWhiteSpace(savedProjectJson)
                      || !string.Equals(workflowJson, savedProjectJson, StringComparison.Ordinal);
        await SaveSnapshotAsync(workflowJson, isDirty, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Tick -= TimerOnTick;
        _timer.Dispose();
        _saveLock.Dispose();
    }

    private async void TimerOnTick(object? sender, EventArgs e)
    {
        try
        {
            await PersistIfChangedAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            SaveFailed?.Invoke(this, new LocalDraftSaveFailedEventArgs(exception));
        }
    }

    private string GetCurrentProjectId()
    {
        if (_session.Project.ProjectId == Guid.Empty)
        {
            throw new InvalidOperationException("A local draft cannot be saved without a stable ProjectId.");
        }

        return _session.Project.ProjectId.ToString("D");
    }
}

public sealed class LocalDraftSaveFailedEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception;
}
