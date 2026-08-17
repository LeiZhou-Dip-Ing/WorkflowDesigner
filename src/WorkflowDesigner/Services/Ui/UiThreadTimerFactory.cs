using System.Windows.Threading;

namespace WorkflowCore.WpfDemo.Services.Ui;

public sealed class UiThreadTimerFactory : IUiTimerFactory
{
    public IUiTimer Create(TimeSpan interval) => new DispatcherTimerAdapter(interval);

    private sealed class DispatcherTimerAdapter : IUiTimer
    {
        private readonly DispatcherTimer _timer;

        public DispatcherTimerAdapter(TimeSpan interval)
        {
            _timer = new DispatcherTimer { Interval = interval };
            _timer.Tick += OnTick;
        }

        public event EventHandler? Tick;

        public void Start() => _timer.Start();

        public void Stop() => _timer.Stop();

        public void Dispose()
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            Tick = null;
        }

        private void OnTick(object? sender, EventArgs e) => Tick?.Invoke(this, e);
    }
}
