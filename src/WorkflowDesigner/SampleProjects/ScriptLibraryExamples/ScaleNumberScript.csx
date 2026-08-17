using WorkflowRuntime.ScriptSdk;

public sealed class ScaleNumberScript : IWorkflowSharpScript
{
    [ScriptInput(
        "Value",
        Description = "Value to scale.",
        Required = true,
        Order = 0)]
    public double Value { get; set; }

    [ScriptInput(
        "Factor",
        Description = "Scale factor.",
        Required = true,
        DefaultValue = 2.0,
        Order = 1)]
    public double Factor { get; set; } = 2.0;

    [ScriptOutput(
        "Result",
        Description = "Scaled result.",
        Order = 0)]
    public double Result { get; private set; }

    public ValueTask ExecuteAsync(
        IWorkflowSharpScriptContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Result = Value * Factor;
        return ValueTask.CompletedTask;
    }
}
