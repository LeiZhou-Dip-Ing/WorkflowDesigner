using System.Globalization;
using WorkflowRuntime.ActionSdk;

namespace WorkflowRuntime.SampleActionPlugin;

[WorkflowAction(
    "sample.runCounter",
    "Run Counter",
    ActionId = "c77e6418-6880-4f0c-ac6a-52f0c91afded",
    Category = "External plugins / State",
    Description = "Read and update a workflow-run variable through the public execution context.",
    DisplayTemplate = "Add {Increment} to {VariableName}")]
public sealed class RunCounterAction : WorkflowActionBase
{
    [WorkflowActionProperty(DisplayName = "Variable name", Required = true, Placeholder = "attemptCount", Order = 0)]
    public string VariableName { get; set; } = "attemptCount";

    [WorkflowActionInput(Minimum = -1000, Maximum = 1000, Step = 1, Order = 1)]
    public int Increment { get; set; } = 1;

    [WorkflowActionOutput(DisplayName = "Current value", Description = "Updated counter value.", Required = true, Order = 2)]
    public int CurrentValue { get; private set; }

    protected override ValueTask ExecuteActionAsync(IWorkflowActionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(VariableName))
        {
            throw new InvalidOperationException("Variable name cannot be empty.");
        }

        var current = context.TryGetVariable(VariableName, out var value) && value != null
            ? Convert.ToInt32(value, CultureInfo.InvariantCulture)
            : 0;
        CurrentValue = checked(current + Increment);
        context.SetVariable(VariableName, CurrentValue);
        context.Log($"Run variable '{VariableName}' updated to {CurrentValue}.");
        return ValueTask.CompletedTask;
    }
}
