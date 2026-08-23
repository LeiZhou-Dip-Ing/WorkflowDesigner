using WorkflowRuntime.OpenCvSamplePlugin.Runtime;
using WorkflowRuntime.ResourceSdk;

namespace WorkflowRuntime.OpenCvSamplePlugin;

public sealed class OpenCvResourceProviderPlugin : IWorkflowResourceProviderPlugin
{
    public string PluginId => "workflow.sample-opencv-resources";

    public string PluginVersion => "2.0.0-alpha.5";

    public IWorkflowResourceRuntime CreateRuntime() => new OpenCvResourceRuntime();
}
