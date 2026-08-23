using System.IO.MemoryMappedFiles;
using WorkflowRuntime.ResourceSdk;

namespace WorkflowRuntime.OpenCvSamplePlugin.Runtime;

internal sealed class SharedPreviewStream : IDisposable
{
    public const int HeaderSize = 64;
    public const int Magic = 0x47564953; // GVIS
    public const int Version = 1;

    private readonly object _syncRoot = new();
    private FileStream _fileStream;
    private MemoryMappedFile _mappedFile;
    private MemoryMappedViewAccessor _accessor;
    private int _activeSlot;
    private long _sequence;

    public SharedPreviewStream(string filePath, int slotCapacity)
    {
        FilePath = filePath;
        SlotCapacity = Math.Max(4096, slotCapacity);
        MappingCapacity = HeaderSize + (2L * SlotCapacity);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        _fileStream = OpenFile(filePath, MappingCapacity);
        _mappedFile = MemoryMappedFile.CreateFromFile(
            _fileStream,
            mapName: null,
            capacity: MappingCapacity,
            MemoryMappedFileAccess.ReadWrite,
            HandleInheritability.None,
            leaveOpen: true);
        _accessor = _mappedFile.CreateViewAccessor(0, MappingCapacity, MemoryMappedFileAccess.ReadWrite);
        WriteStaticHeader();
    }

    public string FilePath { get; }

    public int SlotCapacity { get; }

    public long MappingCapacity { get; }

    public DateTimeOffset LastAccessUtc { get; private set; } = DateTimeOffset.UtcNow;

    public WorkflowResourcePreviewFrame Write(
        ReadOnlySpan<byte> pixels,
        Guid runId,
        string imageHandle,
        string streamKey,
        int width,
        int height,
        int stride,
        string pixelFormat,
        string? methodName,
        int? lineNumber,
        string? actionType,
        Guid? actionExecutionId)
    {
        if (pixels.Length > SlotCapacity)
        {
            throw new InvalidOperationException(
                $"Preview requires {pixels.Length} bytes, but the shared-memory slot capacity is {SlotCapacity} bytes.");
        }

        lock (_syncRoot)
        {
            var nextSlot = 1 - _activeSlot;
            var offset = HeaderSize + (nextSlot * (long)SlotCapacity);
            var buffer = pixels.ToArray();
            _accessor.WriteArray(offset, buffer, 0, buffer.Length);

            _accessor.Write(8, width);
            _accessor.Write(12, height);
            _accessor.Write(16, stride);
            _accessor.Write(20, buffer.Length);
            _accessor.Write(28, ToPixelFormatCode(pixelFormat));

            var nextSequence = ++_sequence;
            _accessor.Write(24, nextSlot);
            _accessor.Write(32, nextSequence);
            _accessor.Flush();

            _activeSlot = nextSlot;
            LastAccessUtc = DateTimeOffset.UtcNow;
            return new WorkflowResourcePreviewFrame
            {
                RunId = runId,
                ResourceHandle = imageHandle,
                StreamKey = streamKey,
                MappingFilePath = FilePath,
                MappingCapacity = MappingCapacity,
                HeaderSize = HeaderSize,
                SlotCapacity = SlotCapacity,
                ActiveSlot = nextSlot,
                Sequence = nextSequence,
                Width = width,
                Height = height,
                Stride = stride,
                DataLength = buffer.Length,
                PixelFormat = pixelFormat,
                MethodName = methodName,
                LineNumber = lineNumber,
                ActionType = actionType,
                ActionExecutionId = actionExecutionId
            };
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            _accessor.Dispose();
            _mappedFile.Dispose();
            _fileStream.Dispose();
            try
            {
                File.Delete(FilePath);
            }
            catch
            {
                // A WPF preview reader may still have the mapping open. Cleanup can retry later.
            }
        }
    }

    private void WriteStaticHeader()
    {
        _accessor.Write(0, Magic);
        _accessor.Write(4, Version);
        _accessor.Write(24, 0);
        _accessor.Write(32, 0L);
        _accessor.Flush();
    }

    private static FileStream OpenFile(string filePath, long capacity)
    {
        var stream = new FileStream(
            filePath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete);
        stream.SetLength(capacity);
        return stream;
    }

    private static int ToPixelFormatCode(string pixelFormat)
        => string.Equals(pixelFormat, "Gray8", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
}
