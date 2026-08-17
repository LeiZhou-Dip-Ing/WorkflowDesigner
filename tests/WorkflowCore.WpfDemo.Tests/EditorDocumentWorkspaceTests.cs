using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Services;
using WorkflowCore.WpfDemo.Services.Workspace;
using WorkflowCore.WpfDemo.ViewModels;
using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class EditorDocumentWorkspaceTests
{
    [Fact]
    public void SynchronizeDocumentWithRuntime_DoesNotChangeOtherMethods()
    {
        var persistence = new JsonEditorDocumentPersistence(new WorkflowEditorJsonSerializer());
        var selectedMethod = CreateMethod("Selected", "greeting");
        var otherMethod = CreateMethod("Other local method", "log");
        var otherLines = otherMethod.MethodLines;
        var localProject = new WorkflowProject { Methods = [selectedMethod, otherMethod] };
        var runtimeMethod = CreateMethod("Selected", "greeting");
        runtimeMethod.Uid = selectedMethod.Uid;
        runtimeMethod.MethodLines.Add(MethodLine.Create(20, 0, WorkflowAction.Create("delay")));
        var workspace = CreateWorkspace(localProject, persistence);

        var synchronized = workspace.SynchronizeDocumentWithRuntime(
            localProject,
            WorkflowEditorDocument.FromMethod(runtimeMethod));

        Assert.Same(selectedMethod, synchronized.Method);
        Assert.Equal(2, selectedMethod.MethodLines.Count);
        Assert.Equal(2, localProject.Methods.Count);
        Assert.Same(otherMethod, localProject.Methods[1]);
        Assert.Same(otherLines, otherMethod.MethodLines);
    }

    [Fact]
    public void SynchronizeWithRuntimeProject_UpdatesOnlyChangedDocuments()
    {
        var persistence = new JsonEditorDocumentPersistence(new WorkflowEditorJsonSerializer());
        var unchangedMethod = CreateMethod("Unchanged", "log");
        var unchangedLines = unchangedMethod.MethodLines;
        var changedMethod = CreateMethod("Changed", "greeting");
        var removedMethod = CreateMethod("Local only", "delay");
        var localProject = new WorkflowProject
        {
            Name = "Local",
            Methods = [unchangedMethod, changedMethod, removedMethod]
        };

        var runtimeUnchangedMethod = persistence.DeserializeDocument(
            persistence.SerializeDocument(WorkflowEditorDocument.FromMethod(unchangedMethod))).Method!;
        var runtimeChangedMethod = CreateMethod("Changed", "greeting");
        runtimeChangedMethod.Uid = changedMethod.Uid;
        runtimeChangedMethod.MethodLines.Add(MethodLine.Create(20, 0, WorkflowAction.Create("delay")));
        var runtimeAddedMethod = CreateMethod("Runtime only", "log");
        var runtimeProject = new WorkflowProject
        {
            Name = "Runtime",
            Methods = [runtimeUnchangedMethod, runtimeChangedMethod, runtimeAddedMethod]
        };

        var workspace = CreateWorkspace(localProject, persistence);

        workspace.SynchronizeWithRuntimeProject(localProject, runtimeProject);

        Assert.Equal("Runtime", localProject.Name);
        Assert.Equal(["Unchanged", "Changed", "Runtime only"], localProject.Methods.Select(method => method.Name));
        Assert.Same(unchangedMethod, localProject.Methods[0]);
        Assert.Same(unchangedLines, unchangedMethod.MethodLines);
        Assert.Same(changedMethod, localProject.Methods[1]);
        Assert.Equal(2, changedMethod.MethodLines.Count);
        Assert.DoesNotContain(localProject.Methods, method => method.Uid == removedMethod.Uid);
    }

    private static WorkflowMethod CreateMethod(string name, string actionType)
        => new()
        {
            Name = name,
            MethodLines = [MethodLine.Create(10, 0, WorkflowAction.Create(actionType))]
        };

    private static EditorDocumentWorkspace CreateWorkspace(
        WorkflowProject project,
        IEditorDocumentPersistence persistence)
        => new(
            new UnusedMethodEditorFactory(),
            new UnusedScriptEditorFactory(),
            persistence,
            new EditorSession(project));

    private sealed class UnusedMethodEditorFactory : IMethodEditorViewModelFactory
    {
        public MethodEditorViewModel Create(WorkflowMethod method, MainWindowViewModel owner)
            => throw new NotSupportedException();

        public void Release(MethodEditorViewModel viewModel)
        {
        }
    }

    private sealed class UnusedScriptEditorFactory : ICSharpScriptEditorViewModelFactory
    {
        public CSharpScriptEditorViewModel Create(WorkflowScript script, MainWindowViewModel owner)
            => throw new NotSupportedException();

        public void Release(CSharpScriptEditorViewModel viewModel)
        {
        }
    }
}
