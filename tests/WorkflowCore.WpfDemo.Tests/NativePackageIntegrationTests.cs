using System.Reflection.PortableExecutable;
using WorkflowCore.Actions.ControlFlow;
using WorkflowCore.Execution;
using WorkflowCore.Errors;
using WorkflowCore.Model;
using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class NativePackageIntegrationTests
{
    [Fact]
    public async Task RestoredPackage_DeploysAndExecutesDefaultNativeKernel()
    {
        var nativePath = Path.Combine(
            AppContext.BaseDirectory,
            "runtimes",
            "win-x64",
            "native",
            "WorkflowCore.Native.dll");
        Assert.True(File.Exists(nativePath), $"Native package asset was not deployed to {nativePath}.");
        using (var stream = File.OpenRead(nativePath))
        using (var reader = new PEReader(stream))
        {
            Assert.False(reader.HasMetadata);
            Assert.Equal(Machine.Amd64, reader.PEHeaders.CoffHeader.Machine);
        }

        var project = new WorkflowProject { Name = "Native package integration" };
        var method = new WorkflowMethod { Name = "Main" };
        method.MethodLines.Add(MethodLine.Create(10, 0, new IfAction { Condition = "true" }));
        method.MethodLines.Add(MethodLine.Create(20, 1, new LogAction { Message = "native:true" }));
        method.MethodLines.Add(MethodLine.Create(30, 0, new EndIfAction()));
        project.Methods.Add(method);

        var result = await new MethodRunner().StartAsync(project, method);

        Assert.Equal(TaskResultType.OK, result.ResultType);
    }
}
