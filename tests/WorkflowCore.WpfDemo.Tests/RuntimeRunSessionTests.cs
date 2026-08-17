using System.Text.Json.Nodes;
using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Services;
using WorkflowCore.WpfDemo.Services.Runtime;
using WorkflowRuntime.Contracts;
using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class RuntimeRunSessionTests
{
    [Fact]
    public async Task RunPreviewAsync_SendsCurrentEditorProjectAndSelectedMethod()
    {
        var method = new WorkflowMethod { Name = "New local method" };
        var project = new WorkflowProject { Name = "Unsaved editor project", Methods = [method] };
        var session = new EditorSession(project);
        var runtimeApi = new RecordingRuntimeApi();
        using var runSession = new RuntimeRunSession(
            runtimeApi,
            new JsonEditorDocumentPersistence(new WorkflowEditorJsonSerializer()),
            session);

        var result = await runSession.RunPreviewAsync(project, method);

        Assert.True(result.Succeeded);
        Assert.Equal(1, runtimeApi.PreviewRunCount);
        Assert.Equal(0, runtimeApi.PublishedRunCount);
        Assert.Equal(method.Uid, runtimeApi.MethodUid);
        Assert.Equal(method.Name, runtimeApi.MethodName);
        Assert.Equal("Unsaved editor project", runtimeApi.PreviewWorkflow?["name"]?.GetValue<string>());
    }

    [Fact]
    public async Task RunPublishedAsync_DoesNotSendTheLocalProjectAsPreview()
    {
        var method = new WorkflowMethod { Name = "Main" };
        var session = new EditorSession(new WorkflowProject { Methods = [method] });
        var runtimeApi = new RecordingRuntimeApi();
        using var runSession = new RuntimeRunSession(
            runtimeApi,
            new JsonEditorDocumentPersistence(new WorkflowEditorJsonSerializer()),
            session);

        var result = await runSession.RunPublishedAsync(method);

        Assert.True(result.Succeeded);
        Assert.Equal(1, runtimeApi.PublishedRunCount);
        Assert.Equal(0, runtimeApi.PreviewRunCount);
        Assert.Equal(WorkflowRuntimeDefaults.DefaultWorkflowId, runtimeApi.WorkflowId);
        Assert.Equal(method.Uid, runtimeApi.MethodUid);
        Assert.Equal(method.Name, runtimeApi.MethodName);
    }

    [Fact]
    public async Task RunPublishedAsync_ReturnsFailureForAFailedTerminalRun()
    {
        var method = new WorkflowMethod { Name = "Main" };
        var session = new EditorSession(new WorkflowProject { Methods = [method] });
        var runtimeApi = new RecordingRuntimeApi
        {
            Status = new WorkflowRunStatusResponse
            {
                State = "Failed",
                ResultType = "Error",
                ResultValue = false,
                Error = "Execution failed."
            }
        };
        using var runSession = new RuntimeRunSession(
            runtimeApi,
            new JsonEditorDocumentPersistence(new WorkflowEditorJsonSerializer()),
            session);

        var result = await runSession.RunPublishedAsync(method);

        Assert.False(result.Succeeded);
        Assert.Contains("Execution failed", result.Message);
    }

    private sealed class RecordingRuntimeApi : IRuntimeApiClient
    {
        private readonly Guid _runId = Guid.NewGuid();

        public event EventHandler<WorkflowRuntimeEventDto>? RuntimeEventReceived { add { } remove { } }
        public event EventHandler<ActionCatalogChangedDto>? ActionCatalogChanged { add { } remove { } }
        public event EventHandler<RuntimeConnectionChangedEventArgs>? ConnectionStateChanged { add { } remove { } }

        public int PreviewRunCount { get; private set; }
        public int PublishedRunCount { get; private set; }
        public string? WorkflowId { get; private set; }
        public Guid? MethodUid { get; private set; }
        public string? MethodName { get; private set; }
        public JsonNode? PreviewWorkflow { get; private set; }
        public WorkflowRunStatusResponse Status { get; init; } = new()
        {
            State = "Completed",
            ResultValue = true
        };

        public Uri ResolveRuntimeUri(string relativeUri) => new("http://localhost/" + relativeUri.TrimStart('/'));
        public Task ConnectEventsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ActionCatalogResponse> GetActionCatalogAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<byte[]> GetActionAssetAsync(ActionAssetReferenceDto asset, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDocumentResponse> GetWorkflowAsync(string workflowId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SharpScriptDocumentResponse> GetSharpScriptAsync(string workflowId, Guid scriptUid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SharpScriptPublishResponse> PublishSharpScriptAsync(string workflowId, SharpScriptDocumentDto script, long expectedWorkflowRevision, IReadOnlyList<SharpScriptLibraryReferenceDto> libraries, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowValidationResponse> ValidateAsync(JsonNode workflow, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowPublishResponse> PublishWorkflowAsync(string workflowId, JsonNode workflow, long expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Guid> StartPreviewRunAsync(
            JsonNode workflow,
            Guid? methodUid,
            string? methodName,
            CancellationToken cancellationToken = default)
        {
            PreviewRunCount++;
            PreviewWorkflow = workflow;
            MethodUid = methodUid;
            MethodName = methodName;
            return Task.FromResult(_runId);
        }

        public Task<Guid> StartPublishedRunAsync(
            string workflowId,
            Guid? methodUid,
            string? methodName,
            CancellationToken cancellationToken = default)
        {
            PublishedRunCount++;
            WorkflowId = workflowId;
            MethodUid = methodUid;
            MethodName = methodName;
            return Task.FromResult(_runId);
        }

        public Task<WorkflowRunStatusResponse> GetRunStatusAsync(Guid runId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new WorkflowRunStatusResponse
            {
                RunId = runId,
                State = Status.State,
                MethodName = Status.MethodName,
                StartedAt = Status.StartedAt,
                FinishedAt = Status.FinishedAt,
                ResultValue = Status.ResultValue,
                ResultType = Status.ResultType,
                Error = Status.Error
            });
        }

        public Task CancelRunAsync(Guid runId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }
}
