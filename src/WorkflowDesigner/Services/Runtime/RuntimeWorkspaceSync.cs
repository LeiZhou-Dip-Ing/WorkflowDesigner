using System.Text.Json.Nodes;
using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Services.Editing;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.Services.Runtime;

/// <summary>Refreshes runtime metadata without reading or replacing the local Project.</summary>
public sealed class RuntimeWorkspaceSync : IDisposable
{
    private readonly IRuntimeApiClient _runtimeApi;
    private readonly IEditorActionCatalog _catalog;
    private readonly IEditorDocumentPersistence _persistence;
    private readonly IActionPropertyEditor _actionProperties;
    private readonly EditorSession _session;
    private readonly SemaphoreSlim _synchronizationLock = new(1, 1);
    private readonly SemaphoreSlim _validationLock = new(1, 1);
    private bool _disposed;

    public RuntimeWorkspaceSync(
        IRuntimeApiClient runtimeApi,
        IEditorActionCatalog catalog,
        IEditorDocumentPersistence persistence,
        IActionPropertyEditor actionProperties,
        EditorSession session)
    {
        _runtimeApi = runtimeApi ?? throw new ArgumentNullException(nameof(runtimeApi));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _actionProperties = actionProperties ?? throw new ArgumentNullException(nameof(actionProperties));
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
        => await _runtimeApi.ConnectEventsAsync(cancellationToken).ConfigureAwait(false);

    public async Task<WorkflowSynchronizationResult?> SynchronizeAsync(
        WorkflowProject project,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(project);
        if (!await _synchronizationLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        try
        {
            progress?.Report("Loading Action Catalog...");
            var previousCatalogVersion = _catalog.Current.CatalogVersion;
            await _catalog.RefreshAsync(cancellationToken).ConfigureAwait(false);
            var catalogChanged = !string.Equals(
                previousCatalogVersion,
                _catalog.Current.CatalogVersion,
                StringComparison.Ordinal);

            progress?.Report("Checking the Runtime active Project...");
            var activeProject = await _runtimeApi.GetActiveProjectIdentityAsync(
                    WorkflowRuntimeDefaults.DefaultWorkflowId,
                    cancellationToken)
                .ConfigureAwait(false);
            ApplyRuntimeIdentity(activeProject);

            return new WorkflowSynchronizationResult(catalogChanged);
        }
        finally
        {
            _synchronizationLock.Release();
        }
    }

    public async Task<WorkflowValidationSummary> ValidateAsync(
        WorkflowProject project,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        return await ValidateAsync(_persistence.Serialize(project), cancellationToken).ConfigureAwait(false);
    }

    public async Task<WorkflowValidationSummary> ValidateAsync(
        string workflowJson,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowJson);
        await _validationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var workflow = JsonNode.Parse(workflowJson)
                ?? throw new InvalidOperationException("The editor produced an empty workflow document.");
            var response = await _runtimeApi.ValidateAsync(workflow, cancellationToken).ConfigureAwait(false);
            var messages = response.Messages
                .Where(message => !string.Equals(message.Severity, "Info", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var hasIssues = !response.IsValid || messages.Length > 0;
            if (!hasIssues)
            {
                return new WorkflowValidationSummary(false, string.Empty, 0);
            }

            var visibleMessages = messages.Take(8).Select(message =>
            {
                var location = message.MethodName == null
                    ? string.Empty
                    : message.LineNumber.HasValue
                        ? $"{message.MethodName}, line {message.LineNumber}: "
                        : $"{message.MethodName}: ";
                return $"{location}{message.Message}";
            });
            var details = string.Join("; ", visibleMessages);
            var remaining = messages.Length > 8 ? $"; and {messages.Length - 8} more" : string.Empty;
            return new WorkflowValidationSummary(
                true,
                string.IsNullOrWhiteSpace(details)
                    ? "Runtime validation failed. The workflow must be corrected before Run or publish."
                    : $"Runtime validation: {details}{remaining}",
                Math.Max(messages.Length, 1));
        }
        finally
        {
            _validationLock.Release();
        }
    }

    public WorkflowActionCatalogCheckResult CheckActionsAgainstCatalog(WorkflowProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var identitiesChanged = _actionProperties.EnsureStableActionIds(project);
        var unavailable = new List<string>();
        foreach (var method in project.Methods)
        {
            foreach (var line in method.MethodLines)
            {
                var action = line.Action;
                var actionType = action?.ActionType;
                var isAvailable = action == null || _actionProperties.FindDescriptor(action) != null;
                line.IsActionAvailable = isAvailable;
                line.ActionAvailabilityMessage = isAvailable
                    ? null
                    : $"Runtime Action '{actionType}' no longer exists. Replace or delete this method line.";
                if (!isAvailable)
                {
                    unavailable.Add($"{method.Name}, line {line.LineNo}: {actionType}");
                }
            }
        }

        if (unavailable.Count == 0)
        {
            return new WorkflowActionCatalogCheckResult(identitiesChanged, Array.Empty<string>(), string.Empty);
        }

        var visibleIssues = string.Join("; ", unavailable.Take(8));
        var remaining = unavailable.Count > 8 ? $"; and {unavailable.Count - 8} more" : string.Empty;
        return new WorkflowActionCatalogCheckResult(
            identitiesChanged,
            unavailable,
            $"{unavailable.Count} workflow Action reference(s) are missing from the current Runtime: {visibleIssues}{remaining}. "
            + "The JSON is preserved; replace or delete the affected lines before publishing.");
    }

    public void ApplyRuntimeSnapshot(WorkflowDocumentResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        _session.RuntimeProjectJson = response.Workflow.ToJsonString();
        _session.RuntimeProjectId = response.ProjectId;
        _session.RuntimeRevision = response.Revision;
        _session.RuntimeContentHash = response.ContentHash;
    }

    public void ApplyRuntimeIdentity(ActiveProjectIdentityResponse? response)
    {
        var canKeepLoadedProject = response != null
                                   && _session.RuntimeProjectId == response.ProjectId
                                   && _session.RuntimeRevision == response.Revision;
        if (!canKeepLoadedProject)
        {
            _session.RuntimeProjectJson = null;
        }

        _session.RuntimeProjectId = response?.ProjectId;
        _session.RuntimeRevision = response?.Revision ?? 0;
        _session.RuntimeContentHash = response?.ContentHash ?? string.Empty;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _synchronizationLock.Dispose();
        _validationLock.Dispose();
    }
}

public sealed record WorkflowSynchronizationResult(bool CatalogChanged);

public sealed record WorkflowActionCatalogCheckResult(
    bool IdentitiesChanged,
    IReadOnlyList<string> UnavailableActions,
    string Message)
{
    public bool HasUnavailableActions => UnavailableActions.Count > 0;
}

public sealed record WorkflowValidationSummary(bool HasIssues, string Message, int IssueCount);
