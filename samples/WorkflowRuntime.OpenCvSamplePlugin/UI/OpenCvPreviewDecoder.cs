using System.IO;
using System.Windows.Media.Imaging;
using WorkflowDesigner.WpfSdk;

namespace WorkflowRuntime.OpenCvSamplePlugin.UI;

internal static class OpenCvPreviewDecoder
{
    public static BitmapSource? Decode(WorkflowDesignerResourcePreview? preview)
    {
        if (preview?.Content is not { Length: > 0 })
        {
            return null;
        }

        using var stream = new MemoryStream(preview.Content, writable: false);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }
}
