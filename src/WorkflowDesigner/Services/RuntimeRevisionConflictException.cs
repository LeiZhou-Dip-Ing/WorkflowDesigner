namespace WorkflowCore.WpfDemo.Services;

using System.Net;

/// <summary>Signals that the runtime revision changed after the editor last downloaded it.</summary>
public sealed class RuntimeRevisionConflictException : RuntimeApiException
{
    public RuntimeRevisionConflictException(
        string workflowId,
        long expectedRevision,
        long currentRevision,
        string currentContentHash,
        string? message = null,
        string responseBody = "")
        : base(HttpStatusCode.Conflict, responseBody, string.IsNullOrWhiteSpace(message)
            ? $"Workflow '{workflowId}' was changed in Runtime revision {currentRevision}."
            : message)
    {
        WorkflowId = workflowId;
        ExpectedRevision = expectedRevision;
        CurrentRevision = currentRevision;
        CurrentContentHash = currentContentHash ?? string.Empty;
    }

    public string WorkflowId { get; }
    public long ExpectedRevision { get; }
    public long CurrentRevision { get; }
    public string CurrentContentHash { get; }
}
