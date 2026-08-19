using System.Windows;
using WorkflowCore.WpfDemo.Views;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.Services.Ui;

public sealed class ProtectedWorkflowPresentation : IProtectedWorkflowPresentation
{
    public Task ShowAsync(
        WorkflowPresentationResponse presentation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var window = new ProtectedWorkflowPresentationWindow(presentation)
        {
            Owner = Application.Current?.MainWindow
        };
        window.Show();
        return Task.CompletedTask;
    }
}
