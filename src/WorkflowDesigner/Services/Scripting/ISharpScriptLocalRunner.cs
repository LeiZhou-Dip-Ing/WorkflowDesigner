using WorkflowRuntime.ScriptCompiler;

namespace WorkflowCore.WpfDemo.Services.Scripting;

public interface ISharpScriptLocalRunner : IDisposable
{
    Task<SharpScriptLocalRunResult> RunAsync(
        Guid scriptUid,
        string source,
        string fileName,
        IReadOnlyDictionary<string, string> inputs,
        CancellationToken cancellationToken = default);

    Task<SharpScriptLocalRunResult> RunAsync(
        Guid scriptUid,
        string source,
        string fileName,
        IReadOnlyDictionary<string, string> inputs,
        IReadOnlyList<string> referencePaths,
        CancellationToken cancellationToken = default)
        => RunAsync(scriptUid, source, fileName, inputs, cancellationToken);

    void Retire(Guid scriptUid);
}

public sealed class SharpScriptLocalRunResult
{
    public bool Succeeded { get; init; }

    public SharpScriptContract? Contract { get; init; }

    public IReadOnlyDictionary<string, object?> Outputs { get; init; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<SharpScriptDiagnostic> Diagnostics { get; init; } = Array.Empty<SharpScriptDiagnostic>();

    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();
}
