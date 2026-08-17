using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using WorkflowCore.WpfDemo.Editor;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.Models;

public sealed class MethodVariableOverviewItem : INotifyPropertyChanged
{
    private readonly WorkflowVariable? _variable;
    private string _variableName = string.Empty;
    private string _dataType = "object";
    private object? _defaultValue;
    private int _requestOrder;
    private string? _requestText;
    private string? _description;
    private bool _minCheck;
    private double _minValue;
    private bool _maxCheck;
    private double _maxValue;
    private string? _pickList;
    private bool _dataIsArray;
    private int _arrayLengthRefToOrder;

    public MethodVariableOverviewItem()
    {
    }

    public MethodVariableOverviewItem(WorkflowVariable variable)
    {
        _variable = variable ?? throw new ArgumentNullException(nameof(variable));
        _variableName = variable.VariableName;
        _dataType = variable.DataType;
        _defaultValue = variable.DefaultValue;
        _requestOrder = variable.OrderIndex;
        _requestText = variable.RequestText;
        _description = variable.Description;
        _minCheck = variable.MinCheck;
        _minValue = variable.MinValue;
        _maxCheck = variable.MaxCheck;
        _maxValue = variable.MaxValue;
        _pickList = variable.PickList;
        _dataIsArray = variable.DataIsArray;
        _arrayLengthRefToOrder = variable.ArrayLengthRefToOrder;
        IsDeclared = true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? ValueChanged;

    public string VariableName
    {
        get => _variable?.VariableName ?? _variableName;
        init => _variableName = value;
    }

    public string Label => WorkflowVariableNaming.GetBaseName(VariableName);

    public WorkflowVariableScope VariableScope => WorkflowVariableNaming.GetScope(VariableName);

    public string ScopeDisplay
        => VariableScope switch
        {
            WorkflowVariableScope.LocalDetermined => "Local",
            WorkflowVariableScope.LocalInternal => "Local (internal)",
            WorkflowVariableScope.GlobalDetermined => "Global",
            WorkflowVariableScope.GlobalInternal => "Global (internal)",
            WorkflowVariableScope.ReturnValue => "Return",
            WorkflowVariableScope.Timer => "Timer",
            _ => "Invalid"
        };

    public bool IsInput
        => WorkflowVariableNaming.IsDetermined(VariableScope);

    public bool IsReturn
        => VariableScope == WorkflowVariableScope.ReturnValue;

    public bool IsDeclared { get; init; }

    public string DataType
    {
        get => _variable?.DataType ?? _dataType;
        set
        {
            if (string.Equals(DataType, value, StringComparison.OrdinalIgnoreCase)) return;
            _dataType = string.IsNullOrWhiteSpace(value) ? "object" : value.Trim();
            if (_variable != null) _variable.DataType = _dataType;
            NotifyChanged();
            OnPropertyChanged(nameof(DefaultValueText));
        }
    }

    public int RequestOrder
    {
        get => _variable?.OrderIndex ?? _requestOrder;
        set
        {
            if (RequestOrder == value) return;
            _requestOrder = value;
            if (_variable != null) _variable.OrderIndex = value;
            NotifyChanged();
        }
    }

    public string RequestText
    {
        get => _variable?.RequestText ?? _requestText ?? string.Empty;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(RequestText, normalized, StringComparison.Ordinal)) return;
            _requestText = normalized;
            if (_variable != null) _variable.RequestText = normalized;
            NotifyChanged();
        }
    }

    public string Description
    {
        get => _variable?.Description ?? _description ?? string.Empty;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(Description, normalized, StringComparison.Ordinal)) return;
            _description = normalized;
            if (_variable != null) _variable.Description = normalized;
            NotifyChanged();
        }
    }

    public string DefaultValueText
    {
        get => Convert.ToString(_variable?.DefaultValue ?? _defaultValue, CultureInfo.InvariantCulture) ?? string.Empty;
        set
        {
            var parsed = ParseDefaultValue(value, DataType);
            if (Equals(_variable?.DefaultValue ?? _defaultValue, parsed)) return;
            _defaultValue = parsed;
            if (_variable != null) _variable.DefaultValue = parsed;
            NotifyChanged();
        }
    }

    public bool MinCheck
    {
        get => _variable?.MinCheck ?? _minCheck;
        set
        {
            if (MinCheck == value) return;
            _minCheck = value;
            if (_variable != null) _variable.MinCheck = value;
            NotifyChanged();
        }
    }

    public double MinValue
    {
        get => _variable?.MinValue ?? _minValue;
        set
        {
            if (MinValue.Equals(value)) return;
            _minValue = value;
            if (_variable != null) _variable.MinValue = value;
            NotifyChanged();
        }
    }

    public bool MaxCheck
    {
        get => _variable?.MaxCheck ?? _maxCheck;
        set
        {
            if (MaxCheck == value) return;
            _maxCheck = value;
            if (_variable != null) _variable.MaxCheck = value;
            NotifyChanged();
        }
    }

    public double MaxValue
    {
        get => _variable?.MaxValue ?? _maxValue;
        set
        {
            if (MaxValue.Equals(value)) return;
            _maxValue = value;
            if (_variable != null) _variable.MaxValue = value;
            NotifyChanged();
        }
    }

    public string PickList
    {
        get => _variable?.PickList ?? _pickList ?? string.Empty;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(PickList, normalized, StringComparison.Ordinal)) return;
            _pickList = normalized;
            if (_variable != null) _variable.PickList = normalized;
            NotifyChanged();
        }
    }

    public bool DataIsArray
    {
        get => _variable?.DataIsArray ?? _dataIsArray;
        set
        {
            if (DataIsArray == value) return;
            _dataIsArray = value;
            if (_variable != null) _variable.DataIsArray = value;
            NotifyChanged();
        }
    }

    public int ArrayLengthRefToOrder
    {
        get => _variable?.ArrayLengthRefToOrder ?? _arrayLengthRefToOrder;
        set
        {
            if (ArrayLengthRefToOrder == value) return;
            _arrayLengthRefToOrder = value;
            if (_variable != null) _variable.ArrayLengthRefToOrder = value;
            NotifyChanged();
        }
    }

    private static object? ParseDefaultValue(string? text, string dataType)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return dataType.ToLowerInvariant() switch
        {
            "boolean" or "bool" when bool.TryParse(text, out var boolean) => boolean,
            "integer" or "int" when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer) => integer,
            "number" or "double" when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => number,
            _ => text
        };
    }

    private void NotifyChanged([CallerMemberName] string? propertyName = null)
    {
        OnPropertyChanged(propertyName);
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
