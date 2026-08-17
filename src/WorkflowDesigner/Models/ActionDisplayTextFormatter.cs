using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using WorkflowCore.WpfDemo.Editor;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.Models;

public static partial class ActionDisplayTextFormatter
{
    public static string Format(
        WorkflowActionDescriptorDto? descriptor,
        WorkflowAction action)
    {
        var template = descriptor?.DisplayTemplate;
        if (string.IsNullOrWhiteSpace(template))
        {
            return descriptor?.Description ?? action.ActionType;
        }

        return TemplateToken().Replace(template, match =>
        {
            var name = match.Groups["name"].Value;
            var value = action.GetProperty(name)
                ?? descriptor?.GetAllFields()
                    .FirstOrDefault(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase))
                    ?.DefaultValue;
            return FormatValue(value);
        });
    }

    private static string FormatValue(JsonNode? value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        if (value is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<string>(out var text)) return text;
            if (jsonValue.TryGetValue<bool>(out var boolean)) return boolean ? "true" : "false";
            if (jsonValue.TryGetValue<long>(out var integer)) return integer.ToString(CultureInfo.InvariantCulture);
            if (jsonValue.TryGetValue<double>(out var number)) return number.ToString("G", CultureInfo.InvariantCulture);
        }

        return value.ToJsonString();
    }

    [GeneratedRegex("\\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\\}", RegexOptions.CultureInvariant)]
    private static partial Regex TemplateToken();
}
