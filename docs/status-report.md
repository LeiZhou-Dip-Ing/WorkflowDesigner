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

## Collaborator Access Model

External WorkflowDesigner collaborators should not be invited to the private WorkflowCore source repository unless they are allowed to see the runtime implementation.

The intended model is:

- WorkflowDesigner repository access for source collaboration.
- GitHub Packages package-level `Read` access for WorkflowCore, WorkflowSdk, WorkflowWpfSdk, and supporting WorkflowRuntime/WorkflowDesigner packages.
- User-level NuGet credentials on each developer machine.
- No package token, password, PAT, or secret stored in this repository.

This allows collaborators to restore, build, run, debug, and continue developing WorkflowDesigner while WorkflowCore source remains private.
