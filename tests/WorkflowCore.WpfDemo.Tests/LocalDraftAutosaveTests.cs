using System.Text.Json.Nodes;
using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Services;
using WorkflowCore.WpfDemo.Services.Drafts;
using WorkflowCore.WpfDemo.Services.Ui;
using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class LocalDraftAutosaveTests
{
    [Fact]
    public void Load_RestoresCurrentWorkingCopyAndSavedBaseline()
    {
        var persistence = new JsonEditorDocumentPersistence(new WorkflowEditorJsonSerializer());
        var working = EditorTestProjectFactory.Create();
        working.Name = "Working project";
        var saved = persistence.Deserialize(persistence.Serialize(working));
        saved.Name = "Saved project";
        var store = new SnapshotDraftStore(new LocalDraftSnapshot
        {
            WorkflowId = "editor-default",
            IsDirty = true,
            SavedAtUtc = DateTimeOffset.UtcNow,
            Workflow = JsonNode.Parse(persistence.Serialize(working))!,
            SavedWorkflow = JsonNode.Parse(persistence.Serialize(saved))!
        });
        var session = new EditorSession();
        using var autosave = new LocalDraftAutosave(
            store,
            persistence,
            session,
            new InertTimerFactory());

        autosave.LoadMostRecent();

        Assert.Equal("Working project", session.Project.Name);
        Assert.Equal("Saved project", persistence.Deserialize(session.SavedProjectJson).Name);
        Assert.True(autosave.IsDirty);
    }

    [Fact]
    public async Task SaveSnapshot_UsesTheCurrentStableProjectId()
    {
        var persistence = new JsonEditorDocumentPersistence(new WorkflowEditorJsonSerializer());
        var project = EditorTestProjectFactory.Create();
        var store = new RecordingDraftStore();
        var session = new EditorSession(project)
        {
            SavedProjectJson = persistence.Serialize(project)
        };
        using var autosave = new LocalDraftAutosave(
            store,
            persistence,
            session,
            new InertTimerFactory());

        await autosave.SaveSnapshotAsync(persistence.Serialize(project), isDirty: false);

        Assert.Equal(project.ProjectId.ToString("D"), store.LastWorkflowId);
    }

    [Fact]
    public void RestoreProjectDraft_RestoresOnlyANewerWorkingCopyAndKeepsFileBaseline()
    {
        var persistence = new JsonEditorDocumentPersistence(new WorkflowEditorJsonSerializer());
        var fileProject = EditorTestProjectFactory.Create();
        fileProject.Name = "Saved file";
        var workingProject = persistence.Deserialize(persistence.Serialize(fileProject));
        workingProject.Name = "Recovered working copy";
        var fileJson = persistence.Serialize(fileProject);
        var store = new SnapshotDraftStore(new LocalDraftSnapshot
        {
            WorkflowId = fileProject.ProjectId.ToString("D"),
            IsDirty = true,
            SavedAtUtc = DateTimeOffset.UtcNow,
            Workflow = JsonNode.Parse(persistence.Serialize(workingProject))!,
            SavedWorkflow = JsonNode.Parse(fileJson)!
        });
        var session = new EditorSession(fileProject)
        {
            SavedProjectJson = fileJson
        };
        using var autosave = new LocalDraftAutosave(
            store,
            persistence,
            session,
            new InertTimerFactory());

        var status = autosave.RestoreProjectDraft(
            fileProject.ProjectId,
            fileJson,
            DateTimeOffset.UtcNow.AddMinutes(-1));

        Assert.Contains("Recovered newer", status);
        Assert.Equal("Recovered working copy", session.Project.Name);
        Assert.Equal("Saved file", persistence.Deserialize(session.SavedProjectJson).Name);
        Assert.True(autosave.IsDirty);
    }

    [Fact]
    public async Task FlushAsync_PersistsTheLastWorkingCopy()
    {
        var persistence = new JsonEditorDocumentPersistence(new WorkflowEditorJsonSerializer());
        var project = EditorTestProjectFactory.Create();
        var session = new EditorSession(project)
        {
            SavedProjectJson = persistence.Serialize(project)
        };
        var store = new RecordingDraftStore();
        using var autosave = new LocalDraftAutosave(
            store,
            persistence,
            session,
            new InertTimerFactory());
        autosave.Start(persistence.Serialize(project));
        project.Name = "Final edit";

        await autosave.FlushAsync();

        Assert.Equal("Final edit", store.LastWorkflow?["name"]?.GetValue<string>());
        Assert.True(store.LastIsDirty);
    }

    [Fact]
    public void CorruptDraft_DoesNotReplaceTheCurrentProjectWithAnEmptyOne()
    {
        var persistence = new JsonEditorDocumentPersistence(new WorkflowEditorJsonSerializer());
        var project = EditorTestProjectFactory.Create();
        project.Name = "Keep this project";
        var session = new EditorSession(project);
        using var autosave = new LocalDraftAutosave(
            new FailingDraftStore(),
            persistence,
            session,
            new InertTimerFactory());

        var status = autosave.LoadMostRecent();

        Assert.True(autosave.HasLoadFailure);
        Assert.Contains("quarantined", status, StringComparison.OrdinalIgnoreCase);
        Assert.Same(project, session.Project);
        Assert.Equal("Keep this project", session.Project.Name);
    }

    private sealed class SnapshotDraftStore(LocalDraftSnapshot snapshot) : ILocalDraftStore
    {
        public LocalDraftSnapshot? Load(string workflowId) => snapshot;

        public LocalDraftSnapshot? LoadMostRecent() => snapshot;

        public Task SaveAsync(
            string workflowId,
            JsonNode workflow,
            JsonNode savedWorkflow,
            bool isDirty,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class RecordingDraftStore : ILocalDraftStore
    {
        public string? LastWorkflowId { get; private set; }
        public JsonNode? LastWorkflow { get; private set; }
        public bool LastIsDirty { get; private set; }

        public LocalDraftSnapshot? Load(string workflowId) => null;

        public LocalDraftSnapshot? LoadMostRecent() => null;

        public Task SaveAsync(
            string workflowId,
            JsonNode workflow,
            JsonNode savedWorkflow,
            bool isDirty,
            CancellationToken cancellationToken = default)
        {
            LastWorkflowId = workflowId;
            LastWorkflow = workflow.DeepClone();
            LastIsDirty = isDirty;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingDraftStore : ILocalDraftStore
    {
        public LocalDraftSnapshot? Load(string workflowId)
            => throw new InvalidDataException("The draft was quarantined.");

        public LocalDraftSnapshot? LoadMostRecent()
            => throw new InvalidDataException("The draft was quarantined.");

        public Task SaveAsync(
            string workflowId,
            JsonNode workflow,
            JsonNode savedWorkflow,
            bool isDirty,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class InertTimerFactory : IUiTimerFactory
    {
        public IUiTimer Create(TimeSpan interval) => new InertTimer();
    }

    private sealed class InertTimer : IUiTimer
    {
        public event EventHandler? Tick { add { } remove { } }
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }
}
