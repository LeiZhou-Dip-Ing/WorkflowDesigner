# OpenCV external Runtime Action plugin

This plugin is deliberately outside the Runtime host and uses only public SDK contracts plus OpenCvSharp.

Actions:

- SDK Load Image (`sample://source` / `sample://template` supported for the self-contained demo)
- SDK Convert to Gray
- SDK Invert Image
- SDK Gaussian Blur
- SDK Threshold
- SDK Canny Edges
- SDK Measure Line (Canny + HoughLinesP)
- SDK Measure Circle (HoughCircles)
- SDK Template Match (MatchTemplate)
- SDK Save Image

Every image-producing Action returns a Runtime-owned image handle through an Action output. With `PublishPreview = true`, the output is also published as a line-scoped `VisionPreview` event. That is how Gray/Blur/Threshold/etc. each get their own processed image in the Designer without sending OpenCvSharp `Mat` objects into WPF.

The optional UI is implemented separately by `WorkflowRuntime.OpenCvSamplePlugin.Designer.Wpf`.
# OpenCV Action SDK plugin

This project is one extensible Runtime plugin and produces one Action assembly:

`WorkflowRuntime.OpenCvSamplePlugin.dll`

Each tool is an Action class registered by `OpenCvSamplePlugin`. Adding another image tool does
not create another plugin project or Runtime DLL.

`InteractiveTemplateMatchSdkAction` keeps the original `TemplateMatchSdkAction` intact and adds a
complete learn-and-match workflow: rectangular ROI learning, optional template-file persistence,
OpenCV template matching, score/position outputs, and an annotated preview.
