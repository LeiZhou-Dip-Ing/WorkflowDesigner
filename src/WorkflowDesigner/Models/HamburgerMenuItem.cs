namespace WorkflowCore.WpfDemo.Models;

public sealed class HamburgerMenuItem
{
    public string Key { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string IconKey { get; init; } = DocumentIconKeys.Method;

    public bool HasSubmenu { get; init; }

    public bool IsBottom { get; init; }
}
