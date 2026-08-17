namespace WorkflowCore.WpfDemo.Services.Ui;

public enum UiDispatchPriority
{
    Normal,
    DataBinding
}

public interface IUiDispatcher
{
    bool HasShutdownStarted { get; }

    bool CheckAccess();

    void Post(Action action, UiDispatchPriority priority = UiDispatchPriority.Normal);

    Task InvokeAsync(Action action);

    Task<T> InvokeAsync<T>(Func<T> action);
}
