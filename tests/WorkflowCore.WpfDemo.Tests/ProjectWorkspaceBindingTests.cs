using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class ProjectWorkspaceBindingTests
{
    [Fact]
    public void ProjectNavigationCommands_AreBoundToTheActiveWorkspaceInsteadOfTheOuterShell()
    {
        var xaml = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "WorkflowDesigner",
            "MainWindow.xaml"));

        Assert.Contains(
            "DataContext.SelectHamburgerMenuCommand, RelativeSource={RelativeSource AncestorType=ListBox}",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "DataContext.OpenMethodCommand, RelativeSource={RelativeSource AncestorType=ListBox}",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "DataContext.OpenScriptCommand, RelativeSource={RelativeSource AncestorType=ListBox}",
            xaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DataContext.SelectHamburgerMenuCommand, RelativeSource={RelativeSource AncestorType=Window}",
            xaml,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        foreach (var startPath in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var directory = new DirectoryInfo(startPath);
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
