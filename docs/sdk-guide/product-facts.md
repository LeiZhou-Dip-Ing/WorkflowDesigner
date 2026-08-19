# WorkflowDesigner SDK guide facts

Source of truth: the current WorkflowDesigner repository and its public package references.

- Audience: invited UI, workflow-authoring, and external Action developers.
- WorkflowDesigner consumes protected Workflow runtime and SDK packages through NuGet.
- External runtime Actions can inherit `WorkflowActionBase` or implement `IWorkflowActionHandler`.
- `WorkflowActionBase` Actions can declare metadata, inputs, outputs, and ordinary properties with public SDK attributes.
- External Actions are registered through `IWorkflowActionPlugin.Register(IWorkflowActionPluginBuilder)`.
- Optional WPF designer extensions implement `IWorkflowDesignerExtension`.
- Designer extensions may register a property editor, selected-Action workspace, or double-click Action editor.
- Designer UI extensions operate through `IWorkflowDesignerActionContext` and public property models.
- The sample projects are the canonical examples used by the guide.
- Private WorkflowCore implementation details, internal algorithms, encryption details, and package internals are out of scope.

## Design assumptions

- Deliverable: browser-first Web PPT, not an editable `.pptx` file.
- Language: Traditional Chinese and English, switchable without reloading.
- Brand assets: no formal logo or visual identity was provided; drafts derive their palette from the current dark WPF UI and green runtime accents.
- Deck goal: onboarding and extension SDK teaching, not sales or internal Core documentation.

