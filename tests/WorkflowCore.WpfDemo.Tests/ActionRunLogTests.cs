using System.Text.Json;
using WorkflowCore.WpfDemo.Services;
using WorkflowCore.WpfDemo.Services.Editing;
using WorkflowCore.WpfDemo.Services.Runtime;
using WorkflowCore.WpfDemo.Services.Ui;
using WorkflowRuntime.Contracts;
using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class ActionRunLogTests
{
    [Fact]
    public void AddRunFailure_DoesNotDuplicateCurrentActionFailure()
    {
        using var log = CreateLog();
        var executionId = Guid.NewGuid();
        log.ResetRunningActions();
        log.Apply(Event("ActionStarted", executionId));
        log.Apply(Event("ActionFailed", executionId, "precise action failure"));

        log.AddRunFailure("Main", "Finished with Error: precise action failure");

        var item = Assert.Single(log.Events);
        Assert.Equal("TextMetrics", item.ActionName);
        Assert.Equal("precise action failure", item.Result);
    }

    [Fact]
    public void AddRunFailure_ShowsFailuresThatOccurBeforeAnActionStarts()
    {
        using var log = CreateLog();
        log.ResetRunningActions();

        log.AddRunFailure("Main", "validation failed");

        var item = Assert.Single(log.Events);
        Assert.Equal("Run", item.ActionName);
        Assert.Equal("validation failed", item.Result);
    }

    [Fact]
    public void RunCompleted_AddsSummaryWithRecordedActionExecutionCount()
    {
        using var log = CreateLog();
        var firstExecutionId = Guid.NewGuid();
        var secondExecutionId = Guid.NewGuid();
        log.Apply(Event("ActionStarted", firstExecutionId));
        log.Apply(Event("ActionCompleted", firstExecutionId));
        log.Apply(Event("ActionStarted", secondExecutionId));
        log.Apply(Event("ActionCompleted", secondExecutionId));

        log.Apply(new WorkflowRuntimeEventDto
        {
            EventType = "RunCompleted",
            MethodName = "Main",
            Message = "Workflow run finished with OK.",
            Payload = JsonSerializer.SerializeToNode(new { resultType = "OK" })
        });

        Assert.Equal(3, log.Events.Count);
        var summary = log.Events[^1];
        Assert.Equal("Run complete", summary.ActionName);
        Assert.Equal("Succeeded", summary.Status);
        Assert.Contains("2 Action executions recorded", summary.Result);
    }

    [Fact]
    public void VariableChanged_AddsAnOrderedVariableTraceEntry()
    {
        using var log = CreateLog();
        var lineUid = Guid.NewGuid();

        log.Apply(new WorkflowRuntimeEventDto
        {
            RunId = Guid.NewGuid(),
            EventType = "VariableChanged",
            ActionType = "TextMetrics",
            MethodName = "Main",
            LineNumber = 15,
            LineUid = lineUid,
            Timestamp = DateTimeOffset.Parse("2026-08-24T10:11:12.345+00:00"),
            Message = "score = 0.98"
        });

        var trace = Assert.Single(log.TraceEntries);
        Assert.Equal(lineUid, trace.LineUid);
        Assert.Equal("Step 15", trace.Step);
        Assert.Equal("score", trace.Name);
        Assert.Equal("0.98", trace.Value);
        Assert.Equal("double", trace.Type);
    }

    private static ActionRunLog CreateLog()
    {
        var catalog = new EmptyCatalog();
        return new ActionRunLog(
            catalog,
            new ActionPropertyEditor(catalog, new VariableEditor()),
            new StoppedTimerFactory());
    }

    private static WorkflowRuntimeEventDto Event(
        string eventType,
        Guid executionId,
        string message = "")
        => new()
        {
            EventType = eventType,
            ActionExecutionId = executionId,
            ActionType = "TextMetrics",
            MethodName = "Main",
            LineNumber = 15,
            Message = message
        };

    private sealed class EmptyCatalog : IEditorActionCatalog
    {
        public ActionCatalogResponse Current { get; } = new();

        public string? GetCachedIconUri(ActionAssetReferenceDto? icon) => null;

        public Task<ActionCatalogResponse> RefreshAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Current);
    }

    private sealed class StoppedTimerFactory : IUiTimerFactory
    {
        public IUiTimer Create(TimeSpan interval) => new StoppedTimer();

        private sealed class StoppedTimer : IUiTimer
        {
            public event EventHandler? Tick { add { } remove { } }
            public void Start() { }
            public void Stop() { }
            public void Dispose() { }
        }
    }
}
