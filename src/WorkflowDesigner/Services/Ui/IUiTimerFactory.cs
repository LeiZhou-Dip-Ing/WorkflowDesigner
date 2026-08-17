namespace WorkflowCore.WpfDemo.Services.Ui;

public interface IUiTimer : IDisposable
{
    event EventHandler? Tick;

    void Start();

    void Stop();
}

public interface IUiTimerFactory
{
    IUiTimer Create(TimeSpan interval);
}
