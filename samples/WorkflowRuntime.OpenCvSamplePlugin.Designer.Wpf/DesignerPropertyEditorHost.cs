using WorkflowDesigner.WpfSdk;

namespace WorkflowRuntime.OpenCvSamplePlugin.Designer.Wpf;

/// <summary>
/// Local XAML bridge to the public Designer SDK host. Keeping the concrete XAML type in the
/// plugin assembly avoids design-time cross-assembly lookup issues while retaining registry behavior.
/// </summary>
public sealed class DesignerPropertyEditorHost : WorkflowPropertyEditorHost
{
}
