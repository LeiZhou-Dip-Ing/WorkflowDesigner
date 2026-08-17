using WorkflowDesigner.WpfSdk;

namespace WorkflowCore.WpfDemo.Controls;

/// <summary>
/// Local XAML bridge for the SDK property editor host. Keeping the XAML-visible type in the
/// application assembly avoids Visual Studio design-time type-resolution noise while all
/// editor resolution behavior remains implemented by WorkflowDesigner.WpfSdk.
/// </summary>
public sealed class DesignerPropertyEditorHost : WorkflowPropertyEditorHost
{
}

/// <summary>
/// Local XAML bridge for the SDK workspace host. Plugin workspaces are still resolved through
/// the central WorkflowDesignerRegistry; this class contains no plugin-specific behavior.
/// </summary>
public sealed class DesignerWorkspaceHost : WorkflowWorkspaceHost
{
}
