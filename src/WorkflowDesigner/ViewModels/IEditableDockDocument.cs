namespace WorkflowCore.WpfDemo.ViewModels;

public interface IEditableDockDocument : IExportableDockDocument
{
    string ContentId { get; }

    bool IsDirty { get; set; }
}
