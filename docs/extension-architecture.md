# Extension architecture

## One Extension = One csproj

An extension keeps Runtime, Designer and shared identifiers in source folders inside one project:

```text
MyCompany.Extension/
|-- Runtime/
|-- Designer/
|-- Shared/
`-- MyCompany.Extension.csproj
```

The project produces one DLL. Runtime activates only the type named by
`WorkflowActionPluginEntryPoint` and, when needed, `WorkflowResourceProviderEntryPoint`.
Designer activates only the type named by `WorkflowDesignerExtensionEntryPoint`. Exported-type
scanning remains a compatibility fallback for old extensions, not the primary discovery path.

## Capability references

| Extension capability | Required package |
| --- | --- |
| Action metadata, generated property panel, execution | `WorkflowRuntime.ActionSdk` |
| C# script contribution | `WorkflowRuntime.ScriptSdk` |
| WPF property editor, workspace, double-click editor, command | `WorkflowDesigner.WpfSdk` |
| Non-serializable resource handles, lifetime, optional preview | `WorkflowRuntime.ResourceSdk` |

References are additive. A Motion extension with no runtime resource does not reference
`WorkflowRuntime.ResourceSdk`. A simple Action does not reference WPF.

## Presentation is explicit

An image tool declares `WorkspaceKind = WorkflowWorkspaceKeys.Image` and an editor key explicitly.
`ActionKind` never selects a workspace or editor. Custom keys are registered by the extension's
Designer entry point. `WorkflowActionEditorKeys.Vision` is a compatibility alias only;
new extensions use `WorkflowActionEditorKeys.Image`.

## Resources and previews

`IWorkflowResourceRuntime` owns generic store, resolve, metadata and cleanup operations. Generic
metadata contains `ResourceType`, `ContentType`, `Source` and `Properties`; image dimensions and
pixel format belong in provider properties. A provider implements `IWorkflowResourcePreviewProvider`
only when it supports previews. Each preview supplies its own bytes, content type and properties.
The official HTTP route is `/api/workflow-runtime/resources/previews/...` and does not assume PNG.

## Example shapes

- Simple Action: one csproj; reference `WorkflowRuntime.ActionSdk`.
- OpenCV: one csproj; reference ActionSdk, ResourceSdk and Designer.WpfSdk; keep Actions, Mat provider,
  image workspace, ROI UI, property editors and double-click editors in folders in that project.
- HALCON: one csproj with the same three optional capabilities; an HImage provider owns HALCON image
  metadata and preview encoding.
- Motion: one csproj; reference ActionSdk and Designer.WpfSdk for Move/Home/Jog/Teach Position UI.
  Do not reference ResourceSdk unless the extension actually exposes runtime resource handles.
