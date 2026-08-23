using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using OpenCvSharp;
using WorkflowRuntime.ResourceSdk;

namespace WorkflowRuntime.OpenCvSamplePlugin.Runtime;

public sealed class OpenCvResourceRuntime : IWorkflowResourceRuntime, IDisposable
{
    private sealed class ImageEntry(Mat image, WorkflowResourceMetadata metadata, Guid runId)
    {
        public Mat Image { get; } = image;
        public WorkflowResourceMetadata Metadata { get; } = metadata;
        public Guid RunId { get; } = runId;
        public DateTimeOffset LastAccessUtc { get; set; } = DateTimeOffset.UtcNow;
        public object SyncRoot { get; } = new();
    }

    private sealed class PreviewEntry(WorkflowResourcePreviewFrame frame)
    {
        public WorkflowResourcePreviewFrame Frame { get; } = frame;
        public DateTimeOffset LastAccessUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    private readonly OpenCvResourceRuntimeOptions _options;
    private readonly ConcurrentDictionary<string, ImageEntry> _images = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SharedPreviewStream> _streams = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PreviewEntry> _latestPreviews = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PreviewEntry> _latestPreviewsByLine = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public OpenCvResourceRuntime(OpenCvResourceRuntimeOptions? options = null)
    {
        _options = options ?? new OpenCvResourceRuntimeOptions();
        _options.PreviewDirectory = Path.GetFullPath(_options.PreviewDirectory);
        Directory.CreateDirectory(_options.PreviewDirectory);
    }

    public bool CanStore(object resource) => resource is Mat;

    public bool CanResolve(string handle)
        => !string.IsNullOrWhiteSpace(handle)
           && handle.StartsWith("resource://opencv/", StringComparison.OrdinalIgnoreCase);

    public string StoreResource(Guid runId, object image, WorkflowResourceMetadata metadata)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(metadata);
        if (image is not Mat mat)
        {
            throw new ArgumentException("OpenCvResourceRuntime accepts OpenCvSharp.Mat image resources.", nameof(image));
        }

        if (mat.Empty())
        {
            throw new ArgumentException("The OpenCV image is empty.", nameof(image));
        }

        var handle = $"resource://opencv/{runId:N}/{Guid.NewGuid():N}";
        var entry = new ImageEntry(mat, metadata, runId);
        if (!_images.TryAdd(handle, entry))
        {
            mat.Dispose();
            throw new InvalidOperationException("Could not allocate a unique image handle.");
        }

        TrimToLimit();
        return handle;
    }

    public bool TryGetResource<TImage>(string handle, out TImage? image)
        where TImage : class
    {
        image = null;
        if (string.IsNullOrWhiteSpace(handle) || !_images.TryGetValue(handle, out var entry))
        {
            return false;
        }

        entry.LastAccessUtc = DateTimeOffset.UtcNow;
        image = entry.Image as TImage;
        return image != null;
    }

    public WorkflowResourceMetadata GetMetadata(string handle)
    {
        if (!_images.TryGetValue(handle, out var entry))
        {
            throw new KeyNotFoundException($"Image handle '{handle}' was not found or has expired.");
        }

        entry.LastAccessUtc = DateTimeOffset.UtcNow;
        return entry.Metadata;
    }

    public WorkflowResourcePreviewFrame CreatePreview(
        string handle,
        Guid runId,
        string streamKey,
        string? methodName,
        int? lineNumber,
        string? actionType,
        Guid? actionExecutionId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_images.TryGetValue(handle, out var entry))
        {
            throw new KeyNotFoundException($"Image handle '{handle}' was not found or has expired.");
        }

