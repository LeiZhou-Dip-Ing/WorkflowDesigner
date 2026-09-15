using WorkflowCore.Execution;
using WorkflowCore.Errors;
using WorkflowCore.Serialization;
using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class ThreadFileActionsDemoTests
{
    [Fact]
    public async Task DemoProject_RunsFileWorkOnThreadAndReturnsCopiedPath()
    {
        var demoDirectory = Path.Combine(Path.GetTempPath(), "WorkflowCoreThreadDemo");
        if (Directory.Exists(demoDirectory)) Directory.Delete(demoDirectory, recursive: true);
        try
        {
            var projectFile = Path.Combine(
                AppContext.BaseDirectory,
                "SampleProjects",
                "ThreadFileActionsDemo.json");
            var project = new WorkflowJsonSerializer().DeserializeProject(await File.ReadAllTextAsync(projectFile));

            var result = await new MethodRunner().StartAsync(project, "Main");

            Assert.Equal(TaskResultType.OK, result.ResultType);
            Assert.True(File.Exists(Path.Combine(demoDirectory, "output", "copied-in-thread.bin")));
        }
        finally
        {
            if (Directory.Exists(demoDirectory)) Directory.Delete(demoDirectory, recursive: true);
        }
    }
}
