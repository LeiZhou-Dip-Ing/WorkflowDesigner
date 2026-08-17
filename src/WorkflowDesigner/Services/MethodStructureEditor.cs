using WorkflowCore.WpfDemo.Editor;

namespace WorkflowCore.WpfDemo.Services;

public enum MethodBlockKind
{
    If,
    For,
    While
}

public static class MethodStructureEditor
{
    public static bool CanSurround(MethodLine? line)
        => line?.Action != null && !IsStructuralAction(line.Action.ActionType);

    public static MethodLine? InsertBlock(
        WorkflowMethod method,
        MethodLine? currentLine,
        MethodBlockKind blockKind,
        bool surroundCurrent,
        Func<string, WorkflowAction> actionFactory)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(actionFactory);

        var lines = GetOrderedLines(method);
        var currentIndex = currentLine == null ? -1 : lines.IndexOf(currentLine);
        if (surroundCurrent && (currentIndex < 0 || !CanSurround(currentLine)))
        {
            return null;
        }

        var (beginActionType, endActionType) = GetBlockActionTypes(blockKind);
        var nestingLevel = currentIndex < 0 ? 0 : currentLine!.NestingLevel;
        var insertionIndex = currentIndex < 0 ? lines.Count : currentIndex + 1;

        if (surroundCurrent)
        {
            insertionIndex = currentIndex;
            currentLine!.NestingLevel = nestingLevel + 1;
        }
        else if (currentIndex >= 0 && OpensChildScope(currentLine!.Action?.ActionType))
        {
            nestingLevel++;
        }

