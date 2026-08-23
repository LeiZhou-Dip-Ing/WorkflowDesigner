using System.Text.Json;
using WorkflowDesigner.WpfSdk;
using WorkflowRuntime.Contracts;
using WorkflowRuntime.ResourceSdk;

namespace WorkflowCore.WpfDemo.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly Dictionary<string, WorkflowDesignerResourcePreview> _resourcePreviews = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _resourcePreviewFetches = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _resourcePreviewFetchLock = new();
    private Guid? _resourcePreviewRunId;
    private WorkflowDesignerResourcePreview? _selectedResourcePreview;

    public WorkflowDesignerResourcePreview? SelectedResourcePreview
    {
        get => _selectedResourcePreview;
        private set
        {
            if (SetProperty(ref _selectedResourcePreview, value))
            {
                OnPropertyChanged(nameof(HasSelectedResourcePreview));
            }
        }
    }

    public bool HasSelectedResourcePreview => SelectedResourcePreview != null;

    private void ApplyResourcePreviewEvent(WorkflowRuntimeEventDto runtimeEvent)
    {
        if (!string.Equals(runtimeEvent.EventType, "ResourcePreview", StringComparison.OrdinalIgnoreCase)
            || runtimeEvent.Payload == null)
        {
            return;
        }

        var frame = runtimeEvent.Payload.Deserialize<WorkflowResourcePreviewFrame>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        if (frame == null) return;

        ResetResourcePreviewsForRun(frame.RunId);
        var key = GetResourcePreviewKey(frame.MethodName, frame.LineNumber);
        if (frame.Content is { Length: > 0 })
        {
            _resourcePreviews[key] = new WorkflowDesignerResourcePreview(
                frame.Content,
                frame.ContentType,
                $"{frame.ContentType} | frame {frame.Sequence}",
                frame.Sequence);
            RefreshSelectedResourcePreview();
            return;
        }

        RefreshSelectedResourcePreview();
        if (!string.IsNullOrWhiteSpace(frame.MethodName) && frame.LineNumber.HasValue)
        {
            _ = FetchResourcePreviewFromRuntimeAsync(frame.RunId, frame.MethodName, frame.LineNumber.Value, key);
        }
    }

    private void RefreshSelectedResourcePreview()
    {
        var methodName = SelectedMethod?.Name;
        var lineNumber = GetSelectedPreviewLineNumber();
        var key = GetResourcePreviewKey(methodName, lineNumber);
        SelectedResourcePreview = _resourcePreviews.TryGetValue(key, out var preview) ? preview : null;

        if (SelectedResourcePreview != null || string.IsNullOrWhiteSpace(methodName) || lineNumber is null or <= 0) return;
        var runId = _session.CurrentRunId ?? _runSession.LastRunId;
        _ = runId.HasValue
            ? FetchResourcePreviewFromRuntimeAsync(runId.Value, methodName, lineNumber.Value, key)
            : FetchLatestResourcePreviewFromRuntimeAsync(methodName, lineNumber.Value, key);
    }

    private async Task FetchResourcePreviewFromRuntimeAsync(Guid runId, string methodName, int lineNumber, string key)
    {
        var requestKey = $"{runId:N}:{key}";
        if (!BeginResourcePreviewFetch(requestKey)) return;
        try
        {
            var content = await _runtimeApi.GetResourcePreviewAsync(runId, methodName, lineNumber).ConfigureAwait(false);
            if (content is not { Length: > 0 })
            {
                await FetchLatestResourcePreviewFromRuntimeAsync(methodName, lineNumber, key).ConfigureAwait(false);
                return;
            }

            ApplyFetchedPreview(runId, key, content, "Runtime resource preview");
        }
        catch (Exception exception) { ApplyResourcePreviewError(key, exception); }
        finally { EndResourcePreviewFetch(requestKey); }
    }

    private async Task FetchLatestResourcePreviewFromRuntimeAsync(string methodName, int lineNumber, string key)
    {
        var requestKey = $"latest:{key}";
        if (!BeginResourcePreviewFetch(requestKey)) return;
        try
        {
            var content = await _runtimeApi.GetLatestResourcePreviewAsync(methodName, lineNumber).ConfigureAwait(false);
            if (content is not { Length: > 0 })
            {
                return;
            }

            ApplyFetchedPreview(null, key, content, "Latest Runtime resource preview");
        }
        catch (Exception exception) { ApplyResourcePreviewError(key, exception); }
        finally { EndResourcePreviewFetch(requestKey); }
    }

    private void ApplyFetchedPreview(Guid? runId, string key, byte[] content, string source)
    {
        _uiDispatcher.Post(() =>
        {
            if (runId.HasValue) ResetResourcePreviewsForRun(runId.Value);
            var preview = new WorkflowDesignerResourcePreview(content, "application/octet-stream", source);
            _resourcePreviews[key] = preview;
            if (string.Equals(key, GetResourcePreviewKey(SelectedMethod?.Name, GetSelectedPreviewLineNumber()), StringComparison.OrdinalIgnoreCase))
            {
                SelectedResourcePreview = preview;
            }
        });
    }

    private void ApplyResourcePreviewError(string key, Exception exception)
        => _uiDispatcher.Post(() =>
        {
            System.Diagnostics.Debug.WriteLine($"Preview unavailable for {key}: {exception.Message}");
        });

    private void ResetResourcePreviewsForRun(Guid runId)
    {
        if (_resourcePreviewRunId == runId) return;
        _resourcePreviewRunId = runId;
        _resourcePreviews.Clear();
    }

    private bool BeginResourcePreviewFetch(string key)
    {
        lock (_resourcePreviewFetchLock) return _resourcePreviewFetches.Add(key);
    }

    private void EndResourcePreviewFetch(string key)
    {
        lock (_resourcePreviewFetchLock) _resourcePreviewFetches.Remove(key);
    }

    private static string GetResourcePreviewKey(string? methodName, int? lineNumber)
        => $"{methodName ?? string.Empty}:{lineNumber?.ToString() ?? string.Empty}";

    private int? GetSelectedPreviewLineNumber()
        => SelectedMethodLine == null ? null : SelectedMethodLine.SequenceNo + 1;
}
