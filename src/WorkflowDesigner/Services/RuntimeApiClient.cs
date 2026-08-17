using System.Net.Http;
using System.Net.Http.Json;
using System.Net;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.SignalR.Client;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.Services;

/// <summary>Calls the runtime REST API and maintains its live SignalR event connection.</summary>
public sealed class RuntimeApiClient : IRuntimeApiClient
{
    private readonly HttpClient _httpClient;
    private readonly Uri _baseAddress;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    private HubConnection? _eventConnection;
    private readonly object _disposeLock = new();
    private Task? _disposeTask;
    private int _isConnected;
    private bool _disposed;

    public RuntimeApiClient()
    {
        var configuredAddress = Environment.GetEnvironmentVariable("WORKFLOW_RUNTIME_URL");
        _baseAddress = EnsureTrailingSlash(new Uri(
            string.IsNullOrWhiteSpace(configuredAddress) ? "http://localhost:5197/" : configuredAddress,
            UriKind.Absolute));
        _httpClient = new HttpClient { BaseAddress = _baseAddress, Timeout = TimeSpan.FromSeconds(30) };
    }

    public event EventHandler<WorkflowRuntimeEventDto>? RuntimeEventReceived;

    public event EventHandler<ActionCatalogChangedDto>? ActionCatalogChanged;

    public event EventHandler<RuntimeConnectionChangedEventArgs>? ConnectionStateChanged;

