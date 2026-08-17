using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Models;

namespace WorkflowCore.WpfDemo.Services.Projects;

public interface IWorkflowProjectFileService
{
    OpenedWorkflowProject Create(string filePath);

    OpenedWorkflowProject Open(string filePath);

    void Save(string filePath, WorkflowProject project);
}
