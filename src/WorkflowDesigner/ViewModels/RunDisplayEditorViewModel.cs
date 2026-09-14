using WorkflowCore.WpfDemo.Editor;

namespace WorkflowCore.WpfDemo.ViewModels;

public sealed class RunDisplayEditorViewModel : ObservableObject, IEditableDockDocument
{
    private readonly MainWindowViewModel _owner;
    private bool _isDirty;

    public RunDisplayEditorViewModel(WorkflowRunDisplay runDisplay, MainWindowViewModel owner)
    {
        RunDisplay = runDisplay ?? throw new ArgumentNullException(nameof(runDisplay));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public WorkflowRunDisplay RunDisplay { get; }

    public string ContentId => $"run-display:{RunDisplay.Uid:N}";

    public string Title => IsDirty ? $"{RunDisplay.Name} *" : RunDisplay.Name;

    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (SetProperty(ref _isDirty, value))
            {
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    public WorkflowEditorDocument CreateExportDocument()
        => WorkflowEditorDocument.FromRunDisplay(RunDisplay);

    public void UpdateXaml(string xaml)
    {
        if (string.Equals(RunDisplay.Xaml, xaml, StringComparison.Ordinal))
        {
            return;
        }

        RunDisplay.Xaml = xaml;
        _owner.MarkRunDisplayChanged();
    }
}
