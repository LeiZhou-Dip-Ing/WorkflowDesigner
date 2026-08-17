# OpenCV Vision samples

## OpenCvLegacyToolsDemo.json

This is the end-to-end external Action + external Designer UI example.
It intentionally mirrors several useful ideas from the legacy vision project, but all image processing is OpenCvSharp and Runtime/Designer remain separated.

Pipeline:

1. SDK Load Image (`sample://source`)
2. SDK Load Image (`sample://template`)
3. SDK Convert to Gray
4. SDK Gaussian Blur
5. SDK Threshold
6. SDK Canny Edges
7. SDK Measure Line (Canny + HoughLinesP)
8. SDK Measure Circle (HoughCircles)
9. SDK Template Match (MatchTemplate)
10. SDK Save Image (`%TEMP%\\WorkflowVisionDemo\\final-match.png`)

Every processing/measurement Action has `PublishPreview = true`. Runtime publishes a `VisionPreview` event with the method name and line number. The WPF Designer caches previews by method + line, so after one run you can click Gray, Blur, Threshold, Canny, Measure Line, Measure Circle, or Template Match and see that exact Action's latest processed image.

The demo uses `sample://source` and `sample://template`, which are generated inside the external OpenCV Runtime plugin, so no local image path is required.

Custom UI examples:

- Gray / Gaussian / Threshold: external preprocessing Workspace.
- Gaussian kernel: external custom Property Editor.
- Measure Line / Measure Circle: external measurement Workspace + external double-click editor.
- Template Match: external matching Workspace + external double-click editor.
- Canny: external double-click editor.
- Load / Invert / Save: can fall back to generic metadata UI, demonstrating that custom UI remains optional.

The **Run / Preview** button in an external Workspace/Dialog calls the narrow `IWorkflowDesignerActionContext.RunPreviewAsync()` contract. It does not receive `MainWindowViewModel` and does not instantiate Runtime Actions in WPF.
