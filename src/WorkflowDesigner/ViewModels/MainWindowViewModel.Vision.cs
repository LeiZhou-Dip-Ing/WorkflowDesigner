using System.Text.Json;
using System.Windows.Media;
using WorkflowCore.WpfDemo.Services.Vision;
using WorkflowRuntime.Contracts;
using WorkflowRuntime.ResourceSdk;

namespace WorkflowCore.WpfDemo.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly Dictionary<string, ImageSource> _visionPreviews = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, VisionPreviewReader> _visionPreviewReaders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _visionPreviewInfos = new(StringComparer.OrdinalIgnoreCase);
    private Guid? _visionPreviewRunId;
    private readonly HashSet<string> _visionPreviewFetches = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _visionPreviewFetchLock = new();
    private ImageSource? _selectedVisionPreviewImage;
    private string _selectedVisionPreviewInfo = "Run a Vision Action with 'Show image preview' enabled.";

    public ImageSource? SelectedVisionPreviewImage
    {
        get => _selectedVisionPreviewImage;
        private set
        {
            if (SetProperty(ref _selectedVisionPreviewImage, value))
            {
                OnPropertyChanged(nameof(HasSelectedVisionPreview));
            }
        }
    }

    public bool HasSelectedVisionPreview => SelectedVisionPreviewImage != null;

    public string SelectedVisionPreviewInfo
    {
        get => _selectedVisionPreviewInfo;
        private set => SetProperty(ref _selectedVisionPreviewInfo, value);
    }

    private void ApplyVisionPreviewEvent(WorkflowRuntimeEventDto runtimeEvent)
    {
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
        if (frame == null)
        {
            return;
        }

        if (_visionPreviewRunId != frame.RunId)
        {
            _visionPreviewRunId = frame.RunId;
            _visionPreviews.Clear();
            _visionPreviewReaders.Clear();
            _visionPreviewInfos.Clear();
        }

        try
        {
            var key = GetVisionPreviewKey(frame.MethodName, frame.LineNumber);
            if (!_visionPreviewReaders.TryGetValue(key, out var reader))
            {
                reader = new VisionPreviewReader();
                _visionPreviewReaders[key] = reader;
            }

            var image = reader.ReadLatest(frame);
            _visionPreviews[key] = image;
            _visionPreviewInfos[key] = $"{frame.Width} × {frame.Height}  |  {frame.PixelFormat}  |  frame {frame.Sequence}";
            RefreshSelectedVisionPreview();
        }
        catch (Exception exception)
        {
            var key = GetVisionPreviewKey(frame.MethodName, frame.LineNumber);
            _visionPreviewInfos[key] =
                $"Shared preview unavailable ({exception.Message}). Loading the Runtime PNG...";
            RefreshSelectedVisionPreview();
            if (!string.IsNullOrWhiteSpace(frame.MethodName) && frame.LineNumber.HasValue)
            {
                _ = FetchVisionPreviewFromRuntimeAsync(
                    frame.RunId,
                    frame.MethodName,
                    frame.LineNumber.Value,
                    key);
            }
        }
    }

    private void RefreshSelectedVisionPreview()
    {
        var methodName = SelectedMethod?.Name;
        var lineNumber = GetSelectedPreviewLineNumber();
        var key = GetVisionPreviewKey(methodName, lineNumber);
        SelectedVisionPreviewImage = _visionPreviews.TryGetValue(key, out var image) ? image : null;
        SelectedVisionPreviewInfo = _visionPreviewInfos.TryGetValue(key, out var info)
            ? info
            : "Run the selected Vision Action to display its latest output.";

        if (SelectedVisionPreviewImage == null
            && !string.IsNullOrWhiteSpace(methodName)
            && lineNumber.HasValue
            && lineNumber.Value > 0)
        {
            var runId = _session.CurrentRunId ?? _runSession.LastRunId;
            if (runId.HasValue)
            {
                _ = FetchVisionPreviewFromRuntimeAsync(runId.Value, methodName, lineNumber.Value, key);
            }
            else
            {
                _ = FetchLatestVisionPreviewFromRuntimeAsync(methodName, lineNumber.Value, key);
            }
        }
    }

    private async Task FetchVisionPreviewFromRuntimeAsync(
        Guid runId,
        string methodName,
        int lineNumber,
        string key)
    {
        var requestKey = $"{runId:N}:{key}";
        lock (_visionPreviewFetchLock)
        {
            if (!_visionPreviewFetches.Add(requestKey))
            {
                return;
            }
        }

        try
        {
            var encodedImage = await _runtimeApi
                .GetVisionPreviewAsync(runId, methodName, lineNumber)
                .ConfigureAwait(false);
            if (encodedImage is not { Length: > 0 })
            {
                await FetchLatestVisionPreviewFromRuntimeAsync(methodName, lineNumber, key)
                    .ConfigureAwait(false);
                return;
            }

            var reader = new VisionPreviewReader();
            var image = reader.ReadEncoded(encodedImage);
            image.Freeze();

            _uiDispatcher.Post(() =>
            {
                if (_visionPreviewRunId != runId)
                {
                    _visionPreviewRunId = runId;
                    _visionPreviews.Clear();
                    _visionPreviewReaders.Clear();
                    _visionPreviewInfos.Clear();
                }

                _visionPreviews[key] = image;
                _visionPreviewInfos[key] = $"{image.PixelWidth} × {image.PixelHeight}  |  Runtime PNG";

                if (string.Equals(
                        key,
                        GetVisionPreviewKey(SelectedMethod?.Name, GetSelectedPreviewLineNumber()),
                        StringComparison.OrdinalIgnoreCase))
                {
                    SelectedVisionPreviewImage = image;
                    SelectedVisionPreviewInfo = _visionPreviewInfos[key];
                }
            });
        }
        catch (Exception exception)
        {
            _uiDispatcher.Post(() =>
            {
                _visionPreviewInfos[key] = $"Preview unavailable: {exception.Message}";
                if (string.Equals(
                        key,
                        GetVisionPreviewKey(SelectedMethod?.Name, GetSelectedPreviewLineNumber()),
                        StringComparison.OrdinalIgnoreCase))
                {
                    SelectedVisionPreviewInfo = _visionPreviewInfos[key];
                }
            });
        }
        finally
        {
            lock (_visionPreviewFetchLock)
            {
                _visionPreviewFetches.Remove(requestKey);
            }
        }
    }

    private async Task FetchLatestVisionPreviewFromRuntimeAsync(
        string methodName,
        int lineNumber,
        string key)
    {
        var requestKey = $"latest:{key}";
        lock (_visionPreviewFetchLock)
        {
            if (!_visionPreviewFetches.Add(requestKey))
            {
                return;
            }
        }

        try
        {
            var encodedImage = await _runtimeApi
                .GetLatestVisionPreviewAsync(methodName, lineNumber)
                .ConfigureAwait(false);
            if (encodedImage is not { Length: > 0 })
            {
                _uiDispatcher.Post(() =>
                {
                    _visionPreviewInfos[key] = $"No Runtime preview found for {methodName} line {lineNumber}.";
                    if (string.Equals(
                            key,
                            GetVisionPreviewKey(SelectedMethod?.Name, GetSelectedPreviewLineNumber()),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        SelectedVisionPreviewInfo = _visionPreviewInfos[key];
                    }
                });
                return;
            }

            var reader = new VisionPreviewReader();
            var image = reader.ReadEncoded(encodedImage);
            image.Freeze();

            _uiDispatcher.Post(() =>
            {
                _visionPreviews[key] = image;
                _visionPreviewInfos[key] = $"{image.PixelWidth} × {image.PixelHeight}  |  latest Runtime PNG";

                if (string.Equals(
                        key,
                        GetVisionPreviewKey(SelectedMethod?.Name, GetSelectedPreviewLineNumber()),
                        StringComparison.OrdinalIgnoreCase))
                {
                    SelectedVisionPreviewImage = image;
                    SelectedVisionPreviewInfo = _visionPreviewInfos[key];
                }
            });
        }
        catch (Exception exception)
        {
            _uiDispatcher.Post(() =>
            {
                _visionPreviewInfos[key] = $"Preview unavailable: {exception.Message}";
                if (string.Equals(
                        key,
                        GetVisionPreviewKey(SelectedMethod?.Name, GetSelectedPreviewLineNumber()),
                        StringComparison.OrdinalIgnoreCase))
                {
                    SelectedVisionPreviewInfo = _visionPreviewInfos[key];
                }
            });
        }
        finally
        {
            lock (_visionPreviewFetchLock)
            {
                _visionPreviewFetches.Remove(requestKey);
            }
        }
    }

    private static string GetVisionPreviewKey(string? methodName, int? lineNumber)
        => $"{methodName ?? string.Empty}:{lineNumber?.ToString() ?? string.Empty}";

    private int? GetSelectedPreviewLineNumber()
        => SelectedMethodLine == null ? null : SelectedMethodLine.SequenceNo + 1;
}
