using WorkflowDesigner.WpfSdk;

namespace WorkflowRuntime.OpenCvSamplePlugin.UI;

/// <summary>
/// Local XAML bridge to the public Designer SDK host. Keeping the concrete XAML type in the
/// plugin assembly avoids design-time cross-assembly lookup issues while retaining registry behavior.
/// </summary>
internal sealed class DesignerPropertyEditorHost : WorkflowPropertyEditorHost
{
}
