using System.Text;
using WorkflowRuntime.ActionSdk;

namespace WorkflowRuntime.SampleActionPlugin;

[WorkflowAction(
    "Greeting",
    "Greeting",
    ActionId = "0f6a1dc4-43a3-4e1e-96bb-6bfcb0c377f3",
    ActionVersion = 1,
    Category = "External plugins",
    Description = "Create and log a greeting from an external Action SDK plugin.",
    DisplayTemplate = "Greet {PersonName} {Repeat} time(s)",
    Aliases = new[] { "sample.greeting" })]
public sealed class GreetingAction : WorkflowActionBase
{
    [WorkflowActionProperty(DisplayName = "Greeting prefix", Description = "Text placed before the person's name.", Category = "Action", Order = 0)]
    public string Prefix { get; set; } = "Hello";

    [WorkflowActionInput(DisplayName = "Person name", Description = "Name included in the greeting.", Required = true, Order = 1)]
    public string PersonName { get; set; } = "Workflow";

    [WorkflowActionInput(Description = "Number of greetings to produce.", Minimum = 1, Maximum = 5, Order = 2)]
    public int Repeat { get; set; } = 1;

    [WorkflowActionOutput(Description = "Generated greeting text.", Required = true, Order = 3)]
    public string Greeting { get; private set; } = string.Empty;

    protected override WorkflowActionIcon Icon => new()
    {
        ContentType = "image/svg+xml",
        Content = Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"32\" height=\"32\" viewBox=\"0 0 32 32\"><rect x=\"2\" y=\"2\" width=\"28\" height=\"28\" rx=\"6\" fill=\"#16a34a\"/><path d=\"M9 11h14v8H14l-5 4z\" fill=\"white\"/></svg>")
    };

    protected override ValueTask ExecuteActionAsync(
        IWorkflowActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Greeting = string.Join(" ", Enumerable.Repeat($"{Prefix}, {PersonName}!", Repeat));

        context.Log(Greeting);
        return ValueTask.CompletedTask;
    }
}
