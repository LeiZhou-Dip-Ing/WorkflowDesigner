namespace WorkflowCore.WpfDemo.Services;

public sealed record SqlScriptPreviewResult(
    bool Succeeded,
    string Message,
    IReadOnlyList<Dictionary<string, string?>> Rows,
    int RowsAffected);
