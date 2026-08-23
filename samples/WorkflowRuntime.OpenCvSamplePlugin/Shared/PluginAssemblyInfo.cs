using WorkflowRuntime.ActionSdk;
using WorkflowRuntime.ResourceSdk;

[assembly: WorkflowActionPluginEntryPoint(
    typeof(WorkflowRuntime.OpenCvSamplePlugin.OpenCvSamplePlugin))]
[assembly: WorkflowDesignerExtensionEntryPoint(
    typeof(WorkflowRuntime.OpenCvSamplePlugin.UI.OpenCvSampleDesignerExtension))]
[assembly: WorkflowResourceProviderEntryPoint(
    typeof(WorkflowRuntime.OpenCvSamplePlugin.OpenCvResourceProviderPlugin))]
