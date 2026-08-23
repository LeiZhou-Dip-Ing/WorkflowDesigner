using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using WorkflowCore.WpfDemo.Editor;
using WorkflowRuntime.ActionSdk;
using WorkflowDesigner.WpfSdk;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.Models;

public sealed class ActionPropertyItem : INotifyPropertyChanged, IWorkflowPropertyEditorModel
{
    private readonly WorkflowActionFieldDto _descriptor;
    private readonly Func<JsonNode?> _readValue;
    private readonly Action<JsonNode?> _writeValue;
    private readonly Action _valueChanging;
    private readonly Action _valueChanged;
    private string _valueText;
    private bool _booleanValue;
    private string? _selectedValue;
    private string? _validationError;

    public ActionPropertyItem(
        WorkflowAction action,
        WorkflowActionFieldDto descriptor,
        Action valueChanged,
        IEnumerable<string>? suggestions = null,
        Action? valueChanging = null)
        : this(
            descriptor,
            () => action.GetProperty(descriptor.Name),
            value => action.SetProperty(descriptor.Name, value),
            valueChanged,
            suggestions,
            valueChanging)
    {
    }

    private ActionPropertyItem(
        WorkflowActionFieldDto descriptor,
        Func<JsonNode?> readValue,
        Action<JsonNode?> writeValue,
        Action valueChanged,
        IEnumerable<string>? suggestions = null,
        Action? valueChanging = null,
        bool isOutputBinding = false)
    {
        _descriptor = descriptor;
        _readValue = readValue;
        _writeValue = writeValue;
        _valueChanging = valueChanging ?? (() => { });
        _valueChanged = valueChanged;
        Name = descriptor.Name;
        DisplayName = descriptor.DisplayName;
        Description = descriptor.Description;
        Category = string.IsNullOrWhiteSpace(descriptor.Category) ? "Action" : descriptor.Category;
        TypeName = descriptor.ValueType;
        Editor = descriptor.Editor;
        EditorKey = ResolveEditorKey(descriptor);
        Order = descriptor.Order;
        IsReadOnly = descriptor.IsReadOnly;
        Required = descriptor.Required;
        EnumValues = descriptor.EnumValues;
        DataSource = descriptor.EditorOptions?.DataSource ?? string.Empty;
        AllowCustomValue = descriptor.EditorOptions?.AllowCustomValue ?? true;
        AllowCreate = descriptor.EditorOptions?.AllowCreate == true;
        AllowClear = descriptor.EditorOptions?.AllowClear == true;
        CreateKind = descriptor.EditorOptions?.CreateKind ?? string.Empty;
        Placeholder = descriptor.EditorOptions?.Placeholder ?? string.Empty;
        IsOutputBinding = isOutputBinding;
        RefreshSuggestions(suggestions ?? Array.Empty<string>());

        var value = _readValue() ?? descriptor.DefaultValue;
        _valueText = FormatValue(value);
        _booleanValue = ReadBoolean(value);
        _selectedValue = _valueText;
        ValidateRequiredValue();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raised after the edited value has been written back to the underlying Action.
    /// Consumers use this for editors whose remaining fields depend on the committed value.
    /// </summary>
    public event EventHandler? ValueApplied;

    public string Name { get; }

    public string DisplayName { get; }

    public string Description { get; }

    public string Category { get; }

    public string TypeName { get; }

    public string Editor { get; }

    public string EditorKey { get; }

    public int Order { get; }

    public bool IsReadOnly { get; }

    public bool Required { get; }

    public IReadOnlyList<string> EnumValues { get; }

    public ObservableCollection<string> Suggestions { get; } = new();

    System.Collections.IEnumerable IWorkflowPropertyEditorModel.EnumValues => EnumValues;

    System.Collections.IEnumerable IWorkflowPropertyEditorModel.Suggestions => Suggestions;

    public string DataSource { get; }

    public bool AllowCustomValue { get; }

    public bool AllowCreate { get; }

    public bool AllowClear { get; }

    public string CreateKind { get; }

    public string Placeholder { get; }

    public bool IsOutputBinding { get; }

    public bool IsBooleanEditor => string.Equals(EditorKey, WorkflowPropertyEditorKeys.Checkbox, StringComparison.OrdinalIgnoreCase);

    public bool IsSelectionEditor => string.Equals(EditorKey, WorkflowPropertyEditorKeys.Select, StringComparison.OrdinalIgnoreCase);

    public bool IsLookupEditor => string.Equals(EditorKey, WorkflowPropertyEditorKeys.Lookup, StringComparison.OrdinalIgnoreCase)
        || string.Equals(EditorKey, WorkflowPropertyEditorKeys.StrictLookup, StringComparison.OrdinalIgnoreCase)
        || string.Equals(EditorKey, WorkflowPropertyEditorKeys.Variable, StringComparison.OrdinalIgnoreCase)
        || string.Equals(EditorKey, WorkflowPropertyEditorKeys.Expression, StringComparison.OrdinalIgnoreCase)
        || string.Equals(EditorKey, WorkflowPropertyEditorKeys.Method, StringComparison.OrdinalIgnoreCase);

    public bool IsStrictLookupEditor => string.Equals(EditorKey, WorkflowPropertyEditorKeys.StrictLookup, StringComparison.OrdinalIgnoreCase)
        || (IsLookupEditor && !AllowCustomValue);

    public bool IsJsonEditor => string.Equals(EditorKey, WorkflowPropertyEditorKeys.Json, StringComparison.OrdinalIgnoreCase);

    public string ValueText
    {
        get => _valueText;
        set
        {
            if (string.Equals(_valueText, value, StringComparison.Ordinal))
            {
                return;
            }

            _valueText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedSuggestion));
            if (!IsBooleanEditor && !IsSelectionEditor)
            {
                TryApply(out _);
            }
        }
    }

    /// <summary>
    /// Keeps a lookup ComboBox selection synchronized with <see cref="ValueText" /> without
    /// treating the temporary null selection raised while suggestions refresh as a user clear.
    /// </summary>
    public string? SelectedSuggestion
    {
        get => Suggestions.FirstOrDefault(value =>
            string.Equals(value, _valueText, StringComparison.OrdinalIgnoreCase));
        set
        {
            if (value == null || string.Equals(_valueText, value, StringComparison.Ordinal))
            {
                return;
            }

            ValueText = value;
        }
    }

    public bool BooleanValue
    {
        get => _booleanValue;
        set
        {
            if (_booleanValue == value)
            {
                return;
            }

            _booleanValue = value;
            OnPropertyChanged();
            ApplyValue(JsonValue.Create(value));
        }
    }

    public string? SelectedValue
    {
        get => _selectedValue;
        set
        {
            if (string.Equals(_selectedValue, value, StringComparison.Ordinal))
            {
                return;
            }

            _selectedValue = value;
            OnPropertyChanged();
            ApplyValue(value == null ? null : JsonValue.Create(value));
        }
    }

    public string? ValidationError
    {
        get => _validationError;
        private set
        {
            if (string.Equals(_validationError, value, StringComparison.Ordinal))
            {
                return;
            }

            _validationError = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasValidationError));
        }
    }

    public bool HasValidationError => !string.IsNullOrWhiteSpace(ValidationError);

    public void RefreshSuggestions(IEnumerable<string> values)
    {
        var normalized = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (Suggestions.SequenceEqual(normalized, StringComparer.Ordinal))
        {
            return;
        }

        Suggestions.Clear();
        foreach (var value in normalized)
        {
            Suggestions.Add(value);
        }

        OnPropertyChanged(nameof(SelectedSuggestion));
    }

    public void ClearValue()
    {
        if (IsReadOnly || !AllowClear)
        {
            return;
        }

        _valueText = string.Empty;
        _selectedValue = null;
        OnPropertyChanged(nameof(ValueText));
        OnPropertyChanged(nameof(SelectedSuggestion));
        OnPropertyChanged(nameof(SelectedValue));
        ApplyValue(null);
        ValidateRequiredValue();
    }

    public static ActionPropertyItem CreateComment(
        MethodLine line,
        Action valueChanged,
        Action? valueChanging = null)
        => new(
            new WorkflowActionFieldDto
            {
                Name = "Comment",
                DisplayName = "Comment",
                Description = "Optional note shown next to this method line.",
                Category = "General",
                ValueType = "string",
                Direction = "property",
                Editor = "text",
                Order = 100
            },
            () => JsonValue.Create(line.Comment ?? string.Empty),
            value => line.Comment = value?.GetValue<string>(),
            valueChanged,
            valueChanging: valueChanging);

    public static ActionPropertyItem CreateDeactivate(
        MethodLine line,
        Action valueChanged,
        Action? valueChanging = null)
        => new(
            new WorkflowActionFieldDto
            {
                Name = "Deactivate",
                DisplayName = "Deactivate",
                Description = "Skip this action when the workflow runs.",
                Category = "General",
                ValueType = "boolean",
                Direction = "property",
                Editor = "checkbox",
                Order = 101
            },
            () => JsonValue.Create(!line.IsActive || line.Action?.IsActive == false),
            value =>
            {
                var active = !(value?.GetValue<bool>() ?? false);
                line.IsActive = active;
                if (line.Action != null)
                {
                    line.Action.IsActive = active;
                }
            },
            valueChanged,
            valueChanging: valueChanging);

    public static ActionPropertyItem CreateOutputBinding(
        WorkflowAction action,
        WorkflowActionFieldDto output,
        Action valueChanged,
        IEnumerable<string>? suggestions = null,
        Action? valueChanging = null)
        => new(
            CreateOutputBindingDescriptor(output),
            () => JsonValue.Create(action.GetOutputBinding(output.Name)),
            value => action.SetOutputBinding(
                output.Name,
                value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var variableName)
                    ? variableName
                    : null),
            valueChanged,
            suggestions,
            valueChanging,
            isOutputBinding: true);

    public static ActionPropertyItem CreateMappedBinding(
        WorkflowActionFieldDto descriptor,
        Func<JsonNode?> readValue,
        Action<JsonNode?> writeValue,
        Action valueChanged,
        IEnumerable<string>? suggestions = null,
        Action? valueChanging = null,
        bool isOutputBinding = false)
        => new(
            descriptor,
            readValue,
            writeValue,
            valueChanged,
            suggestions,
            valueChanging,
            isOutputBinding);

    public bool TryApply(out string? error)
    {
        error = null;
        if (IsReadOnly)
        {
            return true;
        }

        try
        {
            var value = ParseValue(_valueText, _descriptor);
            ApplyValue(value);
            ValidateRequiredValue();
            error = ValidationError;
            return error == null;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            ValidationError = error;
            return false;
        }
    }

    private void ApplyValue(JsonNode? value)
    {
        if (IsReadOnly)
        {
            return;
        }

        try
        {
            _valueChanging();
            _writeValue(value);
            ValidationError = null;
            _valueChanged();
            ValueApplied?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            ValidationError = exception.Message;
        }
    }

    private void ValidateRequiredValue()
    {
        if (Required && string.IsNullOrWhiteSpace(_valueText))
        {
            ValidationError = $"{DisplayName} is required.";
        }
    }

    private static JsonNode? ParseValue(string text, WorkflowActionFieldDto descriptor)
    {
        var editorKey = DesignerKeyCompatibility.NormalizePropertyEditor(descriptor.Editor);
        if (string.Equals(editorKey, WorkflowPropertyEditorKeys.Variable, StringComparison.OrdinalIgnoreCase)
            || string.Equals(editorKey, WorkflowPropertyEditorKeys.Expression, StringComparison.OrdinalIgnoreCase)
            || string.Equals(descriptor.EditorOptions?.DataSource, "methodVariables", StringComparison.OrdinalIgnoreCase)
            || string.Equals(descriptor.EditorOptions?.DataSource, "methodVariableExpressions", StringComparison.OrdinalIgnoreCase))
        {
            return JsonValue.Create(text);
        }

        var valueType = descriptor.ValueType.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(text) && valueType != "string")
        {
            return null;
        }

        JsonNode? result = valueType switch
        {
            "boolean" => JsonValue.Create(bool.Parse(text)),
            "integer" => JsonValue.Create(long.Parse(text, CultureInfo.InvariantCulture)),
            "number" => JsonValue.Create(double.Parse(text, CultureInfo.InvariantCulture)),
            "array" or "object" => JsonNode.Parse(text),
            _ => JsonValue.Create(text)
        };

        if (result is JsonValue numberValue && numberValue.TryGetValue<double>(out var number))
        {
            if (descriptor.Minimum.HasValue && number < descriptor.Minimum.Value)
            {
                throw new ArgumentOutOfRangeException(descriptor.Name, $"Value must be at least {descriptor.Minimum.Value}.");
            }

            if (descriptor.Maximum.HasValue && number > descriptor.Maximum.Value)
            {
                throw new ArgumentOutOfRangeException(descriptor.Name, $"Value must be at most {descriptor.Maximum.Value}.");
            }
        }

        return result;
    }

    private static string FormatValue(JsonNode? value)
    {
        if (value == null) return string.Empty;
        if (value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text)) return text;
        return value.ToJsonString();
    }

    private static bool ReadBoolean(JsonNode? value)
        => value is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out var boolean) && boolean;

    private static WorkflowActionFieldDto CreateOutputBindingDescriptor(WorkflowActionFieldDto output)
        => new()
        {
            Name = output.Name,
            DisplayName = $"Store {output.DisplayName} as",
            Description = string.IsNullOrWhiteSpace(output.Description)
                ? $"Method variable that receives the {output.DisplayName} output."
                : $"{output.Description} Select or enter the receiving method variable.",
            Order = output.Order,
            ValueType = "string",
            Direction = "output",
            Category = string.IsNullOrWhiteSpace(output.Category) ? "Action" : output.Category,
            Required = false,
            IsReadOnly = false,
            Editor = "variable",
            EditorOptions = new WorkflowActionEditorOptionsDto
            {
                DataSource = "methodVariables",
                AllowCustomValue = true,
                AllowCreate = true,
                AllowClear = true,
                CreateKind = $"variable:{NormalizeDataType(output.ValueType)}",
                Placeholder = "Select or enter an output variable"
            }
        };

    private static string NormalizeDataType(string? valueType)
        => valueType?.ToLowerInvariant() switch
        {
            "boolean" => "boolean",
            "integer" => "integer",
            "number" => "number",
            "string" => "string",
            "array" => "array",
            "image" => "resource",
            _ => "object"
        };

    private static string ResolveEditorKey(WorkflowActionFieldDto descriptor)
    {
        var configured = DesignerKeyCompatibility.NormalizePropertyEditor(descriptor.Editor);
        var hasExplicitEditor = !string.IsNullOrWhiteSpace(descriptor.Editor);
        if (hasExplicitEditor && !string.Equals(configured, WorkflowPropertyEditorKeys.Text, StringComparison.OrdinalIgnoreCase))
        {
            return configured;
        }

        if (!string.IsNullOrWhiteSpace(descriptor.EditorOptions?.DataSource))
        {
            return descriptor.EditorOptions.AllowCustomValue
                ? WorkflowPropertyEditorKeys.Lookup
                : WorkflowPropertyEditorKeys.StrictLookup;
        }

        if (descriptor.EnumValues.Count > 0)
        {
            return WorkflowPropertyEditorKeys.Select;
        }

        return descriptor.ValueType?.Trim().ToLowerInvariant() switch
        {
            "boolean" => WorkflowPropertyEditorKeys.Checkbox,
            "integer" or "number" => WorkflowPropertyEditorKeys.Number,
            "array" or "object" => WorkflowPropertyEditorKeys.Json,
            _ => configured
        };
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
