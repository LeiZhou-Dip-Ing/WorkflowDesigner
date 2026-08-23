# Extension framework

WorkflowDesigner is a generic host. Business UI is supplied by an extension and selected only by
registered metadata keys.

## Project shape

An advanced extension remains one project:

```text
Workflow.Vendor.csproj
  extension.json
  Action/
  Runtime/
  UI/
  Shared/
```

`Shared/` defines one identity and all protocol keys. Action, Designer, Resource, and Command
entry points report that same identity and version. `SdkContractMajor` describes API
compatibility; it is independent from the NuGet package version, assembly version, and extension
version.

## UI registration

- `RegisterPropertyEditor` maps an editor key to an extension DataTemplate.
- `RegisterWorkspace` maps a workspace key to an extension view factory.
- `RegisterActionEditor` maps a double-click key to an extension window factory.
- Unknown keys fall back to generic Properties UI.
- Duplicate keys fail with the capability type, key, existing owner, and new owner.

Views bind to `IWorkflowDesignerActionContext.Properties`. Optional facilities are obtained with
`GetCapability<T>()`; a motion extension is therefore not forced to reference preview or resource
contracts.

## Runtime commands

Use persistent Action fields for configuration. Use extension commands for operations:

```text
Designer ViewModel
  -> IWorkflowDesignerActionContext.ExecuteCommandAsync
  -> POST /api/workflow-runtime/extensions/commands
  -> WorkflowExtensionCommandRegistry
  -> IWorkflowExtensionCommandHandler
  -> WorkflowDesignerCommandResult
```

The key is `(ExtensionId, CommandId)`. Payload and result data are generic dictionaries, and the
target Action identity and cancellation token travel with the request.

## Resource values

New Actions use `resource` for provider-owned handles. The serialized value stays a string; its
meaning belongs to the registered Resource provider. Legacy `image` fields remain readable as a
resource alias, but new plugins should not declare that type.

## Discovery and dependencies

Each formal extension directory contains `extension.json`. Loaders read its `entryAssembly` and do
not scan dependency DLLs for entry points. Direct DLL discovery remains only for legacy folders
without a manifest.

`AssemblyDependencyResolver` resolves extension-local managed dependencies from the entry
assembly and deps file. Runtime and Designer loaders also resolve extension-local native files
under `runtimes/<rid>/native`. Hot reload and process isolation are outside this contract.

## Examples

- OpenCV: Actions + image resource provider + image workspace + ROI/dialog ViewModels + Learn and Match commands.
- HALCON: Actions + HImage provider + HALCON workspace + shape-model dialog + Learn command.
- Motion: Move/Home Actions + axis workspace + Jog/Home/Teach commands, with no ResourceSdk dependency.

Removing every optional extension leaves the Designer, generic property/editor hosts, method
editor, script editor, Runtime, and ordinary Action plugins operational.
