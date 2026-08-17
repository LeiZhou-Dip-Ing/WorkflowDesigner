using WorkflowRuntime.ScriptCompiler;

namespace WorkflowCore.WpfDemo.Models;

public sealed class SharpScriptDiagnosticItem
{
    public required SharpScriptDiagnosticSeverity Severity { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public int Line { get; init; }

    public int Column { get; init; }
}
