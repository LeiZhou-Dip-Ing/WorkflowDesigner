# OpenCV Feature Pipeline Demo

`OpenCvFeaturePipelineDemo.wflowx` is a deploy-only encrypted workflow. It cannot be imported or
edited by WorkflowDesigner. Keep the editable JSON source in the private authoring environment and
provide the encryption key only to the backend runtime.

Run method `FeaturePipeline` once, then click each row. Every Vision Action publishes a line-scoped preview, so the right workspace shows that step's own result:

1. **SDK Load Image** — untouched generated workpiece source (`sample://feature-source`)
2. **SDK Convert to Gray** — grayscale result
3. **SDK Gaussian Blur** — denoised grayscale result
4. **SDK Threshold** — Otsu binary segmentation
5. **SDK Morphology Close** — cleaned binary mask
6. **SDK Extract Contour Features** — contours, bounding boxes, centroids and area labels overlaid on the original image
7. **SDK Save Image** — saves the final annotated result

The feature Action also exposes scalar outputs: feature count, largest area, and largest feature center X/Y.

The preprocessing and feature workspaces are provided by the optional UI entry point inside `WorkflowRuntime.OpenCvSamplePlugin.dll`. Without loading its Designer entry point, the Actions remain runnable and fall back to the generic metadata UI.
