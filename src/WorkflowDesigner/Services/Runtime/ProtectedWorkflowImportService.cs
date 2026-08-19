using System.IO;
using WorkflowCore.WpfDemo.Services.Ui;

namespace WorkflowCore.WpfDemo.Services.Runtime;

public sealed class ProtectedWorkflowImportService : IProtectedWorkflowImportService
{
    private readonly IRuntimeApiClient _runtimeApi;
    private readonly IProtectedWorkflowPresentation _presentation;

    public ProtectedWorkflowImportService(
        IRuntimeApiClient runtimeApi,
        IProtectedWorkflowPresentation presentation)
    {
        _runtimeApi = runtimeApi ?? throw new ArgumentNullException(nameof(runtimeApi));
        _presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
    }

    public async Task<ProtectedWorkflowImportResult> ImportAndShowAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var workflowId = CreateWorkflowId(filePath);
        var active = await _runtimeApi
            .GetActiveProjectIdentityAsync(workflowId, cancellationToken)
            .ConfigureAwait(false);
        var published = await _runtimeApi.ImportProtectedWorkflowAsync(
                workflowId,
                filePath,
                active?.Revision ?? 0,
                cancellationToken)
            .ConfigureAwait(false);
        var model = await _runtimeApi
            .GetWorkflowPresentationAsync(workflowId, cancellationToken)
            .ConfigureAwait(false);
        await _presentation.ShowAsync(model, cancellationToken).ConfigureAwait(false);
        return new ProtectedWorkflowImportResult(workflowId, published.Revision);
    }

    private static string CreateWorkflowId(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        var safe = new string(name.Select(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' ? character : '-').ToArray());
        safe = safe.Trim('-', '.');
        return string.IsNullOrWhiteSpace(safe) ? "protected-workflow" : safe;
    }
}
