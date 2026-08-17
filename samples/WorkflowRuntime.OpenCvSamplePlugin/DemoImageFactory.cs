using OpenCvSharp;

namespace WorkflowRuntime.OpenCvSamplePlugin;

internal static class DemoImageFactory
{
    public static Mat CreateSource()
    {
        var image = new Mat(new Size(960, 540), MatType.CV_8UC3, new Scalar(32, 36, 42));
        Cv2.Rectangle(image, new Rect(55, 55, 850, 430), new Scalar(205, 212, 218), -1);
        Cv2.Rectangle(image, new Rect(95, 100, 295, 210), new Scalar(80, 92, 104), -1);
        Cv2.Rectangle(image, new Rect(135, 140, 215, 130), new Scalar(235, 235, 235), -1);

        // Long high-contrast line for MeasureLine.
        Cv2.Line(image, new Point(120, 410), new Point(820, 350), new Scalar(20, 20, 20), 11, LineTypes.AntiAlias);
        Cv2.Line(image, new Point(120, 425), new Point(820, 365), new Scalar(245, 245, 245), 3, LineTypes.AntiAlias);

        // Dominant circle for MeasureCircle.
        Cv2.Circle(image, new Point(690, 215), 92, new Scalar(160, 166, 172), -1, LineTypes.AntiAlias);
        Cv2.Circle(image, new Point(690, 215), 92, new Scalar(20, 20, 20), 7, LineTypes.AntiAlias);
        Cv2.Circle(image, new Point(690, 215), 30, new Scalar(65, 70, 75), -1, LineTypes.AntiAlias);

        using var template = CreateTemplate();
        using var roi = new Mat(image, new Rect(160, 145, template.Width, template.Height));
        template.CopyTo(roi);

        foreach (var point in new[] { new Point(470,145), new Point(525,145), new Point(470,235), new Point(525,235), new Point(455,305), new Point(540,305) })
        {
            Cv2.Circle(image, point, 10, new Scalar(45, 50, 55), -1, LineTypes.AntiAlias);
            Cv2.Circle(image, point, 10, new Scalar(245, 245, 245), 2, LineTypes.AntiAlias);
        }

        return image;
    }

    public static Mat CreateFeaturePipelineSource()
    {
        // Deliberately colored source image so Step 1 -> Step 2 is visually obvious.
        // The following processing steps then progressively remove noise, segment the image,
        // close mask gaps, and finally overlay extracted contour features on this color source.
        var image = new Mat(new Size(960, 540), MatType.CV_8UC3, new Scalar(26, 31, 38));

        // Blue-gray workpiece plate.
        Cv2.Rectangle(image, new Rect(75, 65, 810, 410), new Scalar(125, 92, 62), -1);
        Cv2.Rectangle(image, new Rect(75, 65, 810, 410), new Scalar(215, 190, 155), 3);

        // Warm yellow rectangular feature.
        Cv2.Rectangle(image, new Rect(145, 135, 185, 120), new Scalar(55, 205, 245), -1);
        Cv2.Rectangle(image, new Rect(145, 135, 185, 120), new Scalar(235, 245, 255), 3);

        // Cyan circular feature with a dark center hole.
        Cv2.Circle(image, new Point(570, 190), 78, new Scalar(220, 190, 70), -1, LineTypes.AntiAlias);
        Cv2.Circle(image, new Point(570, 190), 78, new Scalar(250, 245, 225), 3, LineTypes.AntiAlias);
        Cv2.Circle(image, new Point(570, 190), 28, new Scalar(32, 38, 44), -1, LineTypes.AntiAlias);

        // Green triangular feature.
        var triangle = new[]
        {
            new Point(680, 380),
            new Point(805, 280),
            new Point(845, 405)
        };
        Cv2.FillConvexPoly(image, triangle, new Scalar(70, 220, 110), LineTypes.AntiAlias);
        Cv2.Polylines(image, new[] { triangle }, true, new Scalar(225, 255, 235), 3, LineTypes.AntiAlias);

        // Bright red/orange fastener features.
        foreach (var point in new[]
                 {
                     new Point(430, 330), new Point(490, 330),
                     new Point(430, 390), new Point(490, 390)
                 })
        {
            Cv2.Circle(image, point, 18, new Scalar(55, 85, 235), -1, LineTypes.AntiAlias);
            Cv2.Circle(image, point, 18, new Scalar(235, 240, 255), 2, LineTypes.AntiAlias);
        }

        // Fine dark scratches/noise demonstrate the benefit of blur + morphology.
        Cv2.Line(image, new Point(120, 300), new Point(350, 320), new Scalar(42, 47, 52), 4, LineTypes.AntiAlias);
        Cv2.Line(image, new Point(350, 90), new Point(620, 110), new Scalar(45, 50, 56), 3, LineTypes.AntiAlias);

        Cv2.PutText(image, "ORIGINAL COLOR", new Point(105, 445), HersheyFonts.HersheySimplex, 0.75,
            new Scalar(245, 245, 245), 2, LineTypes.AntiAlias);

        return image;
    }

    public static Mat CreateTemplate()
    {
        var template = new Mat(new Size(120, 100), MatType.CV_8UC3, new Scalar(235, 235, 235));
        Cv2.Rectangle(template, new Rect(12, 12, 96, 76), new Scalar(20, 20, 20), 4);
        Cv2.Line(template, new Point(28, 50), new Point(92, 50), new Scalar(20, 20, 20), 5, LineTypes.AntiAlias);
        Cv2.Line(template, new Point(60, 27), new Point(60, 73), new Scalar(20, 20, 20), 5, LineTypes.AntiAlias);
        Cv2.Circle(template, new Point(60, 50), 13, new Scalar(20, 20, 20), 3, LineTypes.AntiAlias);
        return template;
    }

    public static Mat CreateTemplateSearch()
    {
        var image = new Mat(new Size(960, 540), MatType.CV_8UC3, new Scalar(38, 43, 49));
        Cv2.Rectangle(image, new Rect(45, 45, 870, 450), new Scalar(184, 190, 196), -1);
        Cv2.PutText(image, "SEARCH IMAGE", new Point(70, 90), HersheyFonts.HersheySimplex, 0.8,
            new Scalar(55, 60, 65), 2, LineTypes.AntiAlias);

        using var template = CreateTemplate();
        using (var matchRoi = new Mat(image, new Rect(610, 305, template.Width, template.Height)))
        {
            template.CopyTo(matchRoi);
        }

        // Similar distractors make the demo prove that the learned ROI is actually used.
        Cv2.Rectangle(image, new Rect(145, 165, 120, 100), new Scalar(235, 235, 235), -1);
        Cv2.Rectangle(image, new Rect(157, 177, 96, 76), new Scalar(20, 20, 20), 4);
        Cv2.Line(image, new Point(175, 215), new Point(235, 215), new Scalar(20, 20, 20), 5);
        Cv2.Circle(image, new Point(205, 215), 13, new Scalar(20, 20, 20), 3);

        Cv2.Circle(image, new Point(430, 215), 65, new Scalar(80, 90, 100), -1, LineTypes.AntiAlias);
        Cv2.Line(image, new Point(350, 410), new Point(540, 390), new Scalar(45, 50, 55), 8, LineTypes.AntiAlias);
        return image;
    }
}
