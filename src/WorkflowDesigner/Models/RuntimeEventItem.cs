using WorkflowCore.WpfDemo.ViewModels;

namespace WorkflowCore.WpfDemo.Models;

public sealed class RuntimeEventItem : ObservableObject
{
    private RuntimeActionState _state = RuntimeActionState.Running;
    private string _time = string.Empty;
    private string _errorMessage = string.Empty;
    private string _output = string.Empty;
    private int _outputPriority;
    private DateTimeOffset? _expectedCompletionAt;
    private string _remainingTime = string.Empty;

    public Guid ActionExecutionId { get; init; }

    public ActionTemplateItem? ActionTemplate { get; init; }

    public string ActionName { get; init; } = string.Empty;

    public string MethodName { get; init; } = string.Empty;

    public int? LineNumber { get; init; }

    public string Location => LineNumber.HasValue
        ? $"{MethodName} · line {LineNumber.Value}"
        : MethodName;

    public string Time
    {
        get => _time;
        private set => SetProperty(ref _time, value);
    }

    public RuntimeActionState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(IsCompleted));
                OnPropertyChanged(nameof(IsFailed));
            }
        }
    }

    public string Status => State switch
    {
        RuntimeActionState.Completed => "Succeeded",
        RuntimeActionState.Failed => "Failed",
        _ when HasCountdown => "Waiting",
        _ => "Running"
    };

    public bool IsRunning => State == RuntimeActionState.Running;

    public bool IsCompleted => State == RuntimeActionState.Completed;

    public bool IsFailed => State == RuntimeActionState.Failed;

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string Output
    {
        get => _output;
        private set => SetProperty(ref _output, value);
    }

    public bool HasCountdown => IsRunning && _expectedCompletionAt.HasValue;

    public string RemainingTime
    {
        get => _remainingTime;
        private set => SetProperty(ref _remainingTime, value);
    }

    public string Result => IsFailed
        ? ErrorMessage
        : HasCountdown
            ? RemainingTime
            : Output;

    public void Start(DateTimeOffset timestamp, int? durationMilliseconds = null)
    {
        Time = timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff");
        _expectedCompletionAt = durationMilliseconds is > 0
            ? timestamp.AddMilliseconds(durationMilliseconds.Value)
            : null;
        State = RuntimeActionState.Running;
        UpdateCountdown(DateTimeOffset.Now);
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(HasCountdown));
        OnPropertyChanged(nameof(Result));
    }

    public void UpdateCountdown(DateTimeOffset now)
    {
        if (!HasCountdown || !_expectedCompletionAt.HasValue)
        {
            return;
        }

        var remaining = Math.Max(0d, (_expectedCompletionAt.Value - now).TotalSeconds);
        RemainingTime = FormattableString.Invariant($"{remaining:0.0}s remaining");
        OnPropertyChanged(nameof(Result));
    }

    public void CaptureOutput(string output, bool isExplicitOutput)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        var priority = isExplicitOutput ? 2 : 1;
        if (priority > _outputPriority)
        {
            _outputPriority = priority;
            Output = output.Trim();
        }
        else if (priority == _outputPriority
                 && !Output.Split(" | ", StringSplitOptions.None).Contains(output.Trim(), StringComparer.Ordinal))
        {
            Output = string.IsNullOrEmpty(Output) ? output.Trim() : $"{Output} | {output.Trim()}";
        }

        OnPropertyChanged(nameof(Result));
    }

    public void Complete(DateTimeOffset timestamp)
    {
        ErrorMessage = string.Empty;
        State = RuntimeActionState.Completed;
        RemainingTime = string.Empty;
        OnPropertyChanged(nameof(HasCountdown));
        OnPropertyChanged(nameof(Result));
    }

    public void Fail(DateTimeOffset timestamp, string errorMessage)
    {
        ErrorMessage = errorMessage;
        State = RuntimeActionState.Failed;
        RemainingTime = string.Empty;
        OnPropertyChanged(nameof(HasCountdown));
        OnPropertyChanged(nameof(Result));
    }
}

public enum RuntimeActionState
{
    Running,
    Completed,
    Failed
}
