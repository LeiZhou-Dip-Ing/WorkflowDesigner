using System.Text.Json;
using WorkflowRuntime.ActionSdk;
using WorkflowRuntime.SampleActionPlugin;
using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class SampleActionPluginTests
{
    [Fact]
    public async Task TextTransform_UsesGeneratedConfigurationAndReturnsOutputs()
    {
        var action = (IWorkflowActionHandler)new TextTransformAction();
        var result = await action.ExecuteAsync(
            new TestActionContext(),
            Inputs(("Mode", "Uppercase"), ("TrimWhitespace", true), ("MaximumLength", 5), ("Text", "  workflow  ")),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("WORKF", result.Outputs["Result"]);
        Assert.Equal(true, result.Outputs["WasTruncated"]);
    }

    [Fact]
    public async Task JsonEnvelope_PreservesStructuredPayload()
    {
        var action = (IWorkflowActionHandler)new JsonEnvelopeAction();
        var result = await action.ExecuteAsync(
            new TestActionContext(),
            Inputs(("EventName", "orders.created"), ("SchemaVersion", 2), ("Payload", new { orderId = 42 })),
            CancellationToken.None);

        var envelope = Assert.IsType<JsonElement>(result.Outputs["Envelope"]);
        Assert.Equal("orders.created", envelope.GetProperty("eventName").GetString());
        Assert.Equal(42, envelope.GetProperty("payload").GetProperty("orderId").GetInt32());
    }

    [Fact]
    public async Task Delay_ObservesCancellation()
    {
        var action = (IWorkflowActionHandler)new DelayAction();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await action.ExecuteAsync(
                new TestActionContext(),
                Inputs(("Milliseconds", 1000)),
                cancellation.Token));
    }

    [Fact]
    public async Task RunCounter_ReadsAndUpdatesPublicContextVariable()
    {
        var context = new TestActionContext();
        context.SetVariable("attemptCount", 4);
        var action = (IWorkflowActionHandler)new RunCounterAction();

        var result = await action.ExecuteAsync(
            context,
            Inputs(("VariableName", "attemptCount"), ("Increment", 3)),
            CancellationToken.None);

        Assert.Equal(7, result.Outputs["CurrentValue"]);
        Assert.True(context.TryGetVariable("attemptCount", out var value));
        Assert.Equal(7, value);
    }

    private static IReadOnlyDictionary<string, JsonElement> Inputs(params (string Name, object? Value)[] values)
        => values.ToDictionary(
            pair => pair.Name,
            pair => JsonSerializer.SerializeToElement(pair.Value),
            StringComparer.OrdinalIgnoreCase);

    private sealed class TestActionContext : IWorkflowActionContext
    {
        private readonly Dictionary<string, object?> _variables = new(StringComparer.OrdinalIgnoreCase);

        public Guid RunId { get; } = Guid.NewGuid();

        public string? WorkflowId => "sample-tests";

        public string? MethodName => "Main";

        public int? LineNumber => 1;

        public bool TryGetVariable(string name, out object? value) => _variables.TryGetValue(name, out value);

        public void SetVariable(string name, object? value) => _variables[name] = value;

        public void Log(string message)
        {
        }
    }
}
