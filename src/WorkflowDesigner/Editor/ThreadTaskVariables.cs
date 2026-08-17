using System.Text.Json.Nodes;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.Editor;

/// <summary>Maintains the method variables that identify background workflow tasks.</summary>
public static class ThreadTaskVariables
{
    public const string DataSourceName = "threadTaskVariables";
    public const string CreateKindName = "taskIdVariable";
    public const string DataTypeName = "integer";

    public static IReadOnlyList<string> GetDeclaredNames(WorkflowMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);

        var declaredIntegerNames = method.MethodVariables
            .Where(variable => variable.IsActive
                && string.Equals(variable.DataType, DataTypeName, StringComparison.OrdinalIgnoreCase))
            .Select(variable => variable.VariableName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return GetThreadStartNames(method)
            .Where(declaredIntegerNames.Contains)
            .ToArray();
    }

    public static int EnsureDeclarations(WorkflowMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);

        var added = 0;
        foreach (var variableName in GetThreadStartNames(method))
        {
            if (method.MethodVariables.Any(variable => string.Equals(
                    variable.VariableName,
                    variableName,
                    StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var variable = new WorkflowVariable
            {
                VariableName = variableName,
                DataType = DataTypeName,
                OrderIndex = method.MethodVariables.Count
            };
            method.MethodVariables.Add(variable);
            added++;
        }

        return added;
    }

    private static IReadOnlyList<string> GetThreadStartNames(WorkflowMethod method)
        => method.MethodLines
            .Select(line => line.Action)
            .Where(action => action != null
                && string.Equals(action.ActionType, "threadStart", StringComparison.OrdinalIgnoreCase))
            .Select(action => ReadString(action!.GetProperty("TaskVarName")).Trim())
            .Where(IsValidVariableName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string ReadString(JsonNode? value)
        => value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text)
            ? text
            : string.Empty;

    private static bool IsValidVariableName(string value)
        => WorkflowVariableNaming.IsVariable(value);
}
