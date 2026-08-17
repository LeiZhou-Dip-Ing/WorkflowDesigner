using System.Text.Json.Nodes;
using WorkflowCore.WpfDemo.Editor;
using WorkflowRuntime.ScriptCompiler;

namespace WorkflowCore.WpfDemo.Models;

public sealed class SharpScriptTestValue : EditorObservableObject
{
    private string _valueText;

    public SharpScriptTestValue(SharpScriptFieldContract definition, bool isOutput)
    {
        Definition = definition;
        IsOutput = isOutput;
        _valueText = FormatDefault(definition.DefaultValue);
    }

    public SharpScriptFieldContract Definition { get; }

    public string Name => Definition.Name;

    public string DisplayName => Definition.DisplayName;

    public string Description => Definition.Description;

    public string TypeName => Definition.TypeName;

    public bool IsOutput { get; }

    public string ValueText
    {
        get => _valueText;
        set => SetProperty(ref _valueText, value);
    }

    public void Reset() => ValueText = IsOutput ? string.Empty : FormatDefault(Definition.DefaultValue);

    private static string FormatDefault(JsonNode? value)
    {
        if (value == null) return string.Empty;
        if (value is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<string>(out var text)) return text;
            if (jsonValue.TryGetValue<bool>(out var boolean)) return boolean ? "true" : "false";
            if (jsonValue.TryGetValue<long>(out var integer)) return integer.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (jsonValue.TryGetValue<double>(out var number)) return number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return value.ToJsonString();
    }
}
