using System.Collections.ObjectModel;
using WorkflowCore.WpfDemo.ViewModels;

namespace WorkflowCore.WpfDemo.Models;

public sealed class ActionRunItem : ObservableObject
{
    private RuntimeActionState _state = RuntimeActionState.Running;
    private DateTimeOffset? _finishedAt;
    private bool _isExpanded = true;

    public Guid RunId { get; init; }

    public string MethodName { get; init; } = string.Empty;

    public DateTimeOffset StartedAt { get; init; }

    public string Title => $"Run {StartedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}";

    public ObservableCollection<RuntimeEventItem> Steps { get; } = new();

    public RuntimeActionState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(IsTerminal));
            }
        }
    }

    public string Status => State.ToString();

    public bool IsTerminal => State is RuntimeActionState.Completed
        or RuntimeActionState.Failed
        or RuntimeActionState.Cancelled;

    public string Duration
    {
        get
        {
            var end = _finishedAt ?? DateTimeOffset.Now;
            return (end - StartedAt).ToString(@"hh\:mm\:ss\.fff");
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public void SetState(RuntimeActionState state, DateTimeOffset timestamp)
    {
        State = state;
        if (IsTerminal) _finishedAt = timestamp;
        OnPropertyChanged(nameof(Duration));
    }
}
