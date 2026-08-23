# OpenCV single-DLL extension

This project is the advanced extension example. It produces one deliverable:

`WorkflowRuntime.OpenCvSamplePlugin.dll`

The same DLL contains two independent public entry points:

- `Action/OpenCvSamplePlugin.cs` registers Runtime Actions through `WorkflowRuntime.ActionSdk`.
- `UI/OpenCvSampleDesignerExtension.cs` registers optional WPF editors and workspaces through `WorkflowWpfSdk`.

Shared IDs live under `Shared/`. Building the project deploys the same DLL to both the Runtime
`plugins` directory and the Designer `designer-plugins` directory. No WorkflowCore, Runtime host,
or Designer source change is required when adding an OpenCV Action or its optional UI.

The assembly-level `WorkflowActionPluginEntryPoint` tells the Runtime exactly which Action plugin
class to load, so it never reflects over the WPF types. The Designer independently discovers the
`IWorkflowDesignerExtension` entry point. Runtime and UI remain separate at execution time even
though distribution is one DLL.

## Ordinary Actions stay simple

Do not copy this WPF setup for a normal business Action. A normal Action plugin only references
`WorkflowSdk`, declares metadata on its Action classes, and receives the generated property panel.
It does not need the entry-point attribute, a `UI` folder, or `WorkflowWpfSdk`.

## Add an advanced extension

1. Add or edit Action classes only under `Action/`.
2. Register them in `Action/OpenCvSamplePlugin.cs`.
3. Add optional custom WPF editors under `UI/` and register them in
   `UI/OpenCvSampleDesignerExtension.cs`.
4. Put keys shared by Action metadata and UI registration under `Shared/`.
5. Build this project. The single DLL is deployed to both hosts automatically in Debug builds.

The Runtime receives image handles and publishes previews through the public Vision SDK. WPF never
receives OpenCvSharp `Mat` instances from the Runtime process.
