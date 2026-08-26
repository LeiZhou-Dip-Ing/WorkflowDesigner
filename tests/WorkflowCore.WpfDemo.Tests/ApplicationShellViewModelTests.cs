using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Models;
using WorkflowCore.WpfDemo.Services.Projects;
using WorkflowCore.WpfDemo.Services.Ui;
using WorkflowCore.WpfDemo.ViewModels;
using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class ApplicationShellViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Startup_HasNoActiveOrUntitledProject()
    {
        var context = CreateContext();

        Assert.True(context.ViewModel.IsStartPageVisible);
        Assert.Null(context.ViewModel.ActiveWorkspace);
        Assert.Equal(0, context.Workspaces.CreateCount);
    }

    [Fact]
    public void Recents_AreGroupedByLastOpenedAtInTheCurrentNaturalWeek()
    {
        var entries = new[]
        {
            Entry("Today", Now.AddHours(-1)),
            Entry("Monday", new DateTimeOffset(2026, 7, 20, 8, 0, 0, TimeSpan.Zero)),
            Entry("Earlier", new DateTimeOffset(2026, 7, 19, 20, 0, 0, TimeSpan.Zero))
        };

        var groups = CreateContext(entries).ViewModel.RecentProjectGroups;

        Assert.Equal(new[] { "Today", "This Week", "Earlier" }, groups.Select(group => group.Name));
        Assert.Equal("Today", Assert.Single(groups[0].Projects).DisplayName);
        Assert.Equal("Monday", Assert.Single(groups[1].Projects).DisplayName);
        Assert.Equal("Earlier", Assert.Single(groups[2].Projects).DisplayName);
    }

    [Fact]
    public void Search_FiltersNameAndFullPathWithoutChangingStoredRecents()
    {
        var context = CreateContext(new[]
        {
            Entry("Packaging", Now, @"C:\Projects\Line.json"),
            Entry("Vision", Now.AddMinutes(-1), @"C:\Inspection\Vision.json")
        });

        context.ViewModel.SearchText = "inspection";

        Assert.Equal("Vision", Assert.Single(Assert.Single(context.ViewModel.RecentProjectGroups).Projects).DisplayName);
        Assert.Equal(2, context.Recents.Load().Count);
        context.ViewModel.SearchText = string.Empty;
        Assert.Equal(2, Assert.Single(context.ViewModel.RecentProjectGroups).Projects.Count);
    }

    [Fact]
    public void Search_MatchesProjectNameIgnoringCase()
    {
        var context = CreateContext(new[]
        {
            Entry("Packaging Workflow", Now),
            Entry("Vision", Now.AddMinutes(-1))
        });

        context.ViewModel.SearchText = "PACKAGING";

        Assert.Equal(
            "Packaging Workflow",
            Assert.Single(Assert.Single(context.ViewModel.RecentProjectGroups).Projects).DisplayName);
    }

    [Fact]
    public void RecentGroup_SortsProjectsByLastOpenedAtDescending()
    {
        var context = CreateContext(new[]
        {
            Entry("Old", Now.AddHours(-3)),
            Entry("Newest", Now.AddMinutes(-5)),
            Entry("Middle", Now.AddHours(-1))
        });

        Assert.Equal(
            new[] { "Newest", "Middle", "Old" },
            Assert.Single(context.ViewModel.RecentProjectGroups).Projects.Select(project => project.DisplayName));
    }

    [Fact]
    public void NewProjectCancellation_CreatesNoProjectOrWorkspace()
    {
        var context = CreateContext();
        context.Dialogs.NewProjectPath = null;

        context.ViewModel.NewProjectCommand.Execute(null);

        Assert.Equal(0, context.ProjectFiles.CreateCount);
        Assert.Equal(0, context.Workspaces.CreateCount);
        Assert.Null(context.ViewModel.ActiveWorkspace);
    }

    [Fact]
    public void NewProject_ActivatesWorkspaceAndUpdatesRecents()
    {
        var context = CreateContext();
        context.Dialogs.NewProjectPath = @"C:\Projects\Packaging.json";

        context.ViewModel.NewProjectCommand.Execute(null);

        Assert.NotNull(context.ViewModel.ActiveWorkspace);
        Assert.False(context.ViewModel.IsStartPageVisible);
        Assert.Equal("Packaging", Assert.Single(context.Recents.Load()).DisplayName);
    }

    [Fact]
    public void ProjectHub_TogglesWithoutClosingTheActiveWorkspace()
    {
        var context = CreateContext();
        context.Dialogs.NewProjectPath = @"C:\Projects\Packaging.json";
        context.ViewModel.NewProjectCommand.Execute(null);
        var workspace = context.ViewModel.ActiveWorkspace;

        Assert.False(context.ViewModel.IsProjectHubVisible);
        context.ViewModel.ShowProjectHubCommand.Execute(null);

        Assert.True(context.ViewModel.IsProjectHubVisible);
        Assert.Same(workspace, context.ViewModel.ActiveWorkspace);

        context.ViewModel.HideProjectHubCommand.Execute(null);
        Assert.False(context.ViewModel.IsProjectHubVisible);
        Assert.Same(workspace, context.ViewModel.ActiveWorkspace);
    }

    [Fact]
    public void MinimizedRibbon_TabSelectionOpensTemporaryPreview()
    {
        var viewModel = CreateContext().ViewModel;

        viewModel.ToggleRibbonMinimizedCommand.Execute(null);

        Assert.True(viewModel.IsRibbonMinimized);
        Assert.False(viewModel.IsRibbonPreviewOpen);

        viewModel.SelectUserSettingsTabCommand.Execute(null);

        Assert.True(viewModel.IsUserSettingsTabSelected);
        Assert.True(viewModel.IsRibbonPreviewOpen);

        viewModel.IsRibbonPreviewOpen = false;
        viewModel.ToggleRibbonMinimizedCommand.Execute(null);

        Assert.False(viewModel.IsRibbonMinimized);
        Assert.False(viewModel.IsRibbonPreviewOpen);
    }

    [Fact]
    public void OpenProjectCancellation_CreatesNoRecentOrWorkspace()
    {
        var context = CreateContext();
        context.Dialogs.OpenProjectPath = null;

        context.ViewModel.OpenProjectCommand.Execute(null);

        Assert.Empty(context.Recents.Load());
        Assert.Equal(0, context.Workspaces.CreateCount);
        Assert.True(context.ViewModel.IsStartPageVisible);
    }

    [Fact]
    public void OpenProject_ActivatesWorkspaceAndRecordsLastOpenedAt()
    {
        var context = CreateContext();
        context.Dialogs.OpenProjectPath = @"C:\Projects\Inspection.json";

        context.ViewModel.OpenProjectCommand.Execute(null);

        var recent = Assert.Single(context.Recents.Load());
        Assert.Equal("Inspection", recent.DisplayName);
        Assert.Equal(Now, recent.LastOpenedAt);
        Assert.NotNull(context.ViewModel.ActiveWorkspace);
    }

    [Fact]
    public void MissingRecentProject_UsesInlineConfirmationAndCanBeRemoved()
    {
        var missing = Entry("Missing", Now);
        var context = CreateContext(new[] { missing });
        context.ProjectFiles.MissingPath = missing.FullPath;

        context.ViewModel.OpenRecentProjectCommand.Execute(missing);
        Assert.True(context.ViewModel.IsMissingProjectDialogOpen);

        context.ViewModel.RemoveMissingProjectCommand.Execute(null);
        Assert.False(context.ViewModel.IsMissingProjectDialogOpen);
        Assert.Empty(context.Recents.Load());
    }

 
    private static TestContext CreateContext(IEnumerable<RecentProjectEntry>? entries = null)
    {
        var recents = new MemoryRecentProjectRepository(entries ?? Array.Empty<RecentProjectEntry>());
        var projectFiles = new FakeProjectFileService();
        var workspaces = new FakeWorkspaceFactory();
        var dialogs = new FakeFileDialogs();
        var viewModel = new ApplicationShellViewModel(
            recents,
            projectFiles,
            workspaces,
            dialogs,
            new FixedTimeProvider(Now));
        return new TestContext(viewModel, recents, projectFiles, workspaces, dialogs);
    }

    private static RecentProjectEntry Entry(string name, DateTimeOffset openedAt, string? path = null)
        => new(path ?? $@"C:\Projects\{name}.json", name, openedAt);

    private sealed record TestContext(
        ApplicationShellViewModel ViewModel,
        MemoryRecentProjectRepository Recents,
        FakeProjectFileService ProjectFiles,
        FakeWorkspaceFactory Workspaces,
        FakeFileDialogs Dialogs);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class MemoryRecentProjectRepository(IEnumerable<RecentProjectEntry> entries) : IRecentProjectRepository
    {
        private readonly List<RecentProjectEntry> _entries = entries.ToList();
        public IReadOnlyList<RecentProjectEntry> Load() => _entries.ToList();
        public void AddOrUpdate(string fullPath, string displayName, DateTimeOffset lastOpenedAt)
        {
            _entries.RemoveAll(entry => ProjectPathIdentity.Equals(entry.FullPath, fullPath));
            _entries.Add(new RecentProjectEntry(fullPath, displayName, lastOpenedAt));
        }
        public void Remove(string fullPath) => _entries.RemoveAll(entry => ProjectPathIdentity.Equals(entry.FullPath, fullPath));
    }

    private sealed class FakeProjectFileService : IWorkflowProjectFileService
    {
        public int CreateCount { get; private set; }
        public string? MissingPath { get; set; }
        public OpenedWorkflowProject Create(string filePath)
        {
            CreateCount++;
            return Project(filePath);
        }
        public OpenedWorkflowProject Open(string filePath)
        {
            if (MissingPath != null && ProjectPathIdentity.Equals(MissingPath, filePath))
            {
                throw new FileNotFoundException("Missing", filePath);
            }
            return Project(filePath);
        }
        public void Save(string filePath, WorkflowProject project) { }
        private static OpenedWorkflowProject Project(string path)
            => new(ProjectPathIdentity.Normalize(path), new WorkflowProject { Name = Path.GetFileNameWithoutExtension(path), Version = "1.0" });
    }

    private sealed class FakeWorkspaceFactory : IProjectWorkspaceFactory
    {
        public int CreateCount { get; private set; }
        public FakeWorkspace? LastWorkspace { get; private set; }
        public IProjectWorkspace Create(OpenedWorkflowProject openedProject)
        {
            CreateCount++;
            return LastWorkspace = new FakeWorkspace();
        }
    }

    private sealed class FakeWorkspace : IProjectWorkspace
    {
        public bool Disposed { get; private set; }
        public bool CanCloseEditor() => true;
        public void Dispose() => Disposed = true;
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeFileDialogs : IEditorFileDialogs
    {
        public string? NewProjectPath { get; set; }
        public string? OpenProjectPath { get; set; }
        public string? SelectNewProjectPath() => NewProjectPath;
        public string? SelectProjectOpenFile() => OpenProjectPath;
        public string? SelectDocumentImportFile() => null;
        public string? SelectProjectImportFile() => null;
        public string? SelectDocumentExportPath(string documentName, string suggestedFileName) => null;
        public string? SelectProjectExportPath() => null;
    }
}
