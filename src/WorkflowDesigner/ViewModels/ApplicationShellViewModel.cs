using System.Collections.ObjectModel;
using System.IO;
using WorkflowCore.WpfDemo.Models;
using WorkflowCore.WpfDemo.Services.Projects;
using WorkflowCore.WpfDemo.Services.Ui;

namespace WorkflowCore.WpfDemo.ViewModels;

/// <summary>Coordinates the application-level transition between the Start Page and one project workspace.</summary>
public sealed class ApplicationShellViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IRecentProjectRepository _recentProjects;
    private readonly IWorkflowProjectFileService _projectFiles;
    private readonly IProjectWorkspaceFactory _workspaceFactory;
    private readonly IEditorFileDialogs _fileDialogs;
    private readonly TimeProvider _timeProvider;
    private IProjectWorkspace? _activeWorkspace;
    private string _searchText = string.Empty;
  
    private string _startPageError = string.Empty;
    private RecentProjectEntry? _missingRecentProject;

    public ApplicationShellViewModel(
        IRecentProjectRepository recentProjects,
        IWorkflowProjectFileService projectFiles,
        IProjectWorkspaceFactory workspaceFactory,
        IEditorFileDialogs fileDialogs,
        TimeProvider timeProvider)
    {
        _recentProjects = recentProjects ?? throw new ArgumentNullException(nameof(recentProjects));
        _projectFiles = projectFiles ?? throw new ArgumentNullException(nameof(projectFiles));
        _workspaceFactory = workspaceFactory ?? throw new ArgumentNullException(nameof(workspaceFactory));
        _fileDialogs = fileDialogs ?? throw new ArgumentNullException(nameof(fileDialogs));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        NewProjectCommand = new RelayCommand(CreateProject);
        OpenProjectCommand = new RelayCommand(OpenProject);
        OpenRecentProjectCommand = new RelayCommand(OpenRecentProject, entry => entry is RecentProjectEntry);
        RemoveMissingProjectCommand = new RelayCommand(RemoveMissingProject, () => MissingRecentProject != null);
        KeepMissingProjectCommand = new RelayCommand(() => MissingRecentProject = null);
        CloseProjectCommand = new RelayCommand(CloseProject, () => ActiveWorkspace != null);
        RefreshRecentProjects();
    }

    public ObservableCollection<RecentProjectGroup> RecentProjectGroups { get; } = new();

    public IProjectWorkspace? ActiveWorkspace
    {
        get => _activeWorkspace;
        private set
        {
            if (SetProperty(ref _activeWorkspace, value))
            {
                OnPropertyChanged(nameof(HasActiveProject));
                OnPropertyChanged(nameof(IsStartPageVisible));
                CloseProjectCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasActiveProject => ActiveWorkspace != null;

    public bool IsStartPageVisible => ActiveWorkspace == null;

    public bool HasRecentProjects => RecentProjectGroups.Any(group => group.Projects.Count > 0);

    public bool HasStartPageError => !string.IsNullOrWhiteSpace(StartPageError);

    public bool IsMissingProjectDialogOpen => MissingRecentProject != null;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                ApplyRecentProjectFilter();
            }
        }
    }

 

    public string StartPageError
    {
        get => _startPageError;
        private set
        {
            if (SetProperty(ref _startPageError, value))
            {
                OnPropertyChanged(nameof(HasStartPageError));
            }
        }
    }

    public RecentProjectEntry? MissingRecentProject
    {
        get => _missingRecentProject;
        private set
        {
            if (SetProperty(ref _missingRecentProject, value))
            {
                OnPropertyChanged(nameof(IsMissingProjectDialogOpen));
                RemoveMissingProjectCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public RelayCommand NewProjectCommand { get; }
    public RelayCommand OpenProjectCommand { get; }
    public RelayCommand OpenRecentProjectCommand { get; }
    public RelayCommand RemoveMissingProjectCommand { get; }
    public RelayCommand KeepMissingProjectCommand { get; }
    public RelayCommand CloseProjectCommand { get; }

    public bool CanCloseApplication() => ActiveWorkspace?.CanCloseEditor() ?? true;

    public async ValueTask DisposeAsync()
    {
        if (ActiveWorkspace != null)
        {
            await ActiveWorkspace.DisposeAsync().ConfigureAwait(false);
            ActiveWorkspace = null;
        }
    }

    private void CreateProject()
    {
        var selectedPath = _fileDialogs.SelectNewProjectPath();
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        TryActivate(() => _projectFiles.Create(selectedPath));
    }

    private void OpenProject()
    {
        var selectedPath = _fileDialogs.SelectProjectOpenFile();
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        TryActivate(() => _projectFiles.Open(selectedPath));
    }

    private void OpenRecentProject(object? parameter)
    {
        if (parameter is not RecentProjectEntry entry)
        {
            return;
        }

        try
        {
            Activate(_projectFiles.Open(entry.FullPath));
        }
        catch (FileNotFoundException)
        {
            MissingRecentProject = entry;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StartPageError = $"Could not open '{entry.DisplayName}': {exception.Message}";
        }
    }

    private void TryActivate(Func<OpenedWorkflowProject> operation)
    {
        try
        {
            Activate(operation());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StartPageError = $"Could not open the Project: {exception.Message}";
        }
    }

    private void Activate(OpenedWorkflowProject openedProject)
    {
        ActiveWorkspace?.Dispose();
        ActiveWorkspace = _workspaceFactory.Create(openedProject);
        _recentProjects.AddOrUpdate(
            openedProject.FullPath,
            openedProject.Project.Name,
            _timeProvider.GetLocalNow());
        StartPageError = string.Empty;
    }

    private void CloseProject()
    {
        if (ActiveWorkspace == null || !ActiveWorkspace.CanCloseEditor())
        {
            return;
        }

        ActiveWorkspace.Dispose();
        ActiveWorkspace = null;
        RefreshRecentProjects();
    }

    private void RemoveMissingProject()
    {
        if (MissingRecentProject == null)
        {
            return;
        }

        _recentProjects.Remove(MissingRecentProject.FullPath);
        MissingRecentProject = null;
        RefreshRecentProjects();
    }

    private void RefreshRecentProjects()
    {
        StartPageError = string.Empty;
        ApplyRecentProjectFilter();
    }

    private void ApplyRecentProjectFilter()
    {
        var query = SearchText.Trim();
        var filtered = _recentProjects.Load()
            .Where(entry => query.Length == 0
                            || entry.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                            || entry.FullPath.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.LastOpenedAt)
            .ToList();

        var now = _timeProvider.GetLocalNow();
        var today = now.Date;
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        DateTime LocalDate(RecentProjectEntry entry)
            => TimeZoneInfo.ConvertTime(entry.LastOpenedAt, _timeProvider.LocalTimeZone).Date;
        ReplaceGroups(
            ("Today", filtered.Where(entry => LocalDate(entry) == today)),
            ("This Week", filtered.Where(entry =>
                LocalDate(entry) >= weekStart
                && LocalDate(entry) < today)),
            ("Earlier", filtered.Where(entry => LocalDate(entry) < weekStart)));
    }

    private void ReplaceGroups(params (string Name, IEnumerable<RecentProjectEntry> Projects)[] groups)
    {
        RecentProjectGroups.Clear();
        foreach (var group in groups)
        {
            var projects = group.Projects.OrderByDescending(entry => entry.LastOpenedAt).ToList();
            if (projects.Count > 0)
            {
                RecentProjectGroups.Add(new RecentProjectGroup(group.Name, projects));
            }
        }

        OnPropertyChanged(nameof(HasRecentProjects));
    }
}
