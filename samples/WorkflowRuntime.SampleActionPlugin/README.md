# Sample external Action plugin

This project references only `WorkflowRuntime.ActionSdk`. It does not reference
`WorkflowCore`, the runtime application, the REST service, or the Windows service.

Building this plugin copies its DLL to the local Windows Service output
`plugins` directory. The Windows Service has no project or assembly reference to
the plugin. Start or restart the service, then inspect
`GET /api/workflow-runtime/action-catalog`; the catalog contains `sample.greeting`.

For another host installation, set `WorkflowRuntimePluginDeployDirectory` to its
configured plugin directory or copy/package the resulting DLL through the normal
deployment pipeline.

The project demonstrates both supported extension modes:

- `GreetingAction` inherits `WorkflowActionBase`. The SDK discovers its annotated
  properties, inputs, and outputs automatically.
- `TextMetricsAction` demonstrates a variable-driven input and two independently
  mapped outputs (`Length` and `Uppercase`) that later Actions can reuse.
- `PingActionHandler` implements `IWorkflowActionHandler` directly. It has no
  parameter metadata and therefore appears as a zero-parameter Action.
