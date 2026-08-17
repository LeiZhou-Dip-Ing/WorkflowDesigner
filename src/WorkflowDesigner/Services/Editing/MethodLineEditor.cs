using System.Text.Json.Nodes;
using WorkflowCore.WpfDemo.Editor;

namespace WorkflowCore.WpfDemo.Services.Editing;

/// <summary>Applies structural edits to method lines while preserving valid block nesting and line order.</summary>
public sealed class MethodLineEditor : IMethodLineEditor
{
    private MethodLineClipboardEntry? _clipboard;

    public bool HasCopiedLine => _clipboard != null;

    public MethodLine AddAction(
        WorkflowMethod method,
        WorkflowAction action,
        int? insertBeforeLineNo,
        Func<MethodLine, bool> opensChildScope)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(opensChildScope);

        var ordered = MethodStructureEditor.GetOrderedLines(method);
        var insertionIndex = insertBeforeLineNo.HasValue
            ? ordered.FindIndex(line => line.LineNo == insertBeforeLineNo.Value)
            : ordered.Count;
        if (insertionIndex < 0)
        {
            insertionIndex = ordered.Count;
        }

        var nestingLevel = SuggestNestingLevel(ordered, insertionIndex, insertBeforeLineNo.HasValue, opensChildScope);
        var line = MethodLine.Create(0, nestingLevel, action);
        ordered.Insert(insertionIndex, line);
        MethodStructureEditor.ReplaceLines(method, ordered);
        return line;
    }

    public MethodLine AddActionAfter(
        WorkflowMethod method,
        MethodLine? selectedLine,
        WorkflowAction action,
        Func<MethodLine, bool> opensChildScope)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(opensChildScope);

        var ordered = MethodStructureEditor.GetOrderedLines(method);
        var selectedIndex = selectedLine == null ? -1 : ordered.IndexOf(selectedLine);
        var nestingLevel = selectedIndex < 0 ? 0 : selectedLine!.NestingLevel;
        if (selectedIndex >= 0 && opensChildScope(selectedLine!))
        {
            nestingLevel++;
        }

        var line = MethodLine.Create(0, nestingLevel, action);
        ordered.Insert(selectedIndex < 0 ? ordered.Count : selectedIndex + 1, line);
        MethodStructureEditor.ReplaceLines(method, ordered);
        return line;
    }

    public MethodLine? Delete(WorkflowMethod method, MethodLine selectedLine, out int deletedCount)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(selectedLine);

        var ordered = MethodStructureEditor.GetOrderedLines(method);
        var deletion = MethodStructureEditor.GetDeletionSet(method, selectedLine);
        deletedCount = deletion.Count;
        if (deletedCount == 0)
        {
            return selectedLine;
        }

        var selectionIndex = ordered.IndexOf(deletion[0]);
        ordered.RemoveAll(deletion.Contains);
        MethodStructureEditor.ReplaceLines(method, ordered);
        return ordered.Count == 0
            ? null
            : ordered[Math.Min(Math.Max(selectionIndex, 0), ordered.Count - 1)];
    }

    public bool Move(WorkflowMethod method, MethodLine selectedLine, int direction)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(selectedLine);

        var ordered = MethodStructureEditor.GetOrderedLines(method);
        var index = ordered.IndexOf(selectedLine);
        var newIndex = index + direction;
        if (index < 0 || newIndex < 0 || newIndex >= ordered.Count)
        {
            return false;
        }

        (ordered[index], ordered[newIndex]) = (ordered[newIndex], ordered[index]);
        MethodStructureEditor.ReplaceLines(method, ordered);
        return true;
    }

    public void SetActive(MethodLine line, bool isActive)
    {
        ArgumentNullException.ThrowIfNull(line);
        line.IsActive = isActive;
        if (line.Action != null)
        {
            line.Action.IsActive = isActive;
        }
    }

    public void Copy(MethodLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.Action == null)
        {
            throw new ArgumentException("The method line does not contain an action.", nameof(line));
        }

        _clipboard = new MethodLineClipboardEntry(line.Action.ToJsonObject(), line.Comment);
    }

    public MethodLine? PasteAfter(WorkflowMethod method, MethodLine? selectedLine)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (_clipboard == null)
        {
            return null;
        }

        var action = new WorkflowAction((JsonObject)_clipboard.Action.DeepClone())
        {
            Uid = Guid.NewGuid()
        };
        var ordered = MethodStructureEditor.GetOrderedLines(method);
        var selectedIndex = selectedLine == null ? -1 : ordered.IndexOf(selectedLine);
        var nestingLevel = selectedIndex < 0 ? 0 : selectedLine!.NestingLevel;
        var line = MethodLine.Create(0, nestingLevel, action, _clipboard.Comment);
        ordered.Insert(selectedIndex < 0 ? ordered.Count : selectedIndex + 1, line);
        MethodStructureEditor.ReplaceLines(method, ordered);
        return line;
    }

    public void Renumber(WorkflowMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var ordered = MethodStructureEditor.GetOrderedLines(method);
        for (var index = 0; index < ordered.Count; index++)
        {
            ordered[index].LineNo = (index + 1) * 10;
            ordered[index].SequenceNo = index;
        }
    }

    public void Prepare(WorkflowProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        foreach (var method in project.Methods)
        {
            Renumber(method);
        }
    }

    public bool CanSurround(MethodLine? line) => MethodStructureEditor.CanSurround(line);

    public bool CanDelete(WorkflowMethod method, MethodLine? line)
        => MethodStructureEditor.CanDelete(method, line);

    public bool CanAddElseBranch(WorkflowMethod method, MethodLine? line)
        => MethodStructureEditor.CanAddElseBranch(method, line);

    public MethodLine? InsertBlock(
        WorkflowMethod method,
        MethodLine? line,
        MethodBlockKind blockKind,
        bool surroundCurrent,
        Func<string, WorkflowAction> actionFactory)
        => MethodStructureEditor.InsertBlock(method, line, blockKind, surroundCurrent, actionFactory);

    public MethodLine? AddElseBranch(
        WorkflowMethod method,
        MethodLine? line,
        Func<string, WorkflowAction> actionFactory)
        => MethodStructureEditor.AddElseBranch(method, line, actionFactory);

    private static int SuggestNestingLevel(
        IReadOnlyList<MethodLine> ordered,
        int insertionIndex,
        bool insertingBefore,
        Func<MethodLine, bool> opensChildScope)
    {
        if (ordered.Count == 0)
        {
            return 0;
        }

        if (insertingBefore && insertionIndex < ordered.Count)
        {
            return ordered[insertionIndex].NestingLevel;
        }

        var previous = ordered[^1];
        return previous.NestingLevel + (opensChildScope(previous) ? 1 : 0);
    }

    private sealed record MethodLineClipboardEntry(JsonObject Action, string? Comment);
}
