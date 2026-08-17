using System.IO;
using System.IO.MemoryMappedFiles;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WorkflowRuntime.VisionSdk;

namespace WorkflowCore.WpfDemo.Services.Vision;

public sealed class VisionPreviewReader
{
    private WriteableBitmap? _bitmap;
    private byte[] _buffer = Array.Empty<byte>();

    public WriteableBitmap ReadLatest(VisionPreviewFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.EncodedImage is { Length: > 0 })
        {
            return ReadEncoded(frame.EncodedImage);
        }

        if (!File.Exists(frame.MappingFilePath))
        {
            throw new FileNotFoundException("Vision preview mapping file was not found.", frame.MappingFilePath);
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var file = new FileStream(
                frame.MappingFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var mapping = MemoryMappedFile.CreateFromFile(
                file,
                mapName: null,
                capacity: frame.MappingCapacity,
                MemoryMappedFileAccess.Read,
                HandleInheritability.None,
                leaveOpen: false);
            using var accessor = mapping.CreateViewAccessor(0, frame.MappingCapacity, MemoryMappedFileAccess.Read);

            var magic = accessor.ReadInt32(0);
            var version = accessor.ReadInt32(4);
            if (magic != 0x47564953 || version != 1)
            {
                throw new InvalidDataException("The vision preview mapping header is not supported.");
            }

            var sequenceBefore = accessor.ReadInt64(32);
            var width = accessor.ReadInt32(8);
            var height = accessor.ReadInt32(12);
            var stride = accessor.ReadInt32(16);
            var dataLength = accessor.ReadInt32(20);
            var activeSlot = accessor.ReadInt32(24);
            var pixelFormatCode = accessor.ReadInt32(28);
            if (width <= 0 || height <= 0 || stride <= 0 || dataLength <= 0
                || activeSlot is < 0 or > 1 || dataLength > frame.SlotCapacity)
            {
                throw new InvalidDataException("The vision preview mapping contains invalid frame metadata.");
            }

            EnsureBuffer(dataLength);
            var offset = frame.HeaderSize + (activeSlot * (long)frame.SlotCapacity);
            accessor.ReadArray(offset, _buffer, 0, dataLength);
            var sequenceAfter = accessor.ReadInt64(32);
            if (sequenceBefore != sequenceAfter)
            {
                continue;
            }

            var pixelFormat = pixelFormatCode == 1 ? PixelFormats.Gray8 : PixelFormats.Bgra32;
            EnsureBitmap(width, height, pixelFormat);
            _bitmap!.WritePixels(new Int32Rect(0, 0, width, height), _buffer, stride, 0);
            return _bitmap;
        }

        throw new IOException("The image producer changed the preview frame while it was being read.");
    }

    public WriteableBitmap ReadEncoded(byte[] encodedImage)
    {
        ArgumentNullException.ThrowIfNull(encodedImage);
        if (encodedImage.Length == 0)
        {
            throw new ArgumentException("The encoded preview image is empty.", nameof(encodedImage));
        }

        using var stream = new MemoryStream(encodedImage, writable: false);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        return new WriteableBitmap(decoder.Frames[0]);
    }

    private void EnsureBuffer(int length)
    {
        if (_buffer.Length < length)
        {
            _buffer = new byte[length];
        }
    }

    private void EnsureBitmap(int width, int height, PixelFormat pixelFormat)
    {
        if (_bitmap != null
            && _bitmap.PixelWidth == width
            && _bitmap.PixelHeight == height
            && _bitmap.Format == pixelFormat)
        {
            return;
        }

        _bitmap = new WriteableBitmap(width, height, 96, 96, pixelFormat, null);
    }
}
