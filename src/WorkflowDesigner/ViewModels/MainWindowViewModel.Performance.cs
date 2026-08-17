using System.Diagnostics;
using WorkflowCore.WpfDemo.Models;
using WorkflowCore.WpfDemo.Services.Ui;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.ViewModels;

/// <summary>Coalesces expensive projections, serialization, validation, and catalog updates.</summary>
public sealed partial class MainWindowViewModel
{
    private readonly IUiTimer _jsonPreviewRefreshTimer;
    private bool _isJsonPreviewRefreshQueued;
    private bool _isCanvasContentRefreshQueued;
    private bool _runtimeValidationRunning;
    private bool _runtimeValidationPending;
    private string _pendingValidationJson = string.Empty;
    private long _pendingValidationRevision;
    private long _contentRevision;
    private long _serializedContentRevision = -1;
    private string _serializedProjectJson = string.Empty;
    private bool _runtimeCatalogBelongsToProject;

    private ActionTemplateItem CreateActionTemplateItem(WorkflowActionDescriptorDto descriptor)
        => new()
        {
            ActionId = descriptor.ActionId,
            DisplayName = descriptor.DisplayName,
            ActionType = descriptor.ActionType,
            Description = descriptor.Description,
            IconImage = _actionCatalog.GetCachedIconImage(descriptor.Icon)
        };

    private void ApplyActionToolboxPatch(ActionCatalogChangedDto change)
    {
        var affectedActionIds = change.Added.Concat(change.Updated).Select(action => action.ActionId)
            .Concat(change.RemovedActionIds).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var category in ActionToolbox.ToArray())
        {
            foreach (var item in category.Children
                         .Where(item => item.ActionId != null && affectedActionIds.Contains(item.ActionId)).ToArray())
                category.Children.Remove(item);
            if (category.Children.Count == 0) ActionToolbox.Remove(category);
        }

        foreach (var descriptor in _projectActionCatalog.Current.Actions
                     .Where(action => affectedActionIds.Contains(action.ActionId) && !action.IsDeprecated))
        {
            var category = ActionToolbox.FirstOrDefault(item =>
                string.Equals(item.DisplayName, descriptor.Category, StringComparison.OrdinalIgnoreCase));
            if (category == null)
            {
                category = new ActionTemplateItem { DisplayName = descriptor.Category };
                ActionToolbox.Add(category);
            }

            var template = CreateActionTemplateItem(descriptor);
            var insertAt = category.Children.TakeWhile(item => string.Compare(
                item.DisplayName, template.DisplayName, StringComparison.OrdinalIgnoreCase) <= 0).Count();
            category.Children.Insert(insertAt, template);
        }

        var orderedCategories = ActionToolbox.OrderBy(
            category => category.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
        for (var index = 0; index < orderedCategories.Length; index++)
        {
            var currentIndex = ActionToolbox.IndexOf(orderedCategories[index]);
            if (currentIndex != index) ActionToolbox.Move(currentIndex, index);
        }
    }

    private async Task ApplyActionCatalogChangeAsync(ActionCatalogChangedDto change)
    {
        try
        {
            if (!await _actionCatalog.ApplyChangeAsync(change).ConfigureAwait(false))
            {
                await SynchronizeRuntimeAsync().ConfigureAwait(false);
                return;
            }

            await _uiDispatcher.InvokeAsync(() =>
            {
                ApplyActionToolboxPatch(change);
                RefreshSelectedMethodLines(keepSelection: true);
                var catalogCheck = _runtimeSync.CheckActionsAgainstCatalog(Project);
                ApplyCatalogCheck(catalogCheck);
                RefreshActionProperties();
                if (catalogCheck.IdentitiesChanged) RefreshJsonPreview();
            }).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            if (IsRuntimeOnline) await SynchronizeRuntimeAsync().ConfigureAwait(false);
        }
    }

    private async Task ValidateCurrentWorkflowAsync(string workflowJson, long revision)
    {
        _pendingValidationJson = workflowJson;
        _pendingValidationRevision = revision;
        _runtimeValidationPending = true;
        if (_runtimeValidationRunning) return;

        _runtimeValidationRunning = true;
        try
        {
            while (_runtimeValidationPending && !_disposed)
            {
                _runtimeValidationPending = false;
                var validationJson = _pendingValidationJson;
                var validationRevision = _pendingValidationRevision;
                var summary = await _runtimeSync.ValidateAsync(validationJson);
                if (validationRevision == Interlocked.Read(ref _contentRevision))
                    await _uiDispatcher.InvokeAsync(() => ApplyValidationSummary(summary));
            }
        }
        finally
        {
            _runtimeValidationRunning = false;
        }
    }

    private void CommitJsonPreviewRefresh()
    {
        var revision = Interlocked.Read(ref _contentRevision);
        var workflowJson = SerializeCurrentProjectSnapshot(force: false);
        ObserveDocumentChanges();
        QueueDraftSnapshot(workflowJson);
        if (IsRuntimeOnline && !_isManualSaveRunning)
            _ = ValidateCurrentWorkflowAsync(workflowJson, revision);
    }

    private string SerializeCurrentProjectSnapshot(bool force)
    {
        var revision = Interlocked.Read(ref _contentRevision);
        if (!force && _serializedContentRevision == revision && !string.IsNullOrWhiteSpace(_serializedProjectJson))
            return _serializedProjectJson;

        var workflowJson = _documentPersistence.Serialize(Project);
        _serializedProjectJson = workflowJson;
        _serializedContentRevision = revision;
        JsonPreview = workflowJson;
        return workflowJson;
    }

    private void ScheduleJsonPreviewRefresh()
    {
        if (_disposed || _uiDispatcher.HasShutdownStarted) return;
        Interlocked.Increment(ref _contentRevision);
        _isJsonPreviewRefreshQueued = true;
        _jsonPreviewRefreshTimer.Stop();
        _jsonPreviewRefreshTimer.Start();
    }

    private void JsonPreviewRefreshTimerOnTick(object? sender, EventArgs e)
    {
        _jsonPreviewRefreshTimer.Stop();
        if (!_isJsonPreviewRefreshQueued || _disposed) return;
        _isJsonPreviewRefreshQueued = false;
        CommitJsonPreviewRefresh();
    }

    private void ScheduleCanvasContentChanged()
    {
        if (_isCanvasContentRefreshQueued || _disposed || _uiDispatcher.HasShutdownStarted) return;
        _isCanvasContentRefreshQueued = true;
        _uiDispatcher.Post(() =>
        {
            _isCanvasContentRefreshQueued = false;
            if (!_disposed) CanvasContentChanged?.Invoke(this, EventArgs.Empty);
        });
    }
}
