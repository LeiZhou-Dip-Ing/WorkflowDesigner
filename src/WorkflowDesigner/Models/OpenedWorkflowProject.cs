using WorkflowCore.WpfDemo.Editor;

namespace WorkflowCore.WpfDemo.Models;

/// <summary>Pairs the in-memory project with the local file that owns its saved state.</summary>
public sealed record OpenedWorkflowProject(string FullPath, WorkflowProject Project);
