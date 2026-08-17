# WorkflowCore V5 WPF Designer

The WPF application is a standalone AvalonDock workflow editor and runtime client. It references `WorkflowRuntime.Contracts` only; execution, persistence on the device, plugin loading, and SignalR transport stay in the Runtime Service.

## Editing model

Each method or script opens as its own AvalonDock document. A document owns its selection, dirty marker, undo history, saved baseline, runtime revision, and runtime comparison metadata.

- A new or edited document shows `*` until its own **Save** command succeeds.
- **Undo** applies only to unsaved edits in that document. Saving establishes a new baseline and clears Undo.
- Closing and reopening a method resolves the document by its stable identity, not only its display name.
- Import adds a method or script to the current project. A name collision can replace that item or create a numbered copy without clearing unrelated documents.
- Export writes only the selected AvalonDock document.
- Draft autosave is separate from an explicit document save.

The editor supports method creation/deletion, drag-and-drop Actions, line ordering and nesting, Action properties, variables, comments, active/deactivated lines, import/export, and the existing method/script document UI.

## Local save and runtime deployment

Local editing and device state are deliberately separate:

- **Save** records the selected document locally.
- **Deploy** publishes the saved workflow to the Runtime Service using its last known revision.
- **Download** retrieves the runtime version without silently overwriting local edits.
- **Compare** reports whether local saved content, local unsaved content, and the deployed runtime content differ.
- **Run** refuses to start when the selected workflow contains unsaved documents, so the runtime never executes an accidental stale version.

If another client deploys first, the service returns a revision conflict. The designer preserves local content and asks the user to compare or download rather than overwriting the newer device revision.

## Runtime display

Commands and workflow documents use REST. Live Action execution events use SignalR. The Action run log is append-only for the editor session and displays Action icon, state, output when present, method/line information, and backend timestamps. Delay Actions use the duration reported by the backend for their countdown display.

The backend retains terminal run status independently from SignalR delivery. A temporary transport failure therefore does not turn a successful run into a failed run.

## Run

Start the Runtime Service first, then the designer:

```powershell
dotnet run --project src/WorkflowRuntime.WindowsService
dotnet run --project samples/WorkflowCore.WpfDemo
```

The default runtime address is `http://localhost:5097`. Action metadata and icons come from the runtime Action Catalog, including manually deployed plugins; the editor does not hardcode plugin Actions.

C# Script editing remains available as an editor document, but a C# Script Runtime is not implemented. Action input/output binding and turning the Console demo into a remote client are also outside this version.
