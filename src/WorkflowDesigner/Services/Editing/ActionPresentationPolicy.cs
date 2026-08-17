using WorkflowDesigner.Contracts;
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

        return string.Equals(presentation?.ActionKind, WorkflowActionKindKeys.Vision, StringComparison.OrdinalIgnoreCase)
            ? WorkflowWorkspaceKeys.Image
            : WorkflowWorkspaceKeys.Properties;
    }

    public static string GetWorkspaceFallbackKey(WorkflowActionDescriptorDto? descriptor)
        => string.Equals(descriptor?.Presentation?.ActionKind, WorkflowActionKindKeys.Vision, StringComparison.OrdinalIgnoreCase)
            ? WorkflowWorkspaceKeys.Image
            : WorkflowWorkspaceKeys.Properties;

    public static bool UsesPropertyWorkspace(WorkflowActionDescriptorDto? descriptor)
    {
        var key = GetWorkspaceKey(descriptor);
        if (string.Equals(key, WorkflowWorkspaceKeys.Properties, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(key, WorkflowWorkspaceKeys.Image, StringComparison.OrdinalIgnoreCase))
        {
            return false;
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

    // V4 compatibility helper retained for callers/tests; custom vision workspaces also count as image workspaces.
    public static bool UsesImageWorkspace(WorkflowActionDescriptorDto? descriptor)
        => UsesCustomWorkspace(descriptor)
           && string.Equals(GetWorkspaceFallbackKey(descriptor), WorkflowWorkspaceKeys.Image, StringComparison.OrdinalIgnoreCase);

    public static bool CanOpenOnDoubleClick(WorkflowActionDescriptorDto? descriptor)
        => !string.IsNullOrWhiteSpace(descriptor?.Presentation?.DoubleClickEditor);

    public static string GetDoubleClickEditor(WorkflowActionDescriptorDto? descriptor)
        => DesignerKeyCompatibility.NormalizeActionEditor(descriptor?.Presentation?.DoubleClickEditor);

    public static string GetDoubleClickEditorFallback(WorkflowActionDescriptorDto? descriptor)
        => string.Equals(descriptor?.Presentation?.ActionKind, WorkflowActionKindKeys.Vision, StringComparison.OrdinalIgnoreCase)
            ? WorkflowActionEditorKeys.Vision
            : WorkflowActionEditorKeys.Properties;
}
