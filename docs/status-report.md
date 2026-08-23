# WorkflowDesigner Split Status

## Package Boundary

WorkflowDesigner is now a separate local repository at `C:\Users\dip_i\Documents\Codex\WorkflowDesigner`.

The WPF application project is `src\WorkflowDesigner\WorkflowDesigner.csproj`.

Current main package references:

- WorkflowCore
- WorkflowRuntime.ActionSdk
- WorkflowRuntime.ResourceSdk
- WorkflowDesigner.WpfSdk
- WorkflowRuntime.ScriptCompiler

`WorkflowRuntime.ScriptCompiler` remains a compiler-service package used by the Designer. Third-party Action extensions do not reference it.

## Source Separation

WorkflowDesigner no longer contains these source projects:

- `src\WorkflowCore`
- `src\WorkflowRuntime.Application`
- `src\WorkflowRuntime.Contracts`
- `src\WorkflowRuntime.ActionSdk`
- `src\WorkflowRuntime.ScriptSdk`
- `src\WorkflowRuntime.ScriptCompiler`
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
- GitHub Packages package-level `Read` access for the exact WorkflowCore, WorkflowRuntime.ActionSdk, WorkflowRuntime.ResourceSdk and WorkflowDesigner.WpfSdk packages required by the repository.
- User-level NuGet credentials on each developer machine.
- No package token, password, PAT, or secret stored in this repository.

This allows collaborators to restore, build, run, debug, and continue developing WorkflowDesigner while WorkflowCore source remains private.
