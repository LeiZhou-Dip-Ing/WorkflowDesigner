using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class SampleScriptLibraryTests
{
    [Fact]
    public void ScriptLibraryExamples_ContainTheDocumentedScenarios()
    {
        var directory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "WorkflowDesigner",
            "SampleProjects",
            "ScriptLibraryExamples");

        var sampleNames = Directory
            .GetFiles(directory, "*.csx")
            .Select(path => Path.GetFileName(path)!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(
            [
                "AsyncScaleScript.csx",
                "ExternalLibraryScaleScript.csx",
                "PropertyTypesScript.csx",
                "ScaleNumberScript.csx"
            ],
            sampleNames);
        Assert.True(File.Exists(Path.Combine(directory, "README.md")));
    }

    private static string FindRepositoryRoot()
    {
        foreach (var startPath in new[]
                 {
                     Environment.GetEnvironmentVariable("WORKFLOW_REPOSITORY_ROOT"),
                     AppContext.BaseDirectory,
                     Environment.CurrentDirectory
                 }.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var directory = new DirectoryInfo(startPath!);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "WorkflowDesigner.sln")))
            {
                directory = directory.Parent;
            }

            if (directory != null)
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("WorkflowDesigner repository root was not found.");
    }
}
