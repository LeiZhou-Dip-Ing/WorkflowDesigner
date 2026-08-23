using System.Text.Json;
using System.Windows.Media;
using WorkflowCore.WpfDemo.Services.Resources;
using WorkflowRuntime.Contracts;
using WorkflowRuntime.ResourceSdk;

namespace WorkflowCore.WpfDemo.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly Dictionary<string, ImageSource> _resourcePreviewImages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _resourcePreviewInfos = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _resourcePreviewFetches = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _resourcePreviewFetchLock = new();
    private Guid? _resourcePreviewRunId;
    private ImageSource? _selectedResourcePreviewImage;
    private string _selectedResourcePreviewInfo = "Run an image Action with preview enabled.";

    public ImageSource? SelectedResourcePreviewImage
    {
        get => _selectedResourcePreviewImage;
        private set
        {
            if (SetProperty(ref _selectedResourcePreviewImage, value))
            {
                OnPropertyChanged(nameof(HasSelectedResourcePreview));
            }
        }
    }

    public bool HasSelectedResourcePreview => SelectedResourcePreviewImage != null;

    public string SelectedResourcePreviewInfo
    {
        get => _selectedResourcePreviewInfo;
        private set => SetProperty(ref _selectedResourcePreviewInfo, value);
    }

    private void ApplyResourcePreviewEvent(WorkflowRuntimeEventDto runtimeEvent)
    {
        // VisionPreview is accepted only as a legacy wire-event alias.
        if ((!string.Equals(runtimeEvent.EventType, "ResourcePreview", StringComparison.OrdinalIgnoreCase)
             && !string.Equals(runtimeEvent.EventType, "VisionPreview", StringComparison.OrdinalIgnoreCase))
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
            try
            {
                var image = new ResourcePreviewReader().ReadEncoded(frame.Content);
                image.Freeze();
                _resourcePreviewImages[key] = image;
                _resourcePreviewInfos[key] = $"{image.PixelWidth} x {image.PixelHeight} | {frame.ContentType} | frame {frame.Sequence}";
                RefreshSelectedResourcePreview();
                return;
            }
            catch (Exception exception)
            {
                _resourcePreviewInfos[key] = $"Preview content is not supported by the image workspace: {exception.Message}";
            }
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
        SelectedResourcePreviewImage = _resourcePreviewImages.TryGetValue(key, out var image) ? image : null;
        SelectedResourcePreviewInfo = _resourcePreviewInfos.TryGetValue(key, out var info)
            ? info
            : "Run the selected image Action to display its latest preview.";

        if (SelectedResourcePreviewImage != null || string.IsNullOrWhiteSpace(methodName) || lineNumber is null or <= 0) return;
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

            ApplyFetchedImage(runId, key, content, "Runtime resource preview");
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
                _uiDispatcher.Post(() => _resourcePreviewInfos[key] = $"No Runtime preview found for {methodName} line {lineNumber}.");
                return;
            }

            ApplyFetchedImage(null, key, content, "Latest Runtime resource preview");
        }
        catch (Exception exception) { ApplyResourcePreviewError(key, exception); }
        finally { EndResourcePreviewFetch(requestKey); }
    }

    private void ApplyFetchedImage(Guid? runId, string key, byte[] content, string source)
    {
        var image = new ResourcePreviewReader().ReadEncoded(content);
        image.Freeze();
        _uiDispatcher.Post(() =>
        {
            if (runId.HasValue) ResetResourcePreviewsForRun(runId.Value);
            _resourcePreviewImages[key] = image;
            _resourcePreviewInfos[key] = $"{image.PixelWidth} x {image.PixelHeight} | {source}";
            if (string.Equals(key, GetResourcePreviewKey(SelectedMethod?.Name, GetSelectedPreviewLineNumber()), StringComparison.OrdinalIgnoreCase))
            {
                SelectedResourcePreviewImage = image;
                SelectedResourcePreviewInfo = _resourcePreviewInfos[key];
            }
        });
    }

    private void ApplyResourcePreviewError(string key, Exception exception)
        => _uiDispatcher.Post(() =>
        {
            _resourcePreviewInfos[key] = $"Preview unavailable: {exception.Message}";
            if (string.Equals(key, GetResourcePreviewKey(SelectedMethod?.Name, GetSelectedPreviewLineNumber()), StringComparison.OrdinalIgnoreCase))
            {
                SelectedResourcePreviewInfo = _resourcePreviewInfos[key];
            }
        });

    private void ResetResourcePreviewsForRun(Guid runId)
    {
        if (_resourcePreviewRunId == runId) return;
        _resourcePreviewRunId = runId;
        _resourcePreviewImages.Clear();
        _resourcePreviewInfos.Clear();
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
