using System.Windows;
using WorkflowDesigner.Contracts;
using WorkflowDesigner.WpfSdk;
using WorkflowRuntime.OpenCvSamplePlugin.Contracts;
using WorkflowRuntime.OpenCvSamplePlugin.Designer.Wpf;
using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class WorkflowDesignerRegistryTests
{
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
}
