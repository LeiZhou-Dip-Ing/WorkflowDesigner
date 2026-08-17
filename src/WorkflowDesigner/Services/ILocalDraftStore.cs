using System.Text.Json.Nodes;

namespace WorkflowCore.WpfDemo.Services;

/// <summary>Stores the editor's recoverable local working copy independently of explicit Save.</summary>
public interface ILocalDraftStore
{
    LocalDraftSnapshot? Load(string workflowId);

    LocalDraftSnapshot? LoadMostRecent();

    Task SaveAsync(
        string workflowId,
        JsonNode workflow,
        JsonNode savedWorkflow,
        bool isDirty,
        CancellationToken cancellationToken = default);
}

public sealed class LocalDraftSnapshot
{
    public required string WorkflowId { get; init; }

    public bool IsDirty { get; init; }

    public DateTimeOffset SavedAtUtc { get; init; }

    public required JsonNode Workflow { get; init; }

    public JsonNode? SavedWorkflow { get; init; }
}
