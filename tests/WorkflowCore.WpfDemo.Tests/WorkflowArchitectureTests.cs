using WorkflowCore.WpfDemo.ViewModels;
using Xunit;
using System.Runtime.CompilerServices;
using WorkflowCore.WpfDemo.Services.Drafts;
using WorkflowCore.WpfDemo.Services.Editing;
using WorkflowCore.WpfDemo.Services.Runtime;
using WorkflowCore.WpfDemo.Services.Workspace;
using WorkflowRuntime.OpenCvSamplePlugin;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class WorkflowArchitectureTests
{
    [Fact]
    public void WpfClient_UsesExplicitSdkPackagesWithoutDirectWorkflowCoreDependency()
    {
        var projectFile = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "WorkflowDesigner",
            "WorkflowDesigner.csproj"));

        Assert.DoesNotContain("Include=\"WorkflowCore\"", projectFile, StringComparison.Ordinal);
        Assert.Contains("Include=\"WorkflowDesigner.WpfSdk\"", projectFile, StringComparison.Ordinal);
        Assert.Contains("Include=\"WorkflowRuntime.ActionSdk\"", projectFile, StringComparison.Ordinal);
        Assert.DoesNotContain("Include=\"WorkflowWpfSdk\"", projectFile, StringComparison.Ordinal);
        Assert.DoesNotContain("<ProjectReference", projectFile, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfClient_DoesNotReferenceRuntimeImplementationAssemblies()
    {
        var referencedAssemblyNames = typeof(MainWindowViewModel).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToArray();

        Assert.DoesNotContain("WorkflowRuntime.Application", referencedAssemblyNames);
        Assert.DoesNotContain("WorkflowRuntime.RestService", referencedAssemblyNames);
        Assert.DoesNotContain("WorkflowRuntime.WindowsService", referencedAssemblyNames);
    }

    [Fact]
    public void MainWindowViewModel_DoesNotUseConcreteDialogsOrDispatcherTimer()
    {
        var source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "WorkflowDesigner",
            "ViewModels",
            "MainWindowViewModel.cs"));

        Assert.DoesNotContain("MessageBox.Show", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new OpenFileDialog", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new SaveFileDialog", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherTimer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("File.ReadAllText", source, StringComparison.Ordinal);
        Assert.DoesNotContain("File.WriteAllText", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HubConnection", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRunStatusAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_documentEditStates", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_runningActionEvents", source, StringComparison.Ordinal);
        Assert.True(File.ReadLines(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "WorkflowDesigner",
            "ViewModels",
            "MainWindowViewModel.cs")).Count() < 3000);
    }

    [Fact]
    public void EditorResponsibilities_AreImplementedByNamedBusinessComponents()
    {
        Assert.NotNull(typeof(MethodLineEditor));
        Assert.NotNull(typeof(VariableEditor));
        Assert.NotNull(typeof(ActionPropertyEditor));
        Assert.NotNull(typeof(EditorDocumentWorkspace));
        Assert.NotNull(typeof(LocalDraftAutosave));
        Assert.NotNull(typeof(RuntimeRunSession));
        Assert.NotNull(typeof(ActionRunLog));
        Assert.NotNull(typeof(RuntimeWorkspaceSync));
        Assert.NotNull(typeof(RuntimeDeployment));
        Assert.NotNull(typeof(MethodDeploymentTracker));
    }

    [Fact]
    public void OpenCvAdvancedUi_UsesMvvmCommandsAndBehaviorWithoutCodeBehind()
    {
        var uiDirectory = Path.Combine(
            GetRepositoryRoot(),
            "samples",
            "WorkflowRuntime.OpenCvSamplePlugin",
            "UI");

        Assert.Empty(Directory.EnumerateFiles(uiDirectory, "*.xaml.cs", SearchOption.AllDirectories));
        foreach (var xamlPath in Directory.EnumerateFiles(uiDirectory, "*.xaml", SearchOption.AllDirectories))
        {
            var xaml = File.ReadAllText(xamlPath);
            Assert.DoesNotContain(" Click=", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain(" MouseLeftButton", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain(" SelectionChanged=", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain(" ValueChanged=", xaml, StringComparison.Ordinal);
        }

        Assert.True(File.Exists(Path.Combine(uiDirectory, "TemplateMatchCanvasBehavior.cs")));
    }

    [Fact]
    public void OrdinaryActionSdkPlugin_DoesNotRequireDesignerUiPackages()
    {
        var project = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "samples",
            "WorkflowRuntime.SampleActionPlugin",
            "WorkflowRuntime.SampleActionPlugin.csproj"));

        Assert.DoesNotContain("WorkflowWpfSdk", project, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkflowDesigner.WpfSdk", project, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenCvSingleAssembly_DeclaresRuntimeResourceAndDesignerEntryPoints()
    {
        var attributeNames = typeof(OpenCvSamplePlugin).Assembly
            .GetCustomAttributesData()
            .Select(attribute => attribute.AttributeType.Name)
            .ToArray();

        Assert.Contains("WorkflowActionPluginEntryPointAttribute", attributeNames);
        Assert.Contains("WorkflowResourceProviderEntryPointAttribute", attributeNames);
        Assert.Contains("WorkflowDesignerExtensionEntryPointAttribute", attributeNames);
    }

    [Fact]
    public void TemplateMatchDesignerCommands_DoNotPersistTemporaryOperationState()
    {
        var source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "samples",
            "WorkflowRuntime.OpenCvSamplePlugin",
            "UI",
            "InteractiveTemplateMatchViewModel.cs"));

        Assert.Contains("RunCommandAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetText(\"Operation\"", source, StringComparison.Ordinal);
    }

    private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
