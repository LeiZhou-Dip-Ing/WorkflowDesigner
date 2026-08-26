using System.Text.Json.Nodes;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.Services;

/// <summary>Defines the editor's REST commands and SignalR event connection to the runtime host.</summary>
public interface IRuntimeApiClient : IDisposable, IAsyncDisposable
{
    event EventHandler<WorkflowRuntimeEventDto>? RuntimeEventReceived;

    event EventHandler<ActionCatalogChangedDto>? ActionCatalogChanged;

    event EventHandler<RuntimeConnectionChangedEventArgs>? ConnectionStateChanged;

    Uri ResolveRuntimeUri(string relativeUri);

    Task ConnectEventsAsync(CancellationToken cancellationToken = default);

    Task<ActionCatalogResponse> GetActionCatalogAsync(CancellationToken cancellationToken = default);

    async Task<ActionCatalogResponse?> GetActionCatalogIfChangedAsync(
        string catalogVersion,
        CancellationToken cancellationToken = default)
        => await GetActionCatalogAsync(cancellationToken).ConfigureAwait(false);

    Task<byte[]> GetActionAssetAsync(ActionAssetReferenceDto asset, CancellationToken cancellationToken = default);

    Task<SharpScriptLibraryCatalogResponse> GetScriptLibrariesAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task<byte[]> GetScriptLibraryAssetAsync(
        SharpScriptLibraryAssemblyDto assembly,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task<SharpScriptLibraryInstallResponse> ImportScriptLibraryAsync(
        string filePath,
        string? libraryId = null,
        string? version = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task<SharpScriptLibraryInstallResponse> InstallScriptLibraryNuGetAsync(
        InstallSharpScriptNuGetRequest request,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task<WorkflowDocumentResponse> GetWorkflowAsync(string workflowId, CancellationToken cancellationToken = default);

    Task<WorkflowPublishResponse> ImportProtectedWorkflowAsync(
        string workflowId,
        string filePath,
        long expectedRevision,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task<WorkflowPresentationResponse> GetWorkflowPresentationAsync(
        string workflowId,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task<ActiveProjectIdentityResponse?> GetActiveProjectIdentityAsync(
        string workflowId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<ActiveProjectIdentityResponse?>(null);

    Task<SharpScriptDocumentResponse> GetSharpScriptAsync(
        string workflowId,
        Guid scriptUid,
        CancellationToken cancellationToken = default);

    Task<SharpScriptPublishResponse> PublishSharpScriptAsync(
        string workflowId,
        SharpScriptDocumentDto script,
        long expectedWorkflowRevision,
        IReadOnlyList<SharpScriptLibraryReferenceDto> libraries,
        CancellationToken cancellationToken = default);

    Task<SharpScriptPublishResponse> PublishSharpScriptAsync(
        string workflowId,
        Guid projectId,
        SharpScriptDocumentDto script,
        long expectedWorkflowRevision,
        IReadOnlyList<SharpScriptLibraryReferenceDto> libraries,
        CancellationToken cancellationToken = default)
        => PublishSharpScriptAsync(
            workflowId,
            script,
            expectedWorkflowRevision,
            libraries,
            cancellationToken);

    Task<WorkflowValidationResponse> ValidateAsync(JsonNode workflow, CancellationToken cancellationToken = default);

    Task<WorkflowPublishResponse> PublishWorkflowAsync(
        string workflowId,
        JsonNode workflow,
        long expectedRevision,
        CancellationToken cancellationToken = default);

    Task<WorkflowPublishResponse> PublishWorkflowAsync(
        string workflowId,
        Guid projectId,
        ProjectDeploymentScope deploymentScope,
        JsonNode workflow,
        long expectedRevision,
        CancellationToken cancellationToken = default)
        => PublishWorkflowAsync(workflowId, workflow, expectedRevision, cancellationToken);

    Task<Guid> StartPreviewRunAsync(
        JsonNode workflow,
        Guid? methodUid,
        string? methodName,
        CancellationToken cancellationToken = default);

    Task<Guid> StartPreviewRunAsync(
        JsonNode workflow,
        Guid? methodUid,
        string? methodName,
        IReadOnlyDictionary<string, JsonNode?> inputs,
        CancellationToken cancellationToken = default)
        => StartPreviewRunAsync(workflow, methodUid, methodName, cancellationToken);

    Task<Guid> StartPreviewRunAsync(
        JsonNode workflow,
        Guid? methodUid,
        string? methodName,
        IReadOnlyDictionary<string, JsonNode?> inputs,
        string executionMode,
        CancellationToken cancellationToken = default)
        => StartPreviewRunAsync(workflow, methodUid, methodName, inputs, cancellationToken);

    Task<Guid> StartPublishedRunAsync(
        string workflowId,
        Guid? methodUid,
        string? methodName,
        CancellationToken cancellationToken = default);

    Task<Guid> StartPublishedRunAsync(
        string workflowId,
        Guid? methodUid,
        string? methodName,
        IReadOnlyDictionary<string, JsonNode?> inputs,
        CancellationToken cancellationToken = default)
        => StartPublishedRunAsync(workflowId, methodUid, methodName, cancellationToken);

    Task<WorkflowRunStatusResponse> GetRunStatusAsync(Guid runId, CancellationToken cancellationToken = default);

    Task<byte[]?> GetResourcePreviewAsync(
        Guid runId,
        string methodName,
        int lineNumber,
        CancellationToken cancellationToken = default)
        => Task.FromResult<byte[]?>(null);

    Task<byte[]?> GetLatestResourcePreviewAsync(
        string methodName,
        int lineNumber,
        CancellationToken cancellationToken = default)
        => Task.FromResult<byte[]?>(null);

    Task<WorkflowExtensionCommandResponseDto> ExecuteExtensionCommandAsync(
        WorkflowExtensionCommandRequestDto request,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    Task CancelRunAsync(Guid runId, CancellationToken cancellationToken = default);

    Task StepRunAsync(Guid runId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    Task StepOverRunAsync(Guid runId, CancellationToken cancellationToken = default)
        => StepRunAsync(runId, cancellationToken);

    Task ContinueRunAsync(Guid runId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    Task PauseRunAsync(Guid runId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    ValueTask IAsyncDisposable.DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed class RuntimeConnectionChangedEventArgs : EventArgs
{
    public RuntimeConnectionChangedEventArgs(bool isConnected)
    {
        IsConnected = isConnected;
    }

    public bool IsConnected { get; }
}
