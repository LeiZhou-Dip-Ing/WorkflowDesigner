using System.IO;
using System.Windows.Media.Imaging;

namespace WorkflowCore.WpfDemo.Services.Resources;

public sealed class ResourcePreviewReader
{
    public WriteableBitmap ReadEncoded(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length == 0)
        {
            throw new ArgumentException("The resource preview is empty.", nameof(content));
        }

        using var stream = new MemoryStream(content, writable: false);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        return new WriteableBitmap(decoder.Frames[0]);
    }
}
