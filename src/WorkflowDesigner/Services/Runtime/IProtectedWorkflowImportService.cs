namespace WorkflowCore.WpfDemo.Services.Runtime;

public interface IProtectedWorkflowImportService
{
    Task<ProtectedWorkflowImportResult> ImportAndShowAsync(
        string filePath,
        CancellationToken cancellationToken = default);
}

public sealed record ProtectedWorkflowImportResult(string WorkflowId, long Revision);