        var beginLine = MethodLine.Create(0, nestingLevel, actionFactory(beginActionType));
        var endLine = MethodLine.Create(0, nestingLevel, actionFactory(endActionType));
        lines.Insert(insertionIndex, beginLine);
        lines.Insert(insertionIndex + (surroundCurrent ? 2 : 1), endLine);
        ReplaceLines(method, lines);
        return beginLine;
    }

    public static bool CanAddElseBranch(WorkflowMethod method, MethodLine? selectedLine)
        => FindIfBlock(method, selectedLine) is { HasElse: false };

    public static MethodLine? AddElseBranch(
        WorkflowMethod method,
        MethodLine? selectedLine,
        Func<string, WorkflowAction> actionFactory)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(actionFactory);

        var block = FindIfBlock(method, selectedLine);
        if (block == null || block.Value.HasElse)
        {
            return null;
        }

        var lines = GetOrderedLines(method);
        var elseLine = MethodLine.Create(
            0,
            lines[block.Value.BeginIndex].NestingLevel,
            actionFactory("else"));
        lines.Insert(block.Value.EndIndex, elseLine);
        ReplaceLines(method, lines);
        return elseLine;
    }

    public static bool CanDelete(WorkflowMethod method, MethodLine? selectedLine)
        => GetDeletionSet(method, selectedLine).Count > 0;

    public static IReadOnlyList<MethodLine> GetDeletionSet(
        WorkflowMethod method,
        MethodLine? selectedLine)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (selectedLine?.Action == null)
        {
            return Array.Empty<MethodLine>();
        }

        var lines = GetOrderedLines(method);
        var selectedIndex = lines.IndexOf(selectedLine);
        if (selectedIndex < 0)
        {
            return Array.Empty<MethodLine>();
        }

        var actionType = selectedLine.Action.ActionType;
        if (IsEndAction(actionType))
        {
            return Array.Empty<MethodLine>();
        }

        if (TryGetMatchingEndAction(actionType, out var endActionType))
        {
            var endIndex = FindMatchingEndIndex(lines, selectedIndex, actionType, endActionType);
            return endIndex < 0
                ? Array.Empty<MethodLine>()
                : lines.GetRange(selectedIndex, endIndex - selectedIndex + 1);
        }

        if (string.Equals(actionType, "else", StringComparison.OrdinalIgnoreCase))
        {
            var endIndex = FindNextAtLevel(lines, selectedIndex + 1, "endIf", selectedLine.NestingLevel);
            return endIndex < 0
                ? Array.Empty<MethodLine>()
                : lines.GetRange(selectedIndex, endIndex - selectedIndex);
        }

        return new[] { selectedLine };
    }

    private static IfBlock? FindIfBlock(WorkflowMethod method, MethodLine? selectedLine)
    {
        if (selectedLine?.Action == null)
        {
            return null;
        }

        var lines = GetOrderedLines(method);
        var selectedIndex = lines.IndexOf(selectedLine);
        if (selectedIndex < 0)
        {
            return null;
        }

        var actionType = selectedLine.Action.ActionType;
        int beginIndex;
        int endIndex;
        if (string.Equals(actionType, "if", StringComparison.OrdinalIgnoreCase))
        {
            beginIndex = selectedIndex;
            endIndex = FindMatchingEndIndex(lines, beginIndex, "if", "endIf");
        }
        else if (string.Equals(actionType, "endIf", StringComparison.OrdinalIgnoreCase))
        {
            endIndex = selectedIndex;
            beginIndex = FindMatchingBeginIndex(lines, endIndex, "if", "endIf");
        }
        else
        {
            return null;
        }

        if (beginIndex < 0 || endIndex < 0)
        {
            return null;
        }

        var nestingLevel = lines[beginIndex].NestingLevel;
        var hasElse = lines
            .Skip(beginIndex + 1)
            .Take(endIndex - beginIndex - 1)
            .Any(line => line.NestingLevel == nestingLevel
                         && string.Equals(line.Action?.ActionType, "else", StringComparison.OrdinalIgnoreCase));
        return new IfBlock(beginIndex, endIndex, hasElse);
    }

    private static int FindMatchingEndIndex(
        IReadOnlyList<MethodLine> lines,
        int beginIndex,
        string beginActionType,
        string endActionType)
    {
        var depth = 0;
        for (var index = beginIndex + 1; index < lines.Count; index++)
        {
            var actionType = lines[index].Action?.ActionType;
            if (string.Equals(actionType, beginActionType, StringComparison.OrdinalIgnoreCase))
            {
                depth++;
            }
            else if (string.Equals(actionType, endActionType, StringComparison.OrdinalIgnoreCase))
            {
                if (depth == 0)
                {
                    return index;
                }

                depth--;
            }
        }

        return -1;
    }

    private static int FindMatchingBeginIndex(
        IReadOnlyList<MethodLine> lines,
        int endIndex,
        string beginActionType,
        string endActionType)
    {
        var depth = 0;
        for (var index = endIndex - 1; index >= 0; index--)
        {
            var actionType = lines[index].Action?.ActionType;
            if (string.Equals(actionType, endActionType, StringComparison.OrdinalIgnoreCase))
            {
                depth++;
            }
            else if (string.Equals(actionType, beginActionType, StringComparison.OrdinalIgnoreCase))
            {
                if (depth == 0)
                {
                    return index;
                }

                depth--;
            }
        }

        return -1;
    }

    private static int FindNextAtLevel(
        IReadOnlyList<MethodLine> lines,
        int startIndex,
        string actionType,
        int nestingLevel)
    {
        for (var index = startIndex; index < lines.Count; index++)
        {
            if (lines[index].NestingLevel == nestingLevel
                && string.Equals(lines[index].Action?.ActionType, actionType, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool TryGetMatchingEndAction(string actionType, out string endActionType)
    {
        endActionType = actionType.ToLowerInvariant() switch
        {
            "if" => "endIf",
            "for" => "endFor",
            "while" => "endWhile",
            _ => string.Empty
        };
        return endActionType.Length > 0;
    }

    private static bool IsStructuralAction(string actionType)
        => TryGetMatchingEndAction(actionType, out _)
           || IsEndAction(actionType)
           || string.Equals(actionType, "else", StringComparison.OrdinalIgnoreCase);

    private static bool IsEndAction(string actionType)
        => actionType.Equals("endIf", StringComparison.OrdinalIgnoreCase)
           || actionType.Equals("endFor", StringComparison.OrdinalIgnoreCase)
           || actionType.Equals("endWhile", StringComparison.OrdinalIgnoreCase);

    private static bool OpensChildScope(string? actionType)
        => actionType != null
           && (TryGetMatchingEndAction(actionType, out _)
               || string.Equals(actionType, "else", StringComparison.OrdinalIgnoreCase));

    private static (string Begin, string End) GetBlockActionTypes(MethodBlockKind blockKind)
        => blockKind switch
        {
            MethodBlockKind.If => ("if", "endIf"),
            MethodBlockKind.For => ("for", "endFor"),
            MethodBlockKind.While => ("while", "endWhile"),
            _ => throw new ArgumentOutOfRangeException(nameof(blockKind), blockKind, null)
        };

    internal static List<MethodLine> GetOrderedLines(WorkflowMethod method)
        => method.MethodLines
            .OrderBy(line => line.LineNo)
            .ThenBy(line => line.SequenceNo)
            .ToList();

    internal static void ReplaceLines(WorkflowMethod method, IReadOnlyList<MethodLine> lines)
    {
        method.MethodLines.Clear();
        for (var index = 0; index < lines.Count; index++)
        {
            lines[index].LineNo = (index + 1) * 10;
            lines[index].SequenceNo = index;
            method.MethodLines.Add(lines[index]);
        }
    }

    private readonly record struct IfBlock(int BeginIndex, int EndIndex, bool HasElse);
}
