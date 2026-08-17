using WorkflowRuntime.ScriptSdk;

public enum ProcessingMode
{
    Standard,
    Precise,
    Fast
}

public sealed class PropertyTypesScript : IWorkflowSharpScript
{
    [ScriptInput("Label", Description = "Required text input.", Required = true, Order = 0,
        Group = "General", Placeholder = "Enter a label")]
    public string Label { get; set; } = string.Empty;

    [ScriptInput("Enabled", Required = false, DefaultValue = true, Order = 1, Group = "General")]
    public bool Enabled { get; set; } = true;

    [ScriptInput("Count", Required = true, DefaultValue = 3, Order = 2,
        Group = "Numbers", Minimum = 1, Maximum = 100, Step = 1)]
    public int Count { get; set; } = 3;

    [ScriptInput("Ratio", Required = false, DefaultValue = 1.5, Order = 3,
        Group = "Numbers", Minimum = 0, Maximum = 10, Step = 0.1)]
    public double Ratio { get; set; } = 1.5;

    [ScriptInput("Mode", Required = true, DefaultValue = ProcessingMode.Standard,
        Order = 4, Group = "Choice", EditorHint = "picklist")]
    public ProcessingMode Mode { get; set; } = ProcessingMode.Standard;

    [ScriptInput("Format", Required = false, DefaultValue = "Compact", Order = 5,
        Group = "Choice", EditorHint = "picklist", Options = new[] { "Compact", "Detailed" })]
    public string Format { get; set; } = "Compact";

    [ScriptOutput("Summary", Order = 0, Group = "Results")]
    public string Summary { get; private set; } = string.Empty;

    [ScriptOutput("Calculated value", Order = 1, Group = "Results")]
    public double CalculatedValue { get; private set; }

    [ScriptOutput("Was enabled", Order = 2, Group = "Results")]
    public bool WasEnabled { get; private set; }

    public ValueTask ExecuteAsync(
        IWorkflowSharpScriptContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CalculatedValue = Count * Ratio;
        WasEnabled = Enabled;
        Summary = $"{Label}: {Mode}/{Format} = {CalculatedValue}";
        return ValueTask.CompletedTask;
    }
}
