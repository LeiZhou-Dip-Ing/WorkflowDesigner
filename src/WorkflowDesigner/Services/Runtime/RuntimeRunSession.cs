using System.Diagnostics;
using System.Text.Json.Nodes;
using WorkflowCore.WpfDemo.Editor;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.Services.Runtime;

/// <summary>Owns one remote run at a time, including start, status polling, cancellation, and UI state.</summary>
public sealed class RuntimeRunSession : IDisposable
{
    private readonly IRuntimeApiClient _runtimeApi;
    private readonly IEditorDocumentPersistence _persistence;
    private readonly EditorSession _session;
    private CancellationTokenSource? _runCancellation;
    private bool _disposed;

    public RuntimeRunSession(
        IRuntimeApiClient runtimeApi,
        IEditorDocumentPersistence persistence,
        EditorSession session)
    {
        _runtimeApi = runtimeApi ?? throw new ArgumentNullException(nameof(runtimeApi));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public event EventHandler? StateChanged;

    public bool IsRunning => _runCancellation != null;

    public bool IsStepRun { get; private set; }

    /// <summary>Most recently started run. Retained after completion so late SignalR events can still be accepted.</summary>
    public Guid? LastRunId { get; private set; }

    /// <summary>Runs the method from the active workflow revision already published to Runtime.</summary>
    public Task<RuntimeRunResult> RunPublishedAsync(
        WorkflowMethod method,
        IReadOnlyDictionary<string, JsonNode?>? inputs = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(method);
        return RunAsync(
            token => _runtimeApi.StartPublishedRunAsync(
                WorkflowRuntimeDefaults.DefaultWorkflowId,
                method.Uid,
                method.Name,
                inputs ?? new Dictionary<string, JsonNode?>(),
                token),
            cancellationToken);
    }

    /// <summary>Tests the current editor Project on Runtime without publishing it.</summary>
    public Task<RuntimeRunResult> RunPreviewAsync(
        WorkflowProject project,
        WorkflowMethod method,
        IReadOnlyDictionary<string, JsonNode?>? inputs = null,
        bool stepMode = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        return RunPreviewAsync(
            _persistence.Serialize(project),
            method,
            inputs,
            stepMode,
            cancellationToken);
    }

    /// <summary>Tests an already serialized editor snapshot without walking the Project graph again.</summary>
    public Task<RuntimeRunResult> RunPreviewAsync(
        string workflowJson,
        WorkflowMethod method,
        IReadOnlyDictionary<string, JsonNode?>? inputs = null,
        bool stepMode = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowJson);
        ArgumentNullException.ThrowIfNull(method);
        return RunAsync(
            token =>
            {
                var workflow = JsonNode.Parse(workflowJson)
                    ?? throw new InvalidOperationException("The editor produced an empty workflow document.");
                return _runtimeApi.StartPreviewRunAsync(
                    workflow,
                    method.Uid,
                    method.Name,
                    inputs ?? new Dictionary<string, JsonNode?>(),
                    stepMode ? "Step" : "Run",
                    token);
            },
            cancellationToken,
            stepMode);
    }

    private async Task<RuntimeRunResult> RunAsync(
        Func<CancellationToken, Task<Guid>> startRun,
        CancellationToken cancellationToken,
        bool stepMode = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsRunning)
        {
            return new RuntimeRunResult(false, "A workflow run is already active.");
        }

        _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IsStepRun = stepMode;
        StateChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await _runtimeApi.ConnectEventsAsync(_runCancellation.Token).ConfigureAwait(false);
            var runId = await startRun(_runCancellation.Token).ConfigureAwait(false);
            LastRunId = runId;
            _session.CurrentRunId = runId;
            StateChanged?.Invoke(this, EventArgs.Empty);

            WorkflowRunStatusResponse status;
            do
            {
                await Task.Delay(200, _runCancellation.Token).ConfigureAwait(false);
                status = await _runtimeApi.GetRunStatusAsync(
                        _session.CurrentRunId.Value,
                        _runCancellation.Token)
                    .ConfigureAwait(false);
            }
            while (!status.IsTerminal);

            return new RuntimeRunResult(IsSuccessful(status), FormatResult(status));
        }
        catch (OperationCanceledException)
        {
            return new RuntimeRunResult(false, "Cancelled.");
        }
        catch (Exception exception)
        {
            return new RuntimeRunResult(false, $"Error: {exception.Message}");
        }
        finally
        {
            _runCancellation.Dispose();
            _runCancellation = null;
            IsStepRun = false;
            _session.CurrentRunId = null;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Task StepAsync(CancellationToken cancellationToken = default)
        => SendControlAsync(_runtimeApi.StepRunAsync, cancellationToken);

    public Task ContinueAsync(CancellationToken cancellationToken = default)
        => SendControlAsync(_runtimeApi.ContinueRunAsync, cancellationToken);

    public Task PauseAsync(CancellationToken cancellationToken = default)
        => SendControlAsync(_runtimeApi.PauseRunAsync, cancellationToken);

    private Task SendControlAsync(
        Func<Guid, CancellationToken, Task> control,
        CancellationToken cancellationToken)
        => _session.CurrentRunId.HasValue
            ? control(_session.CurrentRunId.Value, cancellationToken)
            : Task.CompletedTask;

    public async Task CancelAsync(CancellationToken cancellationToken = default)
    {
        if (_session.CurrentRunId.HasValue)
        {
            try
            {
                await _runtimeApi.CancelRunAsync(_session.CurrentRunId.Value, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
            }
        }

        _runCancellation?.Cancel();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _runCancellation?.Cancel();
        _runCancellation?.Dispose();
        _runCancellation = null;
    }

    private static string FormatResult(WorkflowRunStatusResponse result)
    {
        if (result.State == "Completed" && result.ResultValue == true)
        {
            return "Finished successfully.";
        }

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            return $"Finished with {result.ResultType ?? result.State}: {result.Error}";
        }

        return $"Finished with {result.ResultType ?? result.State}.";
    }

    private static bool IsSuccessful(WorkflowRunStatusResponse result)
        => string.Equals(result.State, "Completed", StringComparison.OrdinalIgnoreCase)
           && result.ResultValue == true;
}

public sealed record RuntimeRunResult(bool Succeeded, string Message);
