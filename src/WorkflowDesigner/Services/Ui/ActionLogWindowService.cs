using System.Windows;
using WorkflowCore.WpfDemo.Services.Runtime;
using WorkflowCore.WpfDemo.Views;

namespace WorkflowCore.WpfDemo.Services.Ui;

/// <summary>WPF implementation kept outside the workspace ViewModel.</summary>
public sealed class ActionLogWindowService : IActionLogWindowService
{
    private readonly Dictionary<ActionRunLog, ActionLogWindow> _windows = new();

    public void Show(ActionRunLog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        if (_windows.TryGetValue(log, out var existing) && existing.IsLoaded)
        {
            existing.Activate();
            return;
        }

        var window = new ActionLogWindow
        {
            DataContext = log,
            Owner = GetOwner()
        };
        _windows[log] = window;
        window.Closed += (_, _) => _windows.Remove(log);
        window.Show();
    }

    public void Close(ActionRunLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        if (_windows.Remove(log, out var window)) window.Close();
    }

    private static Window? GetOwner()
        => Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
           ?? Application.Current?.MainWindow;
}
