using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly object _pendingRuntimeEventsLock = new();
    private readonly List<WorkflowRuntimeEventDto> _pendingRuntimeEvents = new();

    private void RuntimeApiOnRuntimeEventReceived(object? sender, WorkflowRuntimeEventDto runtimeEvent)
    {
        var currentRunId = _session.CurrentRunId;
        var acceptedRunId = currentRunId ?? _runSession.LastRunId;

        if (!acceptedRunId.HasValue)
        {
            if (_runSession.IsRunning)
            {
                lock (_pendingRuntimeEventsLock)
                {
                    _pendingRuntimeEvents.Add(runtimeEvent);
                }
            }

            return;
        }

        if (acceptedRunId.Value != runtimeEvent.RunId)
        {
            // During the short interval before StartRunAsync returns the new run id, queue
            // events instead of accidentally accepting/dropping them against LastRunId.
            if (_runSession.IsRunning && !currentRunId.HasValue)
            {
                lock (_pendingRuntimeEventsLock)
                {
                    _pendingRuntimeEvents.Add(runtimeEvent);
                }
            }

            return;
        }

        ApplyRuntimeEvent(runtimeEvent);
    }

    private void ApplyRuntimeEvent(WorkflowRuntimeEventDto runtimeEvent)
    {
        void Apply()
        {
            _actionRunLog.Apply(runtimeEvent);
            ApplyResourcePreviewEvent(runtimeEvent);
            ApplyDebugEvent(runtimeEvent);
        }

        if (_uiDispatcher.CheckAccess())
        {
            Apply();
        }
        else
        {
            _uiDispatcher.Post(Apply);
        }
    }

    private void FlushPendingRuntimeEvents()
    {
        var acceptedRunId = _session.CurrentRunId ?? _runSession.LastRunId;
        List<WorkflowRuntimeEventDto> events;
        lock (_pendingRuntimeEventsLock)
        {
            events = acceptedRunId.HasValue
                ? _pendingRuntimeEvents.Where(item => item.RunId == acceptedRunId.Value).ToList()
                : [];
            _pendingRuntimeEvents.Clear();
        }

        foreach (var runtimeEvent in events)
        {
            _actionRunLog.Apply(runtimeEvent);
            ApplyResourcePreviewEvent(runtimeEvent);
            ApplyDebugEvent(runtimeEvent);
        }
    }

    private void ApplyDebugEvent(WorkflowRuntimeEventDto runtimeEvent)
    {
        if (string.Equals(runtimeEvent.EventType, "DebugPaused", StringComparison.OrdinalIgnoreCase))
        {
            SetDebugLocation(runtimeEvent.MethodName, runtimeEvent.LineNumber, runtimeEvent.LineUid);
            IsDebugPaused = true;
            StatusText = $"Paused after step {runtimeEvent.LineNumber}: {runtimeEvent.ActionType}.";
            RefreshSelectedResourcePreview();
        }
        else if (string.Equals(runtimeEvent.EventType, "DebugResumed", StringComparison.OrdinalIgnoreCase))
        {
            IsDebugPaused = false;
        }
    }

    private void RunSessionOnStateChanged(object? sender, EventArgs e)
        => _uiDispatcher.Post(() =>
        {
            FlushPendingRuntimeEvents();
            RefreshSelectedResourcePreview();
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsStepRun));
            OnPropertyChanged(nameof(RunState));
            OnPropertyChanged(nameof(IsPaused));
            RaiseCommandStates();
        });
}