    public Uri ResolveRuntimeUri(string relativeUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeUri);
        return Uri.TryCreate(relativeUri, UriKind.Absolute, out var absoluteUri)
            ? absoluteUri
            : new Uri(_baseAddress, relativeUri.TrimStart('/'));
    }

    public async Task ConnectEventsAsync(CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
        var connectionToken = linkedCts.Token;
        await _connectionLock.WaitAsync(connectionToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_eventConnection?.State == HubConnectionState.Connected)
            {
                RaiseConnectionStateChanged(true);
                return;
            }

            if (_eventConnection != null)
            {
                await _eventConnection.DisposeAsync().ConfigureAwait(false);
            }

            var connection = new HubConnectionBuilder()
                .WithUrl(new Uri(_baseAddress, "api/workflow-runtime/events"))
                .WithAutomaticReconnect(new PersistentRetryPolicy())
                .Build();
            connection.On<WorkflowRuntimeEventDto>("RuntimeEvent", value => RuntimeEventReceived?.Invoke(this, value));
            connection.On<ActionCatalogChangedDto>("ActionCatalogChanged", value => ActionCatalogChanged?.Invoke(this, value));
            _eventConnection = connection;
            connection.Reconnecting += _ =>
            {
                RaiseConnectionStateChanged(false);
                return Task.CompletedTask;
            };
            connection.Reconnected += _ =>
            {
                RaiseConnectionStateChanged(true);
                return Task.CompletedTask;
            };
            connection.Closed += _ =>
            {
                if (!_disposed)
                {
                    RaiseConnectionStateChanged(false);
                }

                return Task.CompletedTask;
            };

            while (connection.State == HubConnectionState.Disconnected)
            {
                try
                {
                    await connection.StartAsync(connectionToken).ConfigureAwait(false);
                    RaiseConnectionStateChanged(true);
                }
                catch (Exception) when (!connectionToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), connectionToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task<ActionCatalogResponse> GetActionCatalogAsync(CancellationToken cancellationToken = default)
        => await _httpClient.GetFromJsonAsync<ActionCatalogResponse>("api/workflow-runtime/action-catalog", cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Workflow Runtime returned an empty Action Catalog.");

    public async Task<ActionCatalogResponse?> GetActionCatalogIfChangedAsync(
        string catalogVersion,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/workflow-runtime/action-catalog");
        if (!string.IsNullOrWhiteSpace(catalogVersion))
        {
            request.Headers.IfNoneMatch.ParseAdd($"\"{catalogVersion}\"");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<ActionCatalogResponse>(cancellationToken: cancellationToken)
                   .ConfigureAwait(false)
               ?? throw new InvalidOperationException("Workflow Runtime returned an empty Action Catalog.");
    }

    public async Task<byte[]> GetActionAssetAsync(
        ActionAssetReferenceDto asset,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        using var response = await _httpClient.GetAsync(ResolveRuntimeUri(asset.RelativeUri), cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<SharpScriptLibraryCatalogResponse> GetScriptLibrariesAsync(
        CancellationToken cancellationToken = default)
        => await _httpClient.GetFromJsonAsync<SharpScriptLibraryCatalogResponse>(
               "api/workflow-runtime/script-libraries",
               cancellationToken).ConfigureAwait(false)
           ?? throw new InvalidOperationException("Workflow Runtime returned an empty Script Library Catalog.");

    public async Task<byte[]> GetScriptLibraryAssetAsync(
        SharpScriptLibraryAssemblyDto assembly,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        if (string.IsNullOrWhiteSpace(assembly.DownloadUri))
        {
            throw new InvalidOperationException($"Script Library compile asset '{assembly.Name}' has no download URI.");
        }

        using var response = await _httpClient.GetAsync(
            ResolveRuntimeUri(assembly.DownloadUri),
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<SharpScriptLibraryInstallResponse> ImportScriptLibraryAsync(
        string filePath,
        string? libraryId = null,
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        await using var stream = File.OpenRead(filePath);
        using var content = new MultipartFormDataContent();
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "file", Path.GetFileName(filePath));
        if (!string.IsNullOrWhiteSpace(libraryId)) content.Add(new StringContent(libraryId), "libraryId");
        if (!string.IsNullOrWhiteSpace(version)) content.Add(new StringContent(version), "version");
        using var response = await _httpClient.PostAsync(
            "api/workflow-runtime/script-libraries/import",
            content,
            cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync<SharpScriptLibraryInstallResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SharpScriptLibraryInstallResponse> InstallScriptLibraryNuGetAsync(
        InstallSharpScriptNuGetRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var response = await _httpClient.PostAsJsonAsync(
            "api/workflow-runtime/script-libraries/nuget",
            request,
            cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync<SharpScriptLibraryInstallResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<WorkflowDocumentResponse> GetWorkflowAsync(
        string workflowId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        return await _httpClient
            .GetFromJsonAsync<WorkflowDocumentResponse>(
                $"api/workflows/{Uri.EscapeDataString(workflowId)}",
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Workflow Runtime returned an empty workflow document.");
    }

    public async Task<ActiveProjectIdentityResponse?> GetActiveProjectIdentityAsync(
        string workflowId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        using var response = await _httpClient.GetAsync(
            $"api/workflows/{Uri.EscapeDataString(workflowId)}/active-project",
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ReadJsonAsync<ActiveProjectIdentityResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SharpScriptDocumentResponse> GetSharpScriptAsync(
        string workflowId,
        Guid scriptUid,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        using var response = await _httpClient.GetAsync(
            $"api/workflows/{Uri.EscapeDataString(workflowId)}/scripts/{scriptUid:D}",
            cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync<SharpScriptDocumentResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SharpScriptPublishResponse> PublishSharpScriptAsync(
        string workflowId,
        SharpScriptDocumentDto script,
        long expectedWorkflowRevision,
        IReadOnlyList<SharpScriptLibraryReferenceDto> libraries,
        CancellationToken cancellationToken = default)
    {
        var activeProject = await GetActiveProjectIdentityAsync(workflowId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Runtime has no active Project. Deploy the complete Project first.");
        return await PublishSharpScriptAsync(
                workflowId,
                activeProject.ProjectId,
                script,
                expectedWorkflowRevision,
                libraries,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<SharpScriptPublishResponse> PublishSharpScriptAsync(
        string workflowId,
        Guid projectId,
        SharpScriptDocumentDto script,
        long expectedWorkflowRevision,
        IReadOnlyList<SharpScriptLibraryReferenceDto> libraries,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        ArgumentNullException.ThrowIfNull(script);
        using var response = await _httpClient.PutAsJsonAsync(
            $"api/workflows/{Uri.EscapeDataString(workflowId)}/scripts/{script.Uid:D}",
            new SharpScriptPublishRequest
            {
                ProjectId = projectId,
                Script = script,
                WorkflowRevision = expectedWorkflowRevision,
                Libraries = libraries
            },
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            await ThrowPublicationConflictAsync(
                response,
                workflowId,
                projectId,
                ProjectDeploymentScope.CurrentScript,
                expectedWorkflowRevision,
                cancellationToken).ConfigureAwait(false);
        }

        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.BadRequest)
        {
            return await response.Content.ReadFromJsonAsync<SharpScriptPublishResponse>(cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("Workflow Runtime returned an empty CSharp script publication response.");
        }

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException("Workflow Runtime returned an unexpected CSharp script publication response.");
    }

    public async Task<WorkflowValidationResponse> ValidateAsync(JsonNode workflow, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "api/workflows/validate",
            new WorkflowValidationRequest { Workflow = workflow },
            cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            return await response.Content.ReadFromJsonAsync<WorkflowValidationResponse>(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Workflow Runtime returned an empty validation response.");
        }

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException("Workflow Runtime returned an unexpected validation response.");
    }

    public async Task<WorkflowPublishResponse> PublishWorkflowAsync(
        string workflowId,
        JsonNode workflow,
        long expectedRevision,
        CancellationToken cancellationToken = default)
        => await PublishWorkflowAsync(
                workflowId,
                ReadRequiredProjectId(workflow),
                ProjectDeploymentScope.CompleteProject,
                workflow,
                expectedRevision,
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<WorkflowPublishResponse> PublishWorkflowAsync(
        string workflowId,
        Guid projectId,
        ProjectDeploymentScope deploymentScope,
        JsonNode workflow,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        ArgumentNullException.ThrowIfNull(workflow);
        using var response = await _httpClient.PutAsJsonAsync(
            $"api/workflows/{Uri.EscapeDataString(workflowId)}",
            new WorkflowPublishRequest
            {
                ProjectId = projectId,
                DeploymentScope = deploymentScope,
                Workflow = workflow,
                Revision = expectedRevision
            },
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            await ThrowPublicationConflictAsync(
                response,
                workflowId,
                projectId,
                deploymentScope,
                expectedRevision,
                cancellationToken).ConfigureAwait(false);
        }

        return await ReadJsonAsync<WorkflowPublishResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ThrowPublicationConflictAsync(
        HttpResponseMessage response,
        string workflowId,
        Guid requestedProjectId,
        ProjectDeploymentScope deploymentScope,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            var identityConflict = JsonSerializer.Deserialize<ProjectIdentityConflictResponse>(responseBody, options);
            if (identityConflict != null && identityConflict.RequestedProjectId != Guid.Empty)
            {
                throw new RuntimeProjectIdentityConflictException(
                    identityConflict.WorkflowId,
                    identityConflict.RequestedProjectId,
                    identityConflict.ActiveProjectId,
                    identityConflict.DeploymentScope,
                    identityConflict.Message,
                    responseBody);
            }

            var revisionConflict = JsonSerializer.Deserialize<WorkflowRevisionConflictResponse>(responseBody, options);
            if (revisionConflict != null && revisionConflict.CurrentRevision > 0)
            {
                throw new RuntimeRevisionConflictException(
                    revisionConflict.WorkflowId,
                    revisionConflict.ExpectedRevision,
                    revisionConflict.CurrentRevision,
                    revisionConflict.CurrentContentHash,
                    revisionConflict.Message,
                    responseBody);
            }
        }

        throw new RuntimeProjectIdentityConflictException(
            workflowId,
            requestedProjectId,
            Guid.Empty,
            deploymentScope,
            "Runtime rejected the Project identity for this deployment.",
            responseBody);
    }

    private static Guid ReadRequiredProjectId(JsonNode workflow)
    {
        if (workflow is not JsonObject project)
        {
            throw new InvalidOperationException("Workflow JSON root must be an object.");
        }

        var projectIdNode = project.FirstOrDefault(property =>
            string.Equals(property.Key, "projectId", StringComparison.OrdinalIgnoreCase)).Value;
        if (projectIdNode is not JsonValue value
            || !value.TryGetValue<string>(out var text)
            || !Guid.TryParse(text, out var projectId)
            || projectId == Guid.Empty)
        {
            throw new InvalidOperationException("Workflow JSON must contain a non-empty projectId.");
        }

        return projectId;
    }

    public async Task<Guid> StartPreviewRunAsync(
        JsonNode workflow,
        Guid? methodUid,
        string? methodName,
        CancellationToken cancellationToken = default)
        => await StartPreviewRunAsync(
            workflow,
            methodUid,
            methodName,
            new Dictionary<string, JsonNode?>(),
            cancellationToken).ConfigureAwait(false);

    public async Task<Guid> StartPreviewRunAsync(
        JsonNode workflow,
        Guid? methodUid,
        string? methodName,
        IReadOnlyDictionary<string, JsonNode?> inputs,
        CancellationToken cancellationToken = default)
        => await StartPreviewRunAsync(
            workflow,
            methodUid,
            methodName,
            inputs,
            "Run",
            cancellationToken).ConfigureAwait(false);

    public async Task<Guid> StartPreviewRunAsync(
        JsonNode workflow,
        Guid? methodUid,
        string? methodName,
        IReadOnlyDictionary<string, JsonNode?> inputs,
        string executionMode,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "api/workflow-runs/preview",
            new WorkflowPreviewRunRequest
            {
                Workflow = workflow,
                MethodUid = methodUid,
                MethodName = methodName,
                Inputs = inputs,
                ExecutionMode = executionMode
            },
            cancellationToken).ConfigureAwait(false);
        var accepted = await ReadJsonAsync<WorkflowRunAcceptedResponse>(response, cancellationToken).ConfigureAwait(false);
        return accepted.RunId;
    }

    public async Task<Guid> StartPublishedRunAsync(
        string workflowId,
        Guid? methodUid,
        string? methodName,
        CancellationToken cancellationToken = default)
        => await StartPublishedRunAsync(
            workflowId,
            methodUid,
            methodName,
            new Dictionary<string, JsonNode?>(),
            cancellationToken).ConfigureAwait(false);

    public async Task<Guid> StartPublishedRunAsync(
        string workflowId,
        Guid? methodUid,
        string? methodName,
        IReadOnlyDictionary<string, JsonNode?> inputs,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "api/workflow-runs",
            new WorkflowPublishedRunRequest
            {
                WorkflowId = workflowId,
                MethodUid = methodUid,
                MethodName = methodName,
                Inputs = inputs
            },
            cancellationToken).ConfigureAwait(false);
        var accepted = await ReadJsonAsync<WorkflowRunAcceptedResponse>(response, cancellationToken).ConfigureAwait(false);
        return accepted.RunId;
    }

    public async Task<WorkflowRunStatusResponse> GetRunStatusAsync(Guid runId, CancellationToken cancellationToken = default)
        => await _httpClient.GetFromJsonAsync<WorkflowRunStatusResponse>($"api/workflow-runs/{runId}", cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Workflow Runtime returned an empty run status.");

    public async Task<byte[]?> GetVisionPreviewAsync(
        Guid runId,
        string methodName,
        int lineNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        if (lineNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lineNumber));
        }

        var relativeUri =
            $"api/workflow-runtime/vision/previews/{runId:D}/{lineNumber}?methodName={Uri.EscapeDataString(methodName)}";
        using var response = await _httpClient.GetAsync(relativeUri, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]?> GetLatestVisionPreviewAsync(
        string methodName,
        int lineNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        if (lineNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lineNumber));
        }

        var relativeUri =
            $"api/workflow-runtime/vision/previews/latest/{lineNumber}?methodName={Uri.EscapeDataString(methodName)}";
        using var response = await _httpClient.GetAsync(relativeUri, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CancelRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync($"api/workflow-runs/{runId}/cancel", null, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public Task StepRunAsync(Guid runId, CancellationToken cancellationToken = default)
        => SendRunControlAsync(runId, "step", cancellationToken);

    public Task ContinueRunAsync(Guid runId, CancellationToken cancellationToken = default)
        => SendRunControlAsync(runId, "continue", cancellationToken);

    public Task PauseRunAsync(Guid runId, CancellationToken cancellationToken = default)
        => SendRunControlAsync(runId, "pause", cancellationToken);

    private async Task SendRunControlAsync(Guid runId, string operation, CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .PostAsync($"api/workflow-runs/{runId}/{operation}", null, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _ = BeginDispose();
    }

    public ValueTask DisposeAsync() => new(BeginDispose());

    private Task BeginDispose()
    {
        lock (_disposeLock)
        {
            return _disposeTask ??= DisposeCoreAsync();
        }
    }

    private async Task DisposeCoreAsync()
    {
        _disposed = true;
        _disposeCts.Cancel();
        await _connectionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_eventConnection != null)
            {
                try
                {
                    await _eventConnection.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    System.Diagnostics.Trace.TraceWarning(
                        "Could not dispose the runtime event connection cleanly: {0}",
                        exception.Message);
                }
                _eventConnection = null;
            }
        }
        finally
        {
            _httpClient.Dispose();
            _connectionLock.Release();
            _connectionLock.Dispose();
            _disposeCts.Dispose();
        }
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Workflow Runtime returned an empty JSON response.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new RuntimeApiException(response.StatusCode, responseBody);
    }

    private static Uri EnsureTrailingSlash(Uri uri)
        => uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal) ? uri : new Uri(uri.AbsoluteUri + "/");

    private void RaiseConnectionStateChanged(bool isConnected)
    {
        var nextValue = isConnected ? 1 : 0;
        if (Interlocked.Exchange(ref _isConnected, nextValue) == nextValue)
        {
            return;
        }

        ConnectionStateChanged?.Invoke(this, new RuntimeConnectionChangedEventArgs(isConnected));
    }

    private sealed class PersistentRetryPolicy : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext)
            => retryContext.PreviousRetryCount switch
            {
                0 => TimeSpan.Zero,
                1 => TimeSpan.FromSeconds(2),
                2 => TimeSpan.FromSeconds(5),
                _ => TimeSpan.FromSeconds(10)
            };
    }
}
