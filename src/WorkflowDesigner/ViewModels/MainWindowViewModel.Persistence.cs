using System.Diagnostics;
using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Services.Drafts;

namespace WorkflowCore.WpfDemo.ViewModels;

/// <summary>Coordinates recoverable local drafts and workspace shutdown durability.</summary>
public sealed partial class MainWindowViewModel
{
    private static WorkflowProject CreateEmptyLocalProject()
        => new()
        {
            Name = "Untitled Project",
            Version = "1.0"
        };

    private void LoadLocalDraft()
        => _statusText = _draftAutosave.LoadMostRecent();

    private void QueueDraftSnapshot(string workflowJson)
        => _draftAutosave.QueueSnapshot(
            workflowJson,
            HasUnsavedDocuments()
            || string.IsNullOrWhiteSpace(_session.SavedProjectJson)
            || !string.Equals(workflowJson, _session.SavedProjectJson, StringComparison.Ordinal));

    private async Task PersistNewLocalProjectAsync(string workflowJson)
    {
        try
        {
            await _draftAutosave.SaveSnapshotAsync(workflowJson, isDirty: false);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            await _uiDispatcher.InvokeAsync(() =>
                StatusText = $"Could not create the local Project: {exception.Message}");
        }
    }

    internal bool PersistRuntimeDownloadToProjectFile(WorkflowProject downloadedProject)
    {
        ArgumentNullException.ThrowIfNull(downloadedProject);
        if (_projectFilePath == null || _projectFileService == null)
        {
            return false;
        }

        _projectFileService.Save(_projectFilePath, downloadedProject);
        Trace.TraceInformation(
            "Saved Runtime Project '{0}' atomically to local Project file '{1}'.",
            downloadedProject.ProjectId,
            _projectFilePath);
        return true;
    }

    private void DraftAutosaveOnStateChanged(object? sender, EventArgs e)
        => _uiDispatcher.Post(() =>
        {
            SaveAllWorkflowCommand?.RaiseCanExecuteChanged();
            UpdateDeploymentState();
        });

    private void DraftAutosaveOnFailed(object? sender, LocalDraftSaveFailedEventArgs e)
        => _uiDispatcher.Post(() =>
            StatusText = $"Could not save the local workflow draft: {e.Exception.Message}");

    public bool CanCloseEditor()
    {
        ObserveDocumentChanges();
        var unsavedDocuments = GetUnsavedDocumentNames();
        if (unsavedDocuments.Count == 0)
        {
            try
            {
                _draftAutosave.FlushAsync().GetAwaiter().GetResult();
                return true;
            }
            catch (Exception exception)
            {
                Trace.TraceError("Could not flush the final local Project draft before closing: {0}", exception);
                StatusText = $"Close blocked: the recovery draft could not be saved. {exception.Message}";
                _dialogs.ShowError("Local Project recovery failed", StatusText);
                return false;
            }
        }

        _dialogs.ShowWarning(
            "Unsaved local Project",
            "The saved Project has not been updated. Use 'Save project' before closing to commit edited or deleted documents:\n\n"
            + string.Join(Environment.NewLine, unsavedDocuments.Select(name => $"- {name}")));
        StatusText = "Close blocked: save the local Project first.";
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _draftAutosave.FlushAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Trace.TraceError("Could not flush the final local Project draft during shutdown: {0}", exception);
        }

        DisposeCore();
    }

    private void DisposeCore()
    {
        _disposed = true;
        _runtimeApi.RuntimeEventReceived -= RuntimeApiOnRuntimeEventReceived;
        _runtimeApi.ActionCatalogChanged -= RuntimeApiOnActionCatalogChanged;
        _runtimeApi.ConnectionStateChanged -= RuntimeApiOnConnectionStateChanged;
        _documents.Changed -= DocumentsOnChanged;
        _runSession.StateChanged -= RunSessionOnStateChanged;
        _draftAutosave.StateChanged -= DraftAutosaveOnStateChanged;
        _draftAutosave.SaveFailed -= DraftAutosaveOnFailed;
        _jsonPreviewRefreshTimer.Stop();
        _jsonPreviewRefreshTimer.Tick -= JsonPreviewRefreshTimerOnTick;
        _jsonPreviewRefreshTimer.Dispose();
        _draftAutosave.Dispose();
        _actionRunLog.Dispose();
        _runSession.Dispose();
        _runtimeSync.Dispose();
        CloseAllMethodEditors();
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            try
            {
                await _draftAutosave.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Trace.TraceError("Could not flush the final local Project draft during shutdown: {0}", exception);
            }

            DisposeCore();
        }

        await _runtimeApi.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
