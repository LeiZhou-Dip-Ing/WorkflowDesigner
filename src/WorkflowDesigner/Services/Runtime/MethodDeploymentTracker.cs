using System.Diagnostics;
using System.Text.Json;
using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Models;

namespace WorkflowCore.WpfDemo.Services.Runtime;

public enum MethodDeploymentNoticeKind
{
    None,
    Renamed,
    New
}

public sealed record MethodDeploymentNotice(MethodDeploymentNoticeKind Kind, string? RuntimeName)
{
    public static MethodDeploymentNotice None { get; } = new(MethodDeploymentNoticeKind.None, null);
}

/// <summary>Tracks local method identities that still need to be published to Runtime.</summary>
public sealed class MethodDeploymentTracker
{
    private readonly IEditorDocumentPersistence _persistence;
    private readonly EditorSession _session;
    private readonly Dictionary<Guid, MethodDeploymentNotice> _notices = new();

    public MethodDeploymentTracker(IEditorDocumentPersistence persistence, EditorSession session)
    {
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public MethodDeploymentNotice Get(WorkflowMethod? method)
        => method != null && _notices.TryGetValue(method.Uid, out var notice)
            ? notice
            : MethodDeploymentNotice.None;

    public void MarkNew(WorkflowMethod method)
        => _notices[method.Uid] = new MethodDeploymentNotice(MethodDeploymentNoticeKind.New, null);

    public void Remove(WorkflowMethod method) => _notices.Remove(method.Uid);

    public void Refresh(WorkflowProject project, bool allowClearAgainstLocalBaseline = false)
    {
        var comparisonProject = ReadComparisonProject();
        if (comparisonProject == null)
        {
            return;
        }

        var canClear = _session.RuntimeProjectJson != null || allowClearAgainstLocalBaseline;
        foreach (var method in project.Methods)
        {
            Apply(method, Evaluate(method, comparisonProject), canClear);
        }

        var activeMethodIds = project.Methods.Select(method => method.Uid).ToHashSet();
        foreach (var removedMethodId in _notices.Keys.Where(id => !activeMethodIds.Contains(id)).ToList())
        {
            _notices.Remove(removedMethodId);
        }
    }

    public void Update(WorkflowMethod method, bool allowClear)
    {
        var comparisonProject = ReadComparisonProject();
        if (comparisonProject != null)
        {
            Apply(method, Evaluate(method, comparisonProject), allowClear);
        }
    }

    private WorkflowProject? ReadComparisonProject()
    {
        var comparisonJson = !string.IsNullOrWhiteSpace(_session.RuntimeProjectJson)
            ? _session.RuntimeProjectJson
            : _session.SavedProjectJson;
        if (string.IsNullOrWhiteSpace(comparisonJson))
        {
            return null;
        }

        try
        {
            return _persistence.Deserialize(comparisonJson);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            Debug.WriteLine(exception);
            return null;
        }
    }

    private void Apply(WorkflowMethod method, MethodDeploymentNotice notice, bool allowClear)
    {
        if (notice.Kind != MethodDeploymentNoticeKind.None)
        {
            _notices[method.Uid] = notice;
        }
        else if (allowClear)
        {
            _notices.Remove(method.Uid);
        }
    }

    private static MethodDeploymentNotice Evaluate(WorkflowMethod localMethod, WorkflowProject comparisonProject)
    {
        var methodByUid = comparisonProject.Methods.FirstOrDefault(method => method.Uid == localMethod.Uid);
        if (methodByUid != null)
        {
            return string.Equals(methodByUid.Name, localMethod.Name, StringComparison.Ordinal)
                ? MethodDeploymentNotice.None
                : new MethodDeploymentNotice(MethodDeploymentNoticeKind.Renamed, methodByUid.Name);
        }

        return new MethodDeploymentNotice(MethodDeploymentNoticeKind.New, null);
    }
}
