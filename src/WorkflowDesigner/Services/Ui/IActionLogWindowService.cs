using WorkflowCore.WpfDemo.Services.Runtime;

namespace WorkflowCore.WpfDemo.Services.Ui;

/// <summary>Owns the view-specific lifetime of Action Log windows.</summary>
public interface IActionLogWindowService
{
    void Show(ActionRunLog log);

    void Close(ActionRunLog log);
}

internal sealed class NullActionLogWindowService : IActionLogWindowService
{
    public static NullActionLogWindowService Instance { get; } = new();

    public void Show(ActionRunLog log) { }

    public void Close(ActionRunLog log) { }
}
