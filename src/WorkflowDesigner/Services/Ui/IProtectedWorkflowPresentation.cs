using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.Services.Ui;

public interface IProtectedWorkflowPresentation
{
    Task ShowAsync(
        WorkflowPresentationResponse presentation,
        CancellationToken cancellationToken = default);
}
