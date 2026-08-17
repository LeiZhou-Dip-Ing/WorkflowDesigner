using System.Text.Json.Nodes;
using WorkflowCore.WpfDemo.Services;
using WorkflowRuntime.Contracts;
using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class EditorActionCatalogTests
{
    [Fact]
    public void EmptyCache_DoesNotCreateABundledActionCatalog()
    {
        using var context = CreateContext();
        var service = new EditorActionCatalog(context.Client, context.CachePath);

        Assert.Empty(service.Current.Actions);
        Assert.Equal(string.Empty, service.Current.CatalogVersion);
    }

    [Fact]
    public async Task Refresh_PersistsOnlyTheCatalogReceivedFromRuntime()
    {
        using var context = CreateContext();
        var service = new EditorActionCatalog(context.Client, context.CachePath);

        await service.RefreshAsync();
        var reloaded = new EditorActionCatalog(context.Client, context.CachePath);

        var action = Assert.Single(reloaded.Current.Actions);
        Assert.Equal("backend.action", action.ActionType);
        Assert.Equal("runtime-version", reloaded.Current.CatalogVersion);
        var cachedIconUri = reloaded.GetCachedIconUri(action.Icon);
        Assert.NotNull(cachedIconUri);
        Assert.True(File.Exists(new Uri(cachedIconUri).LocalPath));
    }

    [Fact]
    public async Task Refresh_PreservesRuntimeMetadataWithoutFrontendOverrides()
    {
        var staleCatalog = new ActionCatalogResponse
        {
            CatalogVersion = "stale-runtime",
            Actions =
            [
                Action("setVariable",
                    Field("VariableName", "x"),
                    Field("ScopeKind", "Method")),
                Action("threadWait",
                    Field("TaskVarName", string.Empty),
                    Field("ReturnVarNames", string.Empty))
            ]
        };
        using var context = CreateContext(staleCatalog);
        var service = new EditorActionCatalog(context.Client, context.CachePath);

        await service.RefreshAsync();
        var reloaded = new EditorActionCatalog(context.Client, context.CachePath);

        var setVariable = reloaded.Current.Actions.Single(action => action.ActionId == "setVariable");
        Assert.Equal("x", setVariable.Inputs.Single(field => field.Name == "VariableName").DefaultValue?.GetValue<string>());
        Assert.Contains(setVariable.Inputs, field => field.Name == "ScopeKind");
        var threadWait = reloaded.Current.Actions.Single(action => action.ActionId == "threadWait");
        Assert.Equal(["TaskVarName", "ReturnVarNames"], threadWait.Inputs.Select(field => field.Name));
    }

    [Fact]
    public async Task ConcurrentRefreshes_AreSerializedToProtectTheSharedCache()
    {
        using var context = CreateContext();
        var service = new EditorActionCatalog(context.Client, context.CachePath);

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => service.RefreshAsync()));

        Assert.Equal(1, context.Client.MaximumConcurrentCatalogRequests);
        Assert.Single(service.Current.Actions);
    }

    private static TestContext CreateContext(ActionCatalogResponse? catalog = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "workflow-catalog-tests", Guid.NewGuid().ToString("N"));
        return new TestContext(
            directory,
            Path.Combine(directory, "action-catalog.json"),
            new StubRuntimeClient(catalog ?? new ActionCatalogResponse
            {
                CatalogVersion = "runtime-version",
                Actions =
                [
                    new WorkflowActionDescriptorDto
                    {
                        ActionType = "backend.action",
                        DisplayName = "Backend Action",
                        Category = "Backend",
                        Icon = new ActionAssetReferenceDto
                        {
                            AssetId = "backend/action.svg",
                            ContentType = "image/svg+xml",
                            ContentHash = "abc123",
                            RelativeUri = "api/workflow-runtime/action-assets/backend/action.svg"
                        }
                    }
                ]
            }));
    }

    private static WorkflowActionDescriptorDto Action(
        string actionId,
        params WorkflowActionFieldDto[] fields)
        => new()
        {
            ActionId = actionId,
            ActionType = actionId,
            DisplayName = actionId,
            Category = "Built-in",
            Inputs = fields
        };

    private static WorkflowActionFieldDto Field(string name, string defaultValue)
        => new()
        {
            Name = name,
            DisplayName = name,
            Direction = "input",
            DefaultValue = JsonValue.Create(defaultValue)
        };

    private sealed class TestContext : IDisposable
    {
        public TestContext(string directory, string cachePath, StubRuntimeClient client)
        {
            Directory = directory;
            CachePath = cachePath;
            Client = client;
        }

        public string Directory { get; }
        public string CachePath { get; }
        public StubRuntimeClient Client { get; }

        public void Dispose()
        {
            Client.Dispose();
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, true);
            }
        }
    }

    private sealed class StubRuntimeClient : IRuntimeApiClient
    {
        private readonly ActionCatalogResponse _catalog;
        private readonly object _requestSync = new();
        private int _activeCatalogRequests;

        public StubRuntimeClient(ActionCatalogResponse catalog)
        {
            _catalog = catalog;
        }

        public event EventHandler<WorkflowRuntimeEventDto>? RuntimeEventReceived { add { } remove { } }
        public event EventHandler<ActionCatalogChangedDto>? ActionCatalogChanged { add { } remove { } }
        public event EventHandler<RuntimeConnectionChangedEventArgs>? ConnectionStateChanged { add { } remove { } }

        public int MaximumConcurrentCatalogRequests { get; private set; }

        public Uri ResolveRuntimeUri(string relativeUri) => new(new Uri("http://localhost/"), relativeUri);
        public Task ConnectEventsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public async Task<ActionCatalogResponse> GetActionCatalogAsync(CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _activeCatalogRequests);
            lock (_requestSync)
            {
                MaximumConcurrentCatalogRequests = Math.Max(MaximumConcurrentCatalogRequests, active);
            }

            try
            {
                await Task.Delay(10, cancellationToken);
                return _catalog;
            }
            finally
            {
                Interlocked.Decrement(ref _activeCatalogRequests);
            }
        }
        public Task<byte[]> GetActionAssetAsync(ActionAssetReferenceDto asset, CancellationToken cancellationToken = default)
            => Task.FromResult("<svg xmlns=\"http://www.w3.org/2000/svg\"><path d=\"M0 0h1v1z\"/></svg>"u8.ToArray());
        public Task<WorkflowDocumentResponse> GetWorkflowAsync(string workflowId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SharpScriptDocumentResponse> GetSharpScriptAsync(string workflowId, Guid scriptUid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SharpScriptPublishResponse> PublishSharpScriptAsync(string workflowId, SharpScriptDocumentDto script, long expectedWorkflowRevision, IReadOnlyList<SharpScriptLibraryReferenceDto> libraries, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowValidationResponse> ValidateAsync(JsonNode workflow, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowPublishResponse> PublishWorkflowAsync(string workflowId, JsonNode workflow, long expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Guid> StartPreviewRunAsync(JsonNode workflow, Guid? methodUid, string? methodName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Guid> StartPublishedRunAsync(string workflowId, Guid? methodUid, string? methodName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowRunStatusResponse> GetRunStatusAsync(Guid runId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CancelRunAsync(Guid runId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Dispose() { }
    }
}
