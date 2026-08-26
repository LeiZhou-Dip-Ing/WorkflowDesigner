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
        Assert.Contains("Property=\"Background\" Value=\"{DynamicResource AppSidebarBrush}\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"Foreground\" Value=\"{DynamicResource AppDisabledTextBrush}\"", appXaml, StringComparison.Ordinal);
        Assert.Equal(
            2,
            CountOccurrences(mainWindowXaml, "Style=\"{StaticResource SidebarActionButtonStyle}\""));
        Assert.DoesNotContain("ToolTip=\"Deploy workflow\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ToolTip=\"Download workflow\"", mainWindowXaml, StringComparison.Ordinal);
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

    [Fact]
    public void CustomWindowChrome_KeepsShellOverlaysInTheContentRow()
    {
        var mainWindowXaml = ReadRepositoryFile("src", "WorkflowDesigner", "MainWindow.xaml");

        Assert.Contains("WindowStyle=\"None\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<shell:WindowChrome", mainWindowXaml, StringComparison.Ordinal);
        var projectHubStart = mainWindowXaml.IndexOf(
            "<Grid x:Name=\"ProjectHubOverlay\"",
            StringComparison.Ordinal);
        Assert.True(projectHubStart >= 0);
        var projectHubTagEnd = mainWindowXaml.IndexOf('>', projectHubStart);
        Assert.True(projectHubTagEnd > projectHubStart);
        Assert.Contains(
            "Grid.Row=\"1\"",
            mainWindowXaml[projectHubStart..projectHubTagEnd],
            StringComparison.Ordinal);
        Assert.Contains(
            "<Grid Grid.Row=\"1\" Panel.ZIndex=\"300\" Background=\"#A8000000\">",
            mainWindowXaml,
            StringComparison.Ordinal);
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
