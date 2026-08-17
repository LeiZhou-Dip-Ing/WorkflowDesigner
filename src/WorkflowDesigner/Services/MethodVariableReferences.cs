using WorkflowDesigner.Contracts;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Models;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.Services;

/// <summary>Finds and renames variable references embedded in method Actions and expressions.</summary>
public static partial class MethodVariableReferences
{
    private static readonly HashSet<string> ExpressionKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "or", "not", "true", "false", "null"
    };

    public static IReadOnlyList<MethodVariableOverviewItem> Discover(
        WorkflowMethod method,
        Func<string, WorkflowActionDescriptorDto?> descriptorResolver)
        => DiscoverCore(method, descriptorResolver, includeDeclarations: true);

    public static IReadOnlyList<MethodVariableOverviewItem> DiscoverReferences(
        WorkflowMethod method,
        Func<string, WorkflowActionDescriptorDto?> descriptorResolver)
        => DiscoverCore(method, descriptorResolver, includeDeclarations: false);

    private static IReadOnlyList<MethodVariableOverviewItem> DiscoverCore(
        WorkflowMethod method,
        Func<string, WorkflowActionDescriptorDto?> descriptorResolver,
        bool includeDeclarations)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(descriptorResolver);

        var variables = new Dictionary<string, VariableAccumulator>(StringComparer.OrdinalIgnoreCase);
        if (includeDeclarations)
        {
            foreach (var declaration in method.MethodVariables.Where(variable => variable.IsActive))
            {
                AddVariable(
                    variables,
                    declaration.VariableName,
                    isDeclared: true,
                    dataType: declaration.DataType,
                    declaration: declaration);
            }
        }

        foreach (var line in method.MethodLines.Where(line => line.IsActive && line.Action is { IsActive: true }))
        {
            var action = line.Action!;
            var descriptor = descriptorResolver(action.ActionType);
            if (descriptor == null)
            {
                continue;
            }

            foreach (var field in descriptor.GetAllFields())
            {
                if (IsOutputField(field))
                {
                    AddVariable(
                        variables,
                        action.GetOutputBinding(field.Name),
                        dataType: NormalizeDataType(field.ValueType));
                    continue;
                }

                if (field.IsReadOnly)
                {
                    continue;
                }

                var value = action.GetProperty(field.Name);
                if (value == null)
                {
                    continue;
                }

                if (IsVariableField(field))
                {
                    foreach (var name in ReadDirectVariableNames(value))
                    {
                        AddVariable(variables, name, dataType: GetVariableFieldDataType(field));
                    }

                    continue;
                }

                if (IsParametersField(field))
                {
                    foreach (var name in ReadParameterVariables(value))
                    {
                        AddVariable(variables, name);
                    }

                    continue;
                }

                if (IsReturnVariableNamesField(field))
                {
                    foreach (var name in ReadCommaSeparatedVariableNames(value))
                    {
                        AddVariable(variables, name);
                    }

                    continue;
                }

                if (IsReturnValuesField(field))
                {
                    foreach (var expression in ReadString(value).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    {
                        foreach (var name in ReadExpressionVariables(expression))
                        {
                            AddVariable(variables, name);
                        }
                    }

                    continue;
                }

                if (IsExpressionField(field))
                {
                    foreach (var name in ReadExpressionVariables(ReadString(value)))
                    {
                        AddVariable(variables, name);
                    }

                    continue;
                }

                if (field.SupportsVariableExpression && IsInputField(field))
                {
                    foreach (var name in ReadExpressionVariables(ReadString(value)))
                    {
                        AddVariable(variables, name, dataType: NormalizeDataType(field.ValueType));
                    }
                }
            }
        }

        foreach (var variable in variables.Values)
        {
            variable.Declaration = method.MethodVariables.FirstOrDefault(declaration =>
                declaration.IsActive
                && string.Equals(declaration.VariableName, variable.Name, StringComparison.OrdinalIgnoreCase));
            variable.IsDeclared = variable.Declaration != null;
            if (variable.Declaration != null)
            {
                variable.DataType = variable.Declaration.DataType;
            }
        }

        return variables.Values
            .OrderBy(variable => variable.Name, StringComparer.OrdinalIgnoreCase)
            .Select(CreateOverviewItem)
            .ToArray();
    }

    public static int Rename(
        WorkflowProject project,
        WorkflowMethod currentMethod,
        string oldName,
        string newName,
        bool acrossAllMethods,
        Func<string, WorkflowActionDescriptorDto?> descriptorResolver)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(currentMethod);
        ArgumentException.ThrowIfNullOrWhiteSpace(oldName);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        ArgumentNullException.ThrowIfNull(descriptorResolver);

        var changes = 0;
        var methods = acrossAllMethods ? project.Methods : [currentMethod];
        foreach (var method in methods)
        {
            foreach (var variable in method.MethodVariables.Where(variable =>
                         string.Equals(variable.VariableName, oldName, StringComparison.OrdinalIgnoreCase)))
            {
                variable.VariableName = newName;
                changes++;
            }

            foreach (var parameter in method.Inputs.Concat(method.Outputs).Where(parameter =>
                         string.Equals(parameter.VariableName, oldName, StringComparison.OrdinalIgnoreCase)))
            {
                parameter.VariableName = newName;
                changes++;
            }

            foreach (var line in method.MethodLines.Where(line => line.Action != null))
            {
                var action = line.Action!;
                var descriptor = descriptorResolver(action.ActionType);
                if (descriptor == null)
                {
                    continue;
                }

                foreach (var field in descriptor.GetAllFields())
                {
                    if (IsOutputField(field))
                    {
                        var outputBinding = action.GetOutputBinding(field.Name);
                        if (string.Equals(outputBinding, oldName, StringComparison.OrdinalIgnoreCase))
                        {
                            action.SetOutputBinding(field.Name, newName);
                            changes++;
                        }

                        continue;
                    }

                    if (field.IsReadOnly)
                    {
                        continue;
                    }

                    var value = action.GetProperty(field.Name);
                    if (value == null)
                    {
                        continue;
                    }

                    var renamed = field.SupportsVariableExpression && IsInputField(field)
                        ? JsonValue.Create(RenamePluginInputReference(ReadString(value), oldName, newName))
                        : RenameFieldValue(field, value, oldName, newName);
                    if (renamed == null || JsonNode.DeepEquals(value, renamed))
                    {
                        continue;
                    }

                    action.SetProperty(field.Name, renamed);
                    changes++;
                }
            }
        }

        return changes;
    }

    private static JsonNode? RenameFieldValue(
        WorkflowActionFieldDto field,
        JsonNode value,
        string oldName,
        string newName)
    {
        if (IsVariableField(field))
        {
            var text = ReadString(value);
            return JsonValue.Create(string.Equals(text.Trim(), oldName, StringComparison.OrdinalIgnoreCase)
                ? newName
                : text);
        }

        if (IsParametersField(field) && value is JsonObject parameters)
        {
            var result = (JsonObject)parameters.DeepClone();
            foreach (var pair in parameters)
            {
                if (pair.Value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var expression))
                {
                    result[pair.Key] = RenameExpression(expression, oldName, newName);
                }
            }

            return result;
        }

        if (IsReturnVariableNamesField(field))
        {
            var names = ReadString(value).Split(',');
            for (var index = 0; index < names.Length; index++)
            {
                var leadingLength = names[index].Length - names[index].TrimStart().Length;
                var trailingLength = names[index].Length - names[index].TrimEnd().Length;
                var name = names[index].Trim();
                if (string.Equals(name, oldName, StringComparison.OrdinalIgnoreCase))
                {
                    names[index] = new string(' ', leadingLength) + newName + new string(' ', trailingLength);
                }
            }

            return JsonValue.Create(string.Join(',', names));
        }

        if (IsReturnValuesField(field) || IsExpressionField(field))
        {
            return JsonValue.Create(RenameExpression(ReadString(value), oldName, newName));
        }

        return value.DeepClone();
    }

    private static string RenamePluginInputReference(string value, string oldName, string newName)
        => RenameExpression(value, oldName, newName);

    private static IEnumerable<string> ReadParameterVariables(JsonNode value)
    {
        if (value is not JsonObject parameters)
        {
            yield break;
        }

        foreach (var pair in parameters)
        {
            if (pair.Value is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var expression))
            {
                continue;
            }

            foreach (var name in ReadExpressionVariables(expression))
            {
                yield return name;
            }
        }
    }

    private static IEnumerable<string> ReadDirectVariableNames(JsonNode value)
    {
        var name = ReadString(value).Trim();
        if (IsVariableName(name))
        {
            yield return name;
        }
    }

    private static IEnumerable<string> ReadCommaSeparatedVariableNames(JsonNode value)
        => ReadString(value)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(IsVariableName);

    private static IEnumerable<string> ReadBracedVariableNames(string expression)
        => BracedVariableRegex().Matches(expression)
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> ReadExpressionVariables(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            yield break;
        }

        var bracedNames = BracedVariableRegex().Matches(expression)
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var name in bracedNames)
        {
            yield return name;
        }

        if (bracedNames.Length > 0)
        {
            yield break;
        }

        var trimmed = expression.Trim();
        if (IsVariableName(trimmed) && !ExpressionKeywords.Contains(trimmed))
        {
            yield return trimmed;
            yield break;
        }

        if (!ContainsExpressionOperator(trimmed))
        {
            yield break;
        }

        var unquoted = RemoveQuotedText(trimmed);
        foreach (Match match in BareVariableRegex().Matches(unquoted))
        {
            var name = match.Value;
            if (!ExpressionKeywords.Contains(name))
            {
                yield return name;
            }
        }
    }

    private static string RenameExpression(string expression, string oldName, string newName)
    {
        if (string.IsNullOrEmpty(expression))
        {
            return expression;
        }

        var bracedChanged = BracedVariableRegex().Replace(expression, match =>
            string.Equals(match.Groups["name"].Value, oldName, StringComparison.OrdinalIgnoreCase)
                ? $"{{{newName}}}"
                : match.Value);

        var hasBracedVariables = BracedVariableRegex().IsMatch(expression);
        var trimmed = expression.Trim();
        if (hasBracedVariables || (!IsVariableName(trimmed) && !ContainsExpressionOperator(trimmed)))
        {
            return bracedChanged;
        }

        return ReplaceBareVariableOutsideQuotes(bracedChanged, oldName, newName);
    }

    private static string ReplaceBareVariableOutsideQuotes(string value, string oldName, string newName)
    {
        var result = new StringBuilder(value.Length);
        char? quote = null;
        for (var index = 0; index < value.Length;)
        {
            var current = value[index];
            if (current is '\'' or '"')
            {
                quote = quote == current ? null : quote ?? current;
                result.Append(current);
                index++;
                continue;
            }

            if (quote == null && (char.IsLetter(current) || current == '_'))
            {
                var start = index++;
                while (index < value.Length && (char.IsLetterOrDigit(value[index]) || value[index] is '_' or '.' or '-' or '$'))
                {
                    index++;
                }

                var token = value[start..index];
                result.Append(string.Equals(token, oldName, StringComparison.OrdinalIgnoreCase) ? newName : token);
                continue;
            }

            result.Append(current);
            index++;
        }

        return result.ToString();
    }

    private static string RemoveQuotedText(string value)
    {
        var result = new StringBuilder(value.Length);
        char? quote = null;
        foreach (var current in value)
        {
            if (current is '\'' or '"')
            {
                quote = quote == current ? null : quote ?? current;
                result.Append(' ');
                continue;
            }

            result.Append(quote == null ? current : ' ');
        }

        return result.ToString();
    }

    private static bool ContainsExpressionOperator(string value)
        => value.IndexOfAny(['+', '-', '*', '/', '%', '>', '<', '!', '&', '|']) >= 0
            || value.Contains("==", StringComparison.Ordinal);

    private static bool IsVariableField(WorkflowActionFieldDto field)
        => string.Equals(DesignerKeyCompatibility.NormalizePropertyEditor(field.Editor), WorkflowPropertyEditorKeys.Variable, StringComparison.OrdinalIgnoreCase)
            || string.Equals(field.EditorOptions?.DataSource, "methodVariables", StringComparison.OrdinalIgnoreCase);

    private static bool IsExpressionField(WorkflowActionFieldDto field)
        => string.Equals(DesignerKeyCompatibility.NormalizePropertyEditor(field.Editor), WorkflowPropertyEditorKeys.Expression, StringComparison.OrdinalIgnoreCase);

    private static bool IsOutputField(WorkflowActionFieldDto field)
        => string.Equals(field.Direction, "output", StringComparison.OrdinalIgnoreCase);

    private static bool IsInputField(WorkflowActionFieldDto field)
        => string.Equals(field.Direction, "input", StringComparison.OrdinalIgnoreCase)
            || string.Equals(field.Direction, "property", StringComparison.OrdinalIgnoreCase);

    private static bool IsParametersField(WorkflowActionFieldDto field)
        => string.Equals(field.Name, "Parameters", StringComparison.OrdinalIgnoreCase)
            && string.Equals(field.ValueType, "object", StringComparison.OrdinalIgnoreCase);

    private static bool IsReturnVariableNamesField(WorkflowActionFieldDto field)
        => string.Equals(field.Name, "ReturnVarNames", StringComparison.OrdinalIgnoreCase);

    private static bool IsReturnValuesField(WorkflowActionFieldDto field)
        => string.Equals(field.Name, "ReturnValues", StringComparison.OrdinalIgnoreCase);

    private static string ReadString(JsonNode? value)
        => value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text) ? text : string.Empty;

    private static bool IsVariableName(string value) => VariableNameRegex().IsMatch(value);

    private static void AddVariable(
        IDictionary<string, VariableAccumulator> variables,
        string name,
        bool isDeclared = false,
        string dataType = "object",
        WorkflowVariable? declaration = null)
    {
        name = name.Trim();
        if (!WorkflowVariableNaming.IsVariable(name))
        {
            return;
        }

        if (!variables.TryGetValue(name, out var variable))
        {
            variable = new VariableAccumulator(name);
            variables.Add(name, variable);
        }

        variable.IsDeclared |= isDeclared;
        if (variable.DataType == "object" && dataType != "object")
        {
            variable.DataType = dataType;
        }

        variable.Declaration ??= declaration;
    }

    private static MethodVariableOverviewItem CreateOverviewItem(VariableAccumulator variable)
        => variable.Declaration != null
            ? new MethodVariableOverviewItem(variable.Declaration)
            : new MethodVariableOverviewItem
            {
                VariableName = variable.Name,
                DataType = variable.DataType,
                IsDeclared = false
            };

    private static string NormalizeDataType(string? valueType)
        => valueType?.ToLowerInvariant() switch
        {
            "boolean" => "boolean",
            "integer" => "integer",
            "number" => "number",
            "string" => "string",
            "array" => "array",
            "image" => "image",
            _ => "object"
        };

    private static string GetVariableFieldDataType(WorkflowActionFieldDto field)
    {
        var createKind = field.EditorOptions?.CreateKind;
        if (!string.IsNullOrWhiteSpace(createKind))
        {
            var parts = createKind.Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && string.Equals(parts[0], "variable", StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeDataType(parts[1]);
            }

            if (string.Equals(createKind, ThreadTaskVariables.CreateKindName, StringComparison.OrdinalIgnoreCase))
            {
                return ThreadTaskVariables.DataTypeName;
            }
        }

        // The property contains a variable name as text; it does not imply that the
        // selected variable's runtime value is a string.
        return "object";
    }

    private sealed class VariableAccumulator(string name)
    {
        public string Name { get; } = name;
        public bool IsDeclared { get; set; }
        public string DataType { get; set; } = "object";
        public WorkflowVariable? Declaration { get; set; }
    }

    [GeneratedRegex("^\\s*[A-Za-z_][A-Za-z0-9_.$]*\\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex VariableNameRegex();

    [GeneratedRegex("\\{(?<name>[A-Za-z_][A-Za-z0-9_.$-]*)\\}", RegexOptions.CultureInvariant)]
    private static partial Regex BracedVariableRegex();

    [GeneratedRegex("[A-Za-z_][A-Za-z0-9_.$]*", RegexOptions.CultureInvariant)]
    private static partial Regex BareVariableRegex();
}
