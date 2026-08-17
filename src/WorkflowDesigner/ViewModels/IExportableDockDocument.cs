using WorkflowCore.WpfDemo.Editor;

namespace WorkflowCore.WpfDemo.ViewModels;

public interface IExportableDockDocument
{
    string Title { get; }

    WorkflowEditorDocument CreateExportDocument();
}
