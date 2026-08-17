using WorkflowCore.WpfDemo.Editor;

namespace WorkflowCore.WpfDemo.Services.Editing;

public interface IMethodLineEditor
{
    bool HasCopiedLine { get; }

    MethodLine AddAction(
        WorkflowMethod method,
        WorkflowAction action,
        int? insertBeforeLineNo,
        Func<MethodLine, bool> opensChildScope);

    MethodLine AddActionAfter(
        WorkflowMethod method,
        MethodLine? selectedLine,
        WorkflowAction action,
        Func<MethodLine, bool> opensChildScope);

    MethodLine? Delete(WorkflowMethod method, MethodLine selectedLine, out int deletedCount);

    bool Move(WorkflowMethod method, MethodLine selectedLine, int direction);

    void SetActive(MethodLine line, bool isActive);

    void Copy(MethodLine line);

    MethodLine? PasteAfter(WorkflowMethod method, MethodLine? selectedLine);

    void Renumber(WorkflowMethod method);

    void Prepare(WorkflowProject project);

    bool CanSurround(MethodLine? line);

    bool CanDelete(WorkflowMethod method, MethodLine? line);

    bool CanAddElseBranch(WorkflowMethod method, MethodLine? line);

    MethodLine? InsertBlock(
        WorkflowMethod method,
        MethodLine? line,
        MethodBlockKind blockKind,
        bool surroundCurrent,
        Func<string, WorkflowAction> actionFactory);

    MethodLine? AddElseBranch(
        WorkflowMethod method,
        MethodLine? line,
        Func<string, WorkflowAction> actionFactory);
}
