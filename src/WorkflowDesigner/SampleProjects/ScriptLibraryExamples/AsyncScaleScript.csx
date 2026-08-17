using WorkflowRuntime.ScriptSdk;
using WorkflowRuntime.TestScriptLibrary;

public sealed class AsyncScaleScript : IWorkflowSharpScript
{
    [ScriptInput("Value", Required = true, Order = 0)]
    public double Value { get; set; }

    [ScriptInput("Factor", Required = true, DefaultValue = 2.0, Order = 1)]
    public double Factor { get; set; } = 2.0;

    [ScriptInput("Delay (ms)", Required = false, DefaultValue = 10, Order = 2,
        Group = "Execution", Minimum = 0, Maximum = 5000, Step = 10)]
    public int DelayMilliseconds { get; set; } = 10;

    [ScriptOutput("Result", Order = 0)]
    public double Result { get; private set; }

    [ScriptOutput("Completed after delay", Order = 1)]
    public bool Completed { get; private set; }

    public async ValueTask ExecuteAsync(
        IWorkflowSharpScriptContext context,
        CancellationToken cancellationToken)
    {
        await Task.Delay(DelayMilliseconds, cancellationToken);
        Result = NumberAlgorithms.Scale(Value, Factor);
        Completed = true;
    }
}
