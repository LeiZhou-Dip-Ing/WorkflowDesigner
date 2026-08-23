using WorkflowRuntime.OpenCvSamplePlugin.Runtime;
using WorkflowRuntime.OpenCvSamplePlugin.Shared;
using WorkflowRuntime.ResourceSdk;

namespace WorkflowRuntime.OpenCvSamplePlugin;

public sealed class OpenCvResourceProviderPlugin : IWorkflowResourceProviderPlugin
{
    public string PluginId => OpenCvPluginIdentity.Id;

    public string PluginVersion => OpenCvPluginIdentity.Version;
    public string DisplayName => OpenCvPluginIdentity.DisplayName;

    public IWorkflowResourceRuntime CreateRuntime() => new OpenCvResourceRuntime();
}
