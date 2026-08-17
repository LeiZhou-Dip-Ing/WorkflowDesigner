using System.Windows.Threading;

namespace WorkflowCore.WpfDemo.Services.Ui;

public sealed class UiThreadDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    public UiThreadDispatcher()
    {
        _dispatcher = System.Windows.Application.Current?.Dispatcher
            ?? Dispatcher.CurrentDispatcher;
    }

    public bool HasShutdownStarted => _dispatcher.HasShutdownStarted;

    public bool CheckAccess() => _dispatcher.CheckAccess();

    public void Post(Action action, UiDispatchPriority priority = UiDispatchPriority.Normal)
    {
        ArgumentNullException.ThrowIfNull(action);
        _dispatcher.BeginInvoke(
            action,
            priority == UiDispatchPriority.DataBinding
                ? DispatcherPriority.DataBind
                : DispatcherPriority.Normal);
    }

    public async Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        await _dispatcher.InvokeAsync(action);
    }

    public async Task<T> InvokeAsync<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return await _dispatcher.InvokeAsync(action);
    }
}
