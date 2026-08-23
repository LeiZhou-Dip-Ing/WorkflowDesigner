using WorkflowRuntime.ActionSdk;
using WorkflowDesigner.WpfSdk;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.Services.Editing;

/// <summary>
/// Interprets frontend-neutral presentation metadata. Concrete custom keys are never hard-coded
/// here: the registry decides whether a plugin provides them.
/// </summary>
public static class ActionPresentationPolicy
{
    public static string GetWorkspaceKey(WorkflowActionDescriptorDto? descriptor)
    {
        var presentation = descriptor?.Presentation;
        var configured = DesignerKeyCompatibility.NormalizeWorkspace(presentation?.WorkspaceKind);
        if (!string.Equals(configured, WorkflowWorkspaceKeys.Auto, StringComparison.OrdinalIgnoreCase))
        {
            return configured;
        }

        return WorkflowWorkspaceKeys.Properties;
    }

    public static string GetWorkspaceFallbackKey(WorkflowActionDescriptorDto? descriptor)
        => WorkflowWorkspaceKeys.Properties;

    public static bool UsesPropertyWorkspace(WorkflowActionDescriptorDto? descriptor)
    {
        var key = GetWorkspaceKey(descriptor);
        if (string.Equals(key, WorkflowWorkspaceKeys.Properties, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (WorkflowDesignerRegistryHost.TryGetCurrent(out var registry)
            && registry != null
            && registry.WorkspaceKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(GetWorkspaceFallbackKey(descriptor), WorkflowWorkspaceKeys.Properties, StringComparison.OrdinalIgnoreCase);
    }

    public static bool UsesCustomWorkspace(WorkflowActionDescriptorDto? descriptor)
        => !UsesPropertyWorkspace(descriptor);

    public static bool CanOpenOnDoubleClick(WorkflowActionDescriptorDto? descriptor)
        => !string.IsNullOrWhiteSpace(descriptor?.Presentation?.DoubleClickEditor);

    public static string GetDoubleClickEditor(WorkflowActionDescriptorDto? descriptor)
        => DesignerKeyCompatibility.NormalizeActionEditor(descriptor?.Presentation?.DoubleClickEditor);

    public static string GetDoubleClickEditorFallback(WorkflowActionDescriptorDto? descriptor)
        => WorkflowActionEditorKeys.Properties;
}
