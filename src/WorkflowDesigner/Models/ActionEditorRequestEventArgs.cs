using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.Models;

public sealed class ActionEditorRequestEventArgs : EventArgs
{
    public ActionEditorRequestEventArgs(
        MethodLineViewItem lineItem,
        WorkflowActionDescriptorDto descriptor,
        string editorKey)
    {
        LineItem = lineItem ?? throw new ArgumentNullException(nameof(lineItem));
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        EditorKey = editorKey ?? string.Empty;
    }

    public MethodLineViewItem LineItem { get; }

    public WorkflowActionDescriptorDto Descriptor { get; }

    public string EditorKey { get; }
}
