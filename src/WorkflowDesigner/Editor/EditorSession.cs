using WorkflowCore.WpfDemo.ViewModels;

namespace WorkflowCore.WpfDemo.Editor;

/// <summary>Holds the editor's local, saved, and deployed snapshots as the single session state.</summary>
public sealed class EditorSession : ObservableObject
{
    private WorkflowProject _project;
    private WorkflowMethod? _selectedMethod;
    private MethodLine? _selectedMethodLine;
    private string _savedProjectJson = string.Empty;
    private string? _runtimeProjectJson;
    private Guid? _runtimeProjectId;
    private long _runtimeRevision;
    private string _runtimeContentHash = string.Empty;
    private Guid? _currentRunId;

    public EditorSession()
        : this(new WorkflowProject { Name = "Workflow Project" })
    {
    }

    public EditorSession(WorkflowProject project)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
    }

    public WorkflowProject Project
    {
        get => _project;
        set => SetProperty(ref _project, value ?? throw new ArgumentNullException(nameof(value)));
    }

    public WorkflowMethod? SelectedMethod
    {
        get => _selectedMethod;
        set => SetProperty(ref _selectedMethod, value);
    }

    public MethodLine? SelectedMethodLine
    {
        get => _selectedMethodLine;
        set => SetProperty(ref _selectedMethodLine, value);
    }

    public string SavedProjectJson
    {
        get => _savedProjectJson;
        set => SetProperty(ref _savedProjectJson, value ?? string.Empty);
    }

    public string? RuntimeProjectJson
    {
        get => _runtimeProjectJson;
        set => SetProperty(ref _runtimeProjectJson, value);
    }

    public Guid? RuntimeProjectId
    {
        get => _runtimeProjectId;
        set
        {
            if (SetProperty(ref _runtimeProjectId, value))
            {
                OnPropertyChanged(nameof(IsCurrentProjectActive));
            }
        }
    }

    public bool IsCurrentProjectActive
        => RuntimeProjectId.HasValue
           && RuntimeProjectId.Value != Guid.Empty
           && RuntimeProjectId.Value == Project.ProjectId;

    public long RuntimeRevision
    {
        get => _runtimeRevision;
        set => SetProperty(ref _runtimeRevision, value);
    }

    public string RuntimeContentHash
    {
        get => _runtimeContentHash;
        set => SetProperty(ref _runtimeContentHash, value ?? string.Empty);
    }

    public Guid? CurrentRunId
    {
        get => _currentRunId;
        set => SetProperty(ref _currentRunId, value);
    }

    public void ClearRuntimeProjectState()
    {
        RuntimeProjectJson = null;
        RuntimeProjectId = null;
        RuntimeRevision = 0;
        RuntimeContentHash = string.Empty;
    }
}
