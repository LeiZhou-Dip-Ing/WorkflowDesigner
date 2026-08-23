using WorkflowRuntime.ActionSdk;

[assembly: WorkflowActionPluginEntryPoint(
    typeof(WorkflowRuntime.OpenCvSamplePlugin.OpenCvSamplePlugin))]
[assembly: WorkflowDesignerExtensionEntryPoint(
    typeof(WorkflowRuntime.OpenCvSamplePlugin.UI.OpenCvSampleDesignerExtension))]
