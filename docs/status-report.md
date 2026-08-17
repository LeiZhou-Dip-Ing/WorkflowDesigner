# WorkflowDesigner Split Status

## Package Boundary

WorkflowDesigner is now a separate local repository at `C:\Users\dip_i\Documents\Codex\WorkflowDesigner`.

The WPF application project is `src\WorkflowDesigner\WorkflowDesigner.csproj`.

Current main package references:

- WorkflowCore
- WorkflowWpfSdk
- WorkflowRuntime.ScriptCompiler

`WorkflowRuntime.ScriptCompiler` remains transitional because the current Designer source directly uses compiler services. The intended final shape is to expose that interaction through WorkflowCore or WorkflowWpfSdk so the app can return to only WorkflowCore + WorkflowWpfSdk as major platform references.

## Source Separation

WorkflowDesigner no longer contains these source projects:

- `src\WorkflowCore`
- `src\WorkflowRuntime.Application`
- `src\WorkflowRuntime.Contracts`
- `src\WorkflowRuntime.ActionSdk`
- `src\WorkflowRuntime.ScriptSdk`
- `src\WorkflowRuntime.ScriptCompiler`
- `src\WorkflowRuntime.VisionSdk`
- `src\WorkflowDesigner.Contracts`
- `src\WorkflowDesigner.WpfSdk`

Those are restored as packages.

## Verification

- Restore from temporary local packages: passed.
- Build from packages: passed.
- Tests: 153 passed, 20 failed under Codex sandbox due WPF font/cache initialization. Needs real Windows desktop or CI runner verification.

## GitHub Packages

The committed `NuGet.Config` uses GitHub Packages and nuget.org only. It does not contain secrets.

`NuGet.local.Config` is ignored and exists only for local split validation before private packages are published.
