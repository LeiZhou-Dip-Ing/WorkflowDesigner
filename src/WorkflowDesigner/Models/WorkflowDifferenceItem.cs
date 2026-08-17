namespace WorkflowCore.WpfDemo.Models;

public sealed class WorkflowDifferenceItem
{
    public WorkflowDifferenceKind Kind { get; init; }

    public string Change => Kind switch
    {
        WorkflowDifferenceKind.LocalOnly => "Local only",
        WorkflowDifferenceKind.RuntimeOnly => "Runtime only",
        _ => "Modified"
    };

    public string Path { get; init; } = string.Empty;

    public string LocalValue { get; init; } = string.Empty;

    public string RuntimeValue { get; init; } = string.Empty;
}

public enum WorkflowDifferenceKind
{
    Modified,
    LocalOnly,
    RuntimeOnly
}
