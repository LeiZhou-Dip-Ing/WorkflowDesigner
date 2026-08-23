using OpenCvSharp;
using WorkflowRuntime.ActionSdk;
using WorkflowRuntime.ResourceSdk;

namespace WorkflowRuntime.OpenCvSamplePlugin;

internal static class OpenCvActionSupport
{
    public static IWorkflowResourceActionContext RequireVision(IWorkflowActionContext context)
        => context as IWorkflowResourceActionContext
           ?? throw new InvalidOperationException("The host does not expose the optional Vision Action context.");

    public static Mat RequireImage(IWorkflowResourceActionContext vision, string handle, string fieldName = "Input image")
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new InvalidOperationException($"{fieldName} is required.");
        }

        if (!vision.TryGetResource<Mat>(handle, out var image) || image == null)
        {
            throw new KeyNotFoundException($"Image handle '{handle}' was not found.");
        }

        return image;
    }

    public static Mat ToGrayClone(Mat source)
    {
        if (source.Channels() == 1)
        {
            return source.Clone();
        }

        var gray = new Mat();
        Cv2.CvtColor(
            source,
            gray,
            source.Channels() == 4 ? ColorConversionCodes.BGRA2GRAY : ColorConversionCodes.BGR2GRAY);
        return gray;
    }

    public static Mat ToBgrClone(Mat source)
    {
        if (source.Channels() == 3)
        {
            return source.Clone();
        }

        var bgr = new Mat();
        Cv2.CvtColor(
            source,
            bgr,
            source.Channels() == 4 ? ColorConversionCodes.BGRA2BGR : ColorConversionCodes.GRAY2BGR);
        return bgr;
    }

    public static WorkflowResourceMetadata Metadata(Mat image, string source)
        => new()
        {
            Width = image.Width,
            Height = image.Height,
            Channels = image.Channels(),
            DepthBits = checked((int)image.ElemSize1() * 8),
            PixelFormat = image.Channels() switch
            {
                1 => "Gray",
                3 => "Bgr",
                4 => "Bgra",
                _ => $"Channels{image.Channels()}"
            },
            Source = source
        };
}
