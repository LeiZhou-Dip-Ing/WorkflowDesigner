namespace WorkflowCore.WpfDemo.Services.Projects;

public interface IProjectWorkspace : IDisposable, IAsyncDisposable
{
    bool CanCloseEditor();
}
