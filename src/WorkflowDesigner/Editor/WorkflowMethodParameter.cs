using System.Globalization;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.Editor;

public sealed class WorkflowMethodParameter : EditorObservableObject
{
    private Guid _uid = Guid.NewGuid();
    private string _name = string.Empty;
    private string _variableName = string.Empty;
    private string _displayName = string.Empty;
    private string _description = string.Empty;
    private int _order;
    private string _valueType = "object";
    private bool _required;
    private object? _defaultValue;
    private string _editor = "text";

    public Guid Uid { get => _uid; set => SetProperty(ref _uid, value); }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string VariableName { get => _variableName; set => SetProperty(ref _variableName, value); }
    public string DisplayName { get => _displayName; set => SetProperty(ref _displayName, value); }
    public string Description { get => _description; set => SetProperty(ref _description, value); }
    public int Order { get => _order; set => SetProperty(ref _order, value); }
    public string ValueType
    {
        get => _valueType;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "object" : value.Trim();
            if (!SetProperty(ref _valueType, normalized)) return;

            if (_defaultValue is string text)
            {
                DefaultValue = ParseDefaultValue(text, normalized);
            }

            OnPropertyChanged(nameof(DefaultValueText));
        }
    }
    public bool Required { get => _required; set => SetProperty(ref _required, value); }
    public object? DefaultValue
    {
        get => _defaultValue;
        set
        {
            var normalized = WorkflowValueConverter.TryCoerce(value, ValueType, out var converted)
                ? converted
                : value;
            if (SetProperty(ref _defaultValue, normalized))
            {
                OnPropertyChanged(nameof(DefaultValueText));
            }
        }
    }

    public string DefaultValueText
    {
        get => Convert.ToString(DefaultValue, CultureInfo.InvariantCulture) ?? string.Empty;
        set => DefaultValue = ParseDefaultValue(value, ValueType);
    }

    public string Editor { get => _editor; set => SetProperty(ref _editor, value); }

    private static object? ParseDefaultValue(string? text, string valueType)
        => WorkflowValueConverter.TryCoerce(text, valueType, out var converted)
            ? converted
            : text;
}
