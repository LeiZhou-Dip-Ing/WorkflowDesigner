# OpenCV extension

`WorkflowRuntime.OpenCvSamplePlugin.csproj` is one extension project with four capabilities:

- `Action/` contains Runtime Actions and command handlers.
- `Runtime/` contains the OpenCV resource provider.
- `UI/` contains WPF workspaces, property editors, dialogs, preview decoding, and ViewModels.
- `Shared/` contains the stable identity, workspace/editor keys, command IDs, and field names.

All entry points use `OpenCvPluginIdentity.Id`, `Version`, and `DisplayName`. `extension.json`
declares the entry assembly, SDK contract major, and capabilities. Runtime and Designer therefore
load only `WorkflowRuntime.OpenCvSamplePlugin.dll`; dependency DLLs are resolver inputs, not
extension entry points.

## Packages

The project references only public extension contracts:

- `WorkflowRuntime.ActionSdk`
- `WorkflowRuntime.ResourceSdk`
- `WorkflowDesigner.WpfSdk`

It does not reference the WorkflowDesigner application or WorkflowCore implementation source.
An ordinary metadata-only Action needs only `WorkflowRuntime.ActionSdk`; it does not need WPF or
ResourceSdk.

## Add an Action

1. Create a `WorkflowActionBase` class under `Action/`.
2. Declare inputs and outputs with metadata attributes. Resource handles use `ValueType = "resource"`.
3. Use extension-owned `WorkspaceKind` and `DoubleClickEditor` keys when custom UI is needed.
4. Register the Action in `OpenCvSamplePlugin.Register`.

The generic property panel is generated from metadata. Actions that need no special UI stop here.

## Add a workspace or double-click editor

1. Add XAML and its ViewModel under `UI/`. Keep state and commands in the ViewModel.
2. Add a stable key to `OpenCvDesignerKeys`.
3. Put the key on the Action metadata.
4. Register `key -> factory` in `OpenCvSampleDesignerExtension`.

`IWorkflowDesignerActionContext.Properties` edits the same Action state used by the generic
property panel, so dirty state and undo/redo stay integrated. Preview is optional: request
`IWorkflowDesignerResourcePreviewCapability` with `GetCapability<T>()`, then decode its content
inside the extension. The host does not expose an image type.

## Use the host UI theme

Extension views and double-click editors must use the public `WorkflowSdk*` dynamic resources.
This keeps extension UI aligned with Automation Pro and updates it immediately when the user
changes the application theme:

```xml
<UserControl Background="{DynamicResource WorkflowSdkPanelBrush}"
             Foreground="{DynamicResource WorkflowSdkTextBrush}">
    <Grid>
        <TextBox Style="{DynamicResource WorkflowSdkTextBoxStyle}" />
        <Button Style="{DynamicResource WorkflowSdkButtonStyle}" />
    </Grid>
</UserControl>
```

Available brushes cover page, surface, panel, border, text, muted text, accent, success, warning,
danger, and selection colors. Shared styles are provided for buttons, text boxes, group boxes,
tab controls, and tab items. The stable key names are also exposed by
`WorkflowDesignerThemeKeys` in `WorkflowDesigner.WpfSdk`.

Do not hard-code window chrome, panel, text, or border colors. A media viewport may deliberately
stay neutral black when that is required to inspect the actual image. Keep commands and mutable
state in the ViewModel; the theme contract does not change the MVVM extension model.

## Add a command

Configuration such as score, velocity, or position remains in Action properties. Operations such
as Learn, Match, Home, or Jog use commands:

1. Add a stable ID under `OpenCvCommandIds`.
2. Implement `IWorkflowExtensionCommandHandler` under `Action/`.
3. Register it with `builder.AddCommand<THandler>(commandId)`.
4. Call `context.ExecuteCommandAsync(new WorkflowDesignerCommandRequest(commandId, payload))`
   from the UI ViewModel.

The call path is Designer context -> Runtime command endpoint -> extension command registry ->
handler -> result. It does not temporarily rewrite workflow JSON.

## Deployment layout

Debug builds deploy the same extension directory to Runtime and Designer:

```text
OpenCvSample/
  extension.json
  WorkflowRuntime.OpenCvSamplePlugin.dll
  WorkflowRuntime.OpenCvSamplePlugin.deps.json
  OpenCvSharp.dll
  runtimes/win-x64/native/
    OpenCvSharpExtern.dll
    opencv_videoio_ffmpeg4130_64.dll
```

The extension implementation is one DLL. OpenCvSharp managed/native files remain separate vendor
dependencies so the .NET dependency resolver and Windows native loader can load supported binary
versions reliably.

## Other extension shapes

A HALCON project can use the same folders for HImage resources, ROI workspaces, shape-model
dialogs, and Learn commands. A Motion project can omit ResourceSdk and provide Move/Home Actions,
an axis workspace, and Jog/Home/Teach handlers. Both remain one csproj and use the same public SDK
contracts and manifest model.