        entry.LastAccessUtc = DateTimeOffset.UtcNow;
        lock (entry.SyncRoot)
        {
            using var preview = CreateDisplayMat(entry.Image, out var pixelFormat);
            var stride = checked((int)preview.Step());
            var dataLength = checked(stride * preview.Rows);
            var pixels = new byte[dataLength];
            Marshal.Copy(preview.Data, pixels, 0, dataLength);

            var stream = GetOrCreateStream(streamKey, dataLength);
            var frame = stream.Write(
                pixels,
                runId,
                handle,
                streamKey,
                preview.Width,
                preview.Height,
                stride,
                pixelFormat,
                methodName,
                lineNumber,
                actionType,
                actionExecutionId);

            // Persist one compressed preview per run/method/line on the Runtime side.
            // SignalR remains a lightweight notification channel; the Designer fetches the
            // actual PNG over REST after the run or whenever the user selects a workflow line.
            Cv2.ImEncode(".png", preview, out var encoded);
            var completedFrame = frame with
            {
                EncodedContent = encoded,
                EncodedContentType = "png"
            };

            if (!string.IsNullOrWhiteSpace(methodName) && lineNumber.HasValue)
            {
                var previewEntry = new PreviewEntry(completedFrame);
                _latestPreviews[CreatePreviewLookupKey(runId, methodName, lineNumber.Value)] = previewEntry;
                _latestPreviewsByLine[CreateLatestPreviewLookupKey(methodName, lineNumber.Value)] = previewEntry;
            }

            // The preview is intentionally included in the event as a transport-safe PNG.
            // This makes the Designer independent of Windows session/shared-memory boundaries.
            // The Runtime REST cache remains available as a recovery path after the run.
            return completedFrame;
        }
    }

    public bool TryGetLatestPreview(
        Guid runId,
        string methodName,
        int lineNumber,
        out WorkflowResourcePreviewFrame? frame)
    {
        frame = null;
        if (string.IsNullOrWhiteSpace(methodName) || lineNumber <= 0)
        {
            return false;
        }

        var key = CreatePreviewLookupKey(runId, methodName, lineNumber);
        if (!_latestPreviews.TryGetValue(key, out var entry))
        {
            return false;
        }

        entry.LastAccessUtc = DateTimeOffset.UtcNow;
        frame = entry.Frame;
        return frame.EncodedContent is { Length: > 0 };
    }

    public bool TryGetLatestPreview(
        string methodName,
        int lineNumber,
        out WorkflowResourcePreviewFrame? frame)
    {
        frame = null;
        if (string.IsNullOrWhiteSpace(methodName) || lineNumber <= 0)
        {
            return false;
        }

        var key = CreateLatestPreviewLookupKey(methodName, lineNumber);
        if (!_latestPreviewsByLine.TryGetValue(key, out var entry))
        {
            return false;
        }

        entry.LastAccessUtc = DateTimeOffset.UtcNow;
        frame = entry.Frame;
        return frame.EncodedContent is { Length: > 0 };
    }

    public int CleanupExpired()
    {
        var removed = 0;
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-Math.Max(1, _options.ResourceRetentionMinutes));
        foreach (var pair in _images)
        {
            if (pair.Value.LastAccessUtc >= cutoff || !_images.TryRemove(pair.Key, out var removedEntry))
            {
                continue;
            }

            removedEntry.Image.Dispose();
            removed++;
        }

        foreach (var pair in _streams)
        {
            if (pair.Value.LastAccessUtc >= cutoff || !_streams.TryRemove(pair.Key, out var removedStream))
            {
                continue;
            }

            removedStream.Dispose();
        }

        foreach (var pair in _latestPreviews)
        {
            if (pair.Value.LastAccessUtc < cutoff)
            {
                _latestPreviews.TryRemove(pair.Key, out _);
            }
        }

        foreach (var pair in _latestPreviewsByLine)
        {
            if (pair.Value.LastAccessUtc < cutoff)
            {
                _latestPreviewsByLine.TryRemove(pair.Key, out _);
            }
        }

        return removed;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var pair in _images)
        {
            pair.Value.Image.Dispose();
        }

        foreach (var pair in _streams)
        {
            pair.Value.Dispose();
        }

        _images.Clear();
        _streams.Clear();
        _latestPreviews.Clear();
        _latestPreviewsByLine.Clear();
    }

    private static string CreatePreviewLookupKey(Guid runId, string methodName, int lineNumber)
        => $"{runId:N}:{methodName.Trim()}:{lineNumber}";

    private static string CreateLatestPreviewLookupKey(string methodName, int lineNumber)
        => $"{methodName.Trim()}:{lineNumber}";

    private Mat CreateDisplayMat(Mat source, out string pixelFormat)
    {
        Mat working;
        if (source.Depth() != 0)
        {
            working = new Mat();
            Cv2.Normalize(source, working, 0, 255, NormTypes.MinMax, 0);
        }
        else
        {
            working = source.Clone();
        }

        Mat display;
        if (working.Channels() == 1)
        {
            display = working;
            pixelFormat = "Gray8";
        }
        else
        {
            display = new Mat();
            if (working.Channels() == 3)
            {
                Cv2.CvtColor(working, display, ColorConversionCodes.BGR2BGRA);
            }
            else if (working.Channels() == 4)
            {
                working.CopyTo(display);
            }
            else
            {
                working.Dispose();
                throw new NotSupportedException($"Preview does not support {source.Channels()} image channels.");
            }

            working.Dispose();
            pixelFormat = "Bgra32";
        }

        var maxWidth = Math.Max(1, _options.PreviewMaxWidth);
        var maxHeight = Math.Max(1, _options.PreviewMaxHeight);
        var scale = Math.Min(1d, Math.Min(maxWidth / (double)display.Width, maxHeight / (double)display.Height));
        if (scale >= 0.999d)
        {
            return display;
        }

        var resized = new Mat();
        Cv2.Resize(display, resized, new global::OpenCvSharp.Size(0, 0), scale, scale, InterpolationFlags.Area);
        display.Dispose();
        return resized;
    }

    private SharedPreviewStream GetOrCreateStream(string streamKey, int requiredCapacity)
    {
        while (true)
        {
            if (_streams.TryGetValue(streamKey, out var current) && current.SlotCapacity >= requiredCapacity)
            {
                return current;
            }

            var capacity = RoundUp(requiredCapacity, 1024 * 1024);
            var path = Path.Combine(_options.PreviewDirectory, $"preview-{Hash(streamKey)}.gvis");
            var replacement = new SharedPreviewStream(path, capacity);
            if (current == null)
            {
                if (_streams.TryAdd(streamKey, replacement))
                {
                    return replacement;
                }
            }
            else if (_streams.TryUpdate(streamKey, replacement, current))
            {
                current.Dispose();
                return replacement;
            }

            replacement.Dispose();
        }
    }

    private void TrimToLimit()
    {
        var limit = Math.Max(8, _options.MaximumRetainedImages);
        var excess = _images.Count - limit;
        if (excess <= 0)
        {
            return;
        }

        foreach (var pair in _images.OrderBy(item => item.Value.LastAccessUtc).Take(excess))
        {
            if (_images.TryRemove(pair.Key, out var removed))
            {
                removed.Image.Dispose();
            }
        }
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..24];

    private static int RoundUp(int value, int block)
        => checked(((value + block - 1) / block) * block);
}
