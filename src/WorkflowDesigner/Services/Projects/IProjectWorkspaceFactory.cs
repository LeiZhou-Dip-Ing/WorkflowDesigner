using WorkflowCore.WpfDemo.Models;

namespace WorkflowCore.WpfDemo.Services.Projects;

public interface IProjectWorkspaceFactory
{
    IProjectWorkspace Create(OpenedWorkflowProject openedProject);
}
