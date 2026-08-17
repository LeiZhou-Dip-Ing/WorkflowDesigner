using Prism.Commands;
using Prism.Mvvm;

namespace WorkflowCore.WpfDemo.Models;

public sealed class DockPaneItem : BindableBase
{
    private string _title = string.Empty;
    private bool _isActive;
    private bool _isSelected;

    public required string ContentId { get; init; }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string IconKey { get; init; } = DocumentIconKeys.Method;

    public required object Content { get; init; }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public DelegateCommand? CloseCommand { get; set; }

    public Action<DockPaneItem>? ActivatedCallback { get; init; }

    public Action<DockPaneItem>? ClosedCallback { get; init; }
}
