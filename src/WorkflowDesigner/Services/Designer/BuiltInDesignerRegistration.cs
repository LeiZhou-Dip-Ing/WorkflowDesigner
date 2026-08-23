using System.Windows;
using System.Windows.Controls;
using WorkflowCore.WpfDemo.Views;
using WorkflowRuntime.ActionSdk;
using WorkflowDesigner.WpfSdk;

namespace WorkflowCore.WpfDemo.Services.Designer;

public static class BuiltInDesignerRegistration
{
    public static void Register(IWorkflowDesignerRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        RegisterTemplate(registry, WorkflowPropertyEditorKeys.Text, "BuiltinPropertyTextTemplate");
        RegisterTemplate(registry, WorkflowPropertyEditorKeys.Number, "BuiltinPropertyNumberTemplate");
        RegisterTemplate(registry, WorkflowPropertyEditorKeys.Checkbox, "BuiltinPropertyBooleanTemplate");
        RegisterTemplate(registry, WorkflowPropertyEditorKeys.Select, "BuiltinPropertySelectionTemplate");
        RegisterTemplate(registry, WorkflowPropertyEditorKeys.Json, "BuiltinPropertyJsonTemplate");
        RegisterTemplate(registry, WorkflowPropertyEditorKeys.Lookup, "BuiltinPropertyLookupTemplate");
        RegisterTemplate(registry, WorkflowPropertyEditorKeys.StrictLookup, "BuiltinPropertyLookupTemplate");
        RegisterTemplate(registry, WorkflowPropertyEditorKeys.Variable, "BuiltinPropertyLookupTemplate");
        RegisterTemplate(registry, WorkflowPropertyEditorKeys.Expression, "BuiltinPropertyLookupTemplate");
        RegisterTemplate(registry, WorkflowPropertyEditorKeys.Method, "BuiltinPropertyLookupTemplate");

        registry.RegisterWorkspace(
            WorkflowWorkspaceKeys.Image,
            context => new ImageWorkspaceView { DataContext = context });

        registry.RegisterActionEditor(
            WorkflowActionEditorKeys.Image,
            context => new ActionEditorWindow(context, isImageEditor: true));

        registry.RegisterActionEditor(
            WorkflowActionEditorKeys.Properties,
            context => new ActionEditorWindow(context, isImageEditor: false));
    }

    private static void RegisterTemplate(
        IWorkflowDesignerRegistry registry,
        string editorKey,
        object resourceKey)
    {
        if (Application.Current.TryFindResource(resourceKey) is not DataTemplate template)
        {
            throw new InvalidOperationException($"Built-in property editor resource '{resourceKey}' was not found.");
        }

        registry.RegisterPropertyEditor(editorKey, template);
    }
}
