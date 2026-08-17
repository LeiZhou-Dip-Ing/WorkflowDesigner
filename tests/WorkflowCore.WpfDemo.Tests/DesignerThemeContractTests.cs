using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class DesignerThemeContractTests
{
    [Fact]
    public void OfflineSidebarButtons_HaveAnExplicitReusableDisabledTheme()
    {
        var appXaml = ReadRepositoryFile("src", "WorkflowDesigner", "App.xaml");
        var mainWindowXaml = ReadRepositoryFile("src", "WorkflowDesigner", "MainWindow.xaml");

        Assert.Contains("x:Key=\"SidebarActionButtonStyle\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("<Trigger Property=\"IsEnabled\" Value=\"False\">", appXaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"Background\" Value=\"#FF1B1B1B\"", appXaml, StringComparison.Ordinal);
        Assert.Equal(
            4,
            CountOccurrences(mainWindowXaml, "Style=\"{StaticResource SidebarActionButtonStyle}\""));
    }

    [Fact]
    public void DocumentIcons_AreProvidedByOneSharedTemplate()
    {
        var appXaml = ReadRepositoryFile("src", "WorkflowDesigner", "App.xaml");
        var mainWindowXaml = ReadRepositoryFile("src", "WorkflowDesigner", "MainWindow.xaml");

        Assert.Contains("x:Key=\"DocumentIconTemplate\"", appXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IconGlyph", mainWindowXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CSharpFileGeometry", mainWindowXaml, StringComparison.Ordinal);
        Assert.True(
            CountOccurrences(mainWindowXaml, "ContentTemplate=\"{StaticResource DocumentIconTemplate}\"") >= 5);
    }

    [Fact]
    public void Editors_UseDedicatedSplittersChromeButtonsAndGlobalScrollBars()
    {
        var appXaml = ReadRepositoryFile("src", "WorkflowDesigner", "App.xaml");
        var methodXaml = ReadRepositoryFile(
            "src",
            "WorkflowDesigner",
            "Views",
            "MethodEditorView.xaml");
        var scriptXaml = ReadRepositoryFile(
            "src",
            "WorkflowDesigner",
            "Views",
            "CSharpScriptEditorView.xaml");

        Assert.Contains("<Style TargetType=\"ScrollBar\">", appXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Method name\"", methodXaml, StringComparison.Ordinal);
        Assert.True(CountOccurrences(methodXaml, "<GridSplitter") >= 2);
        Assert.Contains("x:Key=\"DiagnosticsChromeButtonStyle\"", scriptXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"⌃\"", scriptXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"×\"", scriptXaml, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(params string[] pathSegments)
        => File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. pathSegments]));

    private static int CountOccurrences(string text, string value)
        => text.Split(value, StringSplitOptions.None).Length - 1;

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
