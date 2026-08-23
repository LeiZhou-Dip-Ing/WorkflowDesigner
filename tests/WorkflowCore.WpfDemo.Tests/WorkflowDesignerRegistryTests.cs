using System.Windows;
using System.Reflection;
using WorkflowRuntime.ActionSdk;
using WorkflowDesigner.WpfSdk;
using WorkflowRuntime.OpenCvSamplePlugin;
using WorkflowRuntime.OpenCvSamplePlugin.Shared;
using WorkflowRuntime.OpenCvSamplePlugin.UI;
using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class WorkflowDesignerRegistryTests
{
    [Fact]
    public void DesignerLoader_WithNoExtensions_ReturnsNoFailures()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"workflow-designer-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var loader = new DesignerPluginLoader(new WorkflowDesignerRegistry());
            Assert.Empty(loader.LoadDirectory(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void OpenCvExtension_UsesOneAssemblyForActionsAndDesignerUi()
    {
        var assembly = typeof(WorkflowRuntime.OpenCvSamplePlugin.OpenCvSamplePlugin).Assembly;

        Assert.Same(assembly, typeof(OpenCvSampleDesignerExtension).Assembly);
        Assert.Equal(
            typeof(WorkflowRuntime.OpenCvSamplePlugin.OpenCvSamplePlugin),
            Assert.Single(assembly.GetCustomAttributes<WorkflowRuntime.ActionSdk.WorkflowActionPluginEntryPointAttribute>()).PluginType);
        Assert.Equal(
            typeof(OpenCvSampleDesignerExtension),
            Assert.Single(assembly.GetCustomAttributes<WorkflowRuntime.ActionSdk.WorkflowDesignerExtensionEntryPointAttribute>()).ExtensionType);
    }

    [Fact]
    public void Registry_NormalizesLegacyKeysAndRejectsSilentReplacement()
    {
        var registry = new WorkflowDesignerRegistry();
        var template = new DataTemplate();

        registry.RegisterPropertyEditor("number", template, "host");

        Assert.True(registry.TryGetPropertyEditor(WorkflowPropertyEditorKeys.Number, out var resolved));
        Assert.Same(template, resolved);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            registry.RegisterPropertyEditor(WorkflowPropertyEditorKeys.Number, new DataTemplate(), "other.plugin"));
        Assert.Contains("already registered", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("host", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Registry_AllowsUnknownNamespacedKeysWithoutHostCodeChanges()
    {
        var registry = new WorkflowDesignerRegistry();
        const string customKey = "vendor.product.property.roi";
        var template = new DataTemplate();

        registry.RegisterPropertyEditor(customKey, template, "vendor.product");

        Assert.True(registry.TryGetPropertyEditor(customKey, out var resolved));
        Assert.Same(template, resolved);
        Assert.Contains(customKey, registry.PropertyEditorKeys);
    }

    [Fact]
    public void OpenCvDesigner_RegistersOneNewInteractiveEditorWithoutReplacingOriginalTemplateEditor()
    {
        var registry = new WorkflowDesignerRegistry();
        new OpenCvSampleDesignerExtension().Register(registry);

        Assert.Contains(OpenCvDesignerKeys.TemplateMatchActionEditor, registry.ActionEditorKeys);
        Assert.Contains(OpenCvDesignerKeys.InteractiveTemplateMatchActionEditor, registry.ActionEditorKeys);
        Assert.NotEqual(OpenCvDesignerKeys.TemplateMatchActionEditor, OpenCvDesignerKeys.InteractiveTemplateMatchActionEditor);
    }

    [Fact]
    public void DesignerLoader_LoadsUiEntryPointFromTheSingleOpenCvAssembly()
    {
        var assemblyPath = typeof(OpenCvSampleDesignerExtension).Assembly.Location;
        var registry = new WorkflowDesignerRegistry();
        var loader = new DesignerPluginLoader(registry);

        var result = Assert.Single(
            loader.LoadDirectory(Path.GetDirectoryName(assemblyPath)!),
            item => string.Equals(
                Path.GetFullPath(item.AssemblyPath),
                Path.GetFullPath(assemblyPath),
                StringComparison.OrdinalIgnoreCase));

        Assert.True(result.Loaded, result.Error);
        Assert.Equal(OpenCvPluginIdentity.Id, result.PluginId);
        Assert.Contains(OpenCvDesignerKeys.InteractiveTemplateMatchActionEditor, registry.ActionEditorKeys);
    }

    [Fact]
    public void OpenCvCapabilitiesShareOneExtensionIdentity()
    {
        var actions = new OpenCvSamplePlugin();
        var resources = new OpenCvResourceProviderPlugin();
        var designer = new OpenCvSampleDesignerExtension();

        Assert.Equal(OpenCvPluginIdentity.Id, actions.PluginId);
        Assert.Equal(actions.PluginId, resources.PluginId);
        Assert.Equal(actions.PluginId, designer.PluginId);
        Assert.Equal(actions.PluginVersion, resources.PluginVersion);
        Assert.Equal(actions.PluginVersion, designer.PluginVersion);
    }
}
