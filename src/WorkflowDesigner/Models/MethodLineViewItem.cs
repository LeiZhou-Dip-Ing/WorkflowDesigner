using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.ViewModels;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.Models;

public sealed class MethodLineViewItem : ObservableObject
{
    private readonly Action _lineChanged;
    private bool _isExpanded;
    private bool _hasChildren;
    private int _displayNestingLevel;
    private string _description;
    private bool _isDebugCurrent;

    public MethodLineViewItem(
        MethodLine line,
        WorkflowActionDescriptorDto? descriptor,
        ActionTemplateItem? actionTemplate,
        bool isExpanded,
        Action lineChanged,
        Action<MethodLineViewItem> toggleExpansion)
    {
        Line = line ?? throw new ArgumentNullException(nameof(line));
        Descriptor = descriptor;
        ActionTemplate = actionTemplate;
        _isExpanded = isExpanded;
        _lineChanged = lineChanged ?? throw new ArgumentNullException(nameof(lineChanged));
        _description = CreateDescription();
        ToggleExpansionCommand = new RelayCommand(() => toggleExpansion(this), () => HasChildren);
    }

    public MethodLine Line { get; }

    public WorkflowActionDescriptorDto? Descriptor { get; }

    public ActionTemplateItem? ActionTemplate { get; }

    public string DisplayName => Descriptor?.DisplayName ?? Line.Action?.ActionType ?? "Unavailable";

    public string Description => _description;

    public string? Comment
    {
        get => Line.Comment;
        set
        {
            if (string.Equals(Line.Comment, value, StringComparison.Ordinal))
            {
                return;
            }

            Line.Comment = value;
            OnPropertyChanged();
            _lineChanged();
        }
    }

    public int DisplayIndex => Line.SequenceNo + 1;

    public int DisplayNestingLevel
    {
        get => _displayNestingLevel;
        internal set
        {
            if (SetProperty(ref _displayNestingLevel, value))
            {
                OnPropertyChanged(nameof(IndentWidth));
            }
        }
    }

    public double IndentWidth => DisplayNestingLevel * 18d;

    public bool IsDeactivated => !Line.IsActive || Line.Action?.IsActive == false;

    public bool IsDebugCurrent
    {
        get => _isDebugCurrent;
        set => SetProperty(ref _isDebugCurrent, value);
    }

    public bool HasChildren
    {
        get => _hasChildren;
        set
        {
            if (SetProperty(ref _hasChildren, value))
            {
                ToggleExpansionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public RelayCommand ToggleExpansionCommand { get; }

    public void Refresh()
    {
        var description = CreateDescription();
        if (!string.Equals(_description, description, StringComparison.Ordinal))
        {
            _description = description;
            OnPropertyChanged(nameof(Description));
        }

        OnPropertyChanged(nameof(Comment));
        OnPropertyChanged(nameof(IsDeactivated));
        OnPropertyChanged(nameof(DisplayIndex));
        OnPropertyChanged(nameof(IndentWidth));
    }

    private string CreateDescription()
        => Line.Action == null
            ? string.Empty
            : ActionDisplayTextFormatter.Format(Descriptor, Line.Action);
}
