# OpenCV external WPF Designer plugin

This assembly demonstrates three independent UI extension levels without changing the host WPF project:

1. Property editor: odd-kernel editor for Gaussian Blur.
2. Selected-Action workspace: preprocessing, measurement, and template-matching layouts inspired by the legacy vision project.
3. Double-click action editor: Canny, Measure Line, Measure Circle, and Template Match.

The views are action-specific but do not reference `MainWindowViewModel` or Runtime Action CLR objects. They receive `IWorkflowDesignerActionContext`, edit the same metadata property models as the generic Property Panel, and invoke `RunPreviewAsync()` through the narrow SDK context.

Runtime and Designer are still separate processes. Image results arrive as `VisionPreview` events keyed by method + line number; selecting a line shows that exact Action's latest output.
