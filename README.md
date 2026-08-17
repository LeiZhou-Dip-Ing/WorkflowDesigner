# WorkflowDesigner

WPF Workflow Designer application repository.

This repository is intentionally separated from the private WorkflowCore source repository. It consumes Workflow runtime, SDK, designer, script, and vision packages through NuGet package references.

Do not add private WorkflowCore or SDK source projects to this repository.

## Developer Setup

Developers can clone and work on this repository without access to the private WorkflowCore source repository.

They need:

- Read access to this WorkflowDesigner repository.
- Read access to the private GitHub Packages published by `LeiZhou-Dip-Ing`.
- A GitHub Personal Access Token for package restore.

The token is stored only in the developer's user-level NuGet configuration. It must not be committed to this repository.

Run:

```powershell
.\bootstrap.ps1
```

If the GitHub Packages source is not configured yet, the script prints the exact `dotnet nuget add source` command to run.

## Access Model

WorkflowDesigner source is open for UI and designer collaboration.

WorkflowCore source remains private. Collaborators should not be added to the WorkflowCore source repository unless they are allowed to see the runtime implementation.

Package access should be granted from GitHub Packages settings with package-level `Read` permission. Do not rely on inheriting WorkflowCore repository permissions for external UI-only collaborators.
