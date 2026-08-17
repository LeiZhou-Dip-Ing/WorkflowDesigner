using WorkflowRuntime.ScriptSdk;
using WorkflowRuntime.TestScriptLibrary;

public sealed class ExternalLibraryScaleScript : IWorkflowSharpScript
{
    [ScriptInput("Value", Required = true, Order = 0)]
    public double Value { get; set; }

    [ScriptInput("Factor", Required = true, DefaultValue = 2.0, Order = 1)]
    public double Factor { get; set; } = 2.0;

    [ScriptOutput("Result", Order = 0)]
    public double Result { get; private set; }

    public ValueTask ExecuteAsync(
        IWorkflowSharpScriptContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Result = NumberAlgorithms.Scale(Value, Factor);
        return ValueTask.CompletedTask;
    }
}
