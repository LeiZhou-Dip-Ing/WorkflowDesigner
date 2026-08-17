using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Services;
using WorkflowCore.WpfDemo.Services.Runtime;
using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class MethodDeploymentTrackerTests
{
    [Fact]
    public void Refresh_ReportsRenameWhenUidExistsWithAnOlderRuntimeName()
    {
        var runtimeMethod = new WorkflowMethod { Name = "Old name" };
        var localMethod = new WorkflowMethod { Uid = runtimeMethod.Uid, Name = "New name" };
        var (tracker, session, persistence) = CreateTracker(localMethod);
        session.RuntimeProjectJson = persistence.Serialize(
            new WorkflowProject { Methods = [runtimeMethod] });

        tracker.Refresh(session.Project);

        var notice = tracker.Get(localMethod);
        Assert.Equal(MethodDeploymentNoticeKind.Renamed, notice.Kind);
        Assert.Equal("Old name", notice.RuntimeName);
    }

    [Fact]
    public void Refresh_ReportsNewWhenNeitherUidNorNameExistsInRuntime()
    {
        var localMethod = new WorkflowMethod { Name = "New method" };
        var (tracker, session, persistence) = CreateTracker(localMethod);
        session.RuntimeProjectJson = persistence.Serialize(
            new WorkflowProject { Methods = [new WorkflowMethod { Name = "Existing" }] });

        tracker.Refresh(session.Project);

        Assert.Equal(MethodDeploymentNoticeKind.New, tracker.Get(localMethod).Kind);
    }

    [Fact]
    public void Refresh_ReportsNewWhenOnlyTheNameMatches()
    {
        var localMethod = new WorkflowMethod { Name = "Main" };
        var (tracker, session, persistence) = CreateTracker(localMethod);
        session.RuntimeProjectJson = persistence.Serialize(
            new WorkflowProject { Methods = [new WorkflowMethod { Name = "Main" }] });

        tracker.Refresh(session.Project);

        Assert.Equal(MethodDeploymentNoticeKind.New, tracker.Get(localMethod).Kind);
    }

    private static (MethodDeploymentTracker Tracker, EditorSession Session, JsonEditorDocumentPersistence Persistence)
        CreateTracker(WorkflowMethod localMethod)
    {
        var persistence = new JsonEditorDocumentPersistence(new WorkflowEditorJsonSerializer());
        var project = new WorkflowProject { Methods = [localMethod] };
        var session = new EditorSession(project)
        {
            SavedProjectJson = persistence.Serialize(project)
        };
        return (new MethodDeploymentTracker(persistence, session), session, persistence);
    }
}
