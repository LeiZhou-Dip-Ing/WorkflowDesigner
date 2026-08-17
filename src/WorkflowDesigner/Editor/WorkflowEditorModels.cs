using System.Text.Json.Nodes;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.Editor;

public enum WorkflowMethodType
{
    Normal = 0,
    Initialization = 1,
    System = 2
}

public sealed class WorkflowProject
{
    public Guid ProjectId { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "Workflow Project";

    public string Version { get; set; } = "1.0";

    public List<WorkflowMethod> Methods { get; set; } = new();

    public List<WorkflowScript> Scripts { get; set; } = new();

    public List<SharpScriptLibraryReferenceDto> ScriptLibraries { get; set; } = new();

    internal JsonObject ExtensionData { get; set; } = new();

    internal bool ProjectIdWasGenerated { get; set; }

    public WorkflowMethod? FindMethod(string methodUidOrName)
    {
        if (Guid.TryParse(methodUidOrName, out var uid))
        {
            return Methods.FirstOrDefault(method => method.Uid == uid);
        }

        return Methods.FirstOrDefault(method => string.Equals(method.Name, methodUidOrName, StringComparison.OrdinalIgnoreCase));
    }
}

public enum WorkflowEditorDocumentKind
{
    Method,
    CSharpScript
}

public sealed class WorkflowEditorDocument
{
    private WorkflowEditorDocument(
        WorkflowEditorDocumentKind kind,
        WorkflowMethod? method,
        WorkflowScript? script)
    {
        Kind = kind;
        Method = method;
        Script = script;
    }

    public WorkflowEditorDocumentKind Kind { get; }

    public WorkflowMethod? Method { get; }

    public WorkflowScript? Script { get; }

    public string Name => Method?.Name ?? Script?.Name ?? string.Empty;

    public static WorkflowEditorDocument FromMethod(WorkflowMethod method)
        => new(WorkflowEditorDocumentKind.Method, method ?? throw new ArgumentNullException(nameof(method)), null);

    public static WorkflowEditorDocument FromScript(WorkflowScript script)
        => new(WorkflowEditorDocumentKind.CSharpScript, null, script ?? throw new ArgumentNullException(nameof(script)));
}

public sealed class WorkflowScript : EditorObservableObject
{
    private Guid _uid = Guid.NewGuid();
    private string _name = string.Empty;
    private string _language = "CSharp";
    private string _content = string.Empty;

    public Guid Uid { get => _uid; set => SetProperty(ref _uid, value); }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                OnPropertyChanged(nameof(DisplayFileName));
            }
        }
    }

    public string DisplayFileName
        => Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            || Name.EndsWith(".csx", StringComparison.OrdinalIgnoreCase)
                ? Name
                : $"{Name}.csx";

    public string Language { get => _language; set => SetProperty(ref _language, value); }

    public string Content { get => _content; set => SetProperty(ref _content, value); }

    internal JsonObject ExtensionData { get; set; } = new();
}

public sealed class WorkflowMethod : EditorObservableObject
{
    private Guid _uid = Guid.NewGuid();
    private string _name = string.Empty;
    private WorkflowMethodType _methodType;
    private bool _initAtStart;
    private string? _initMethodName;
    private DateTime? _lastExecution;

    public Guid Uid { get => _uid; set => SetProperty(ref _uid, value); }

    public string Name { get => _name; set => SetProperty(ref _name, value); }

    public WorkflowMethodType MethodType { get => _methodType; set => SetProperty(ref _methodType, value); }

    public bool InitAtStart { get => _initAtStart; set => SetProperty(ref _initAtStart, value); }

    public string? InitMethodName { get => _initMethodName; set => SetProperty(ref _initMethodName, value); }

    public DateTime? LastExecution { get => _lastExecution; set => SetProperty(ref _lastExecution, value); }

    public List<MethodLine> MethodLines { get; set; } = new();

    public List<WorkflowVariable> MethodVariables { get; set; } = new();

    public List<WorkflowMethodParameter> Inputs { get; set; } = new();

    public List<WorkflowMethodParameter> Outputs { get; set; } = new();

    internal JsonObject ExtensionData { get; set; } = new();
}

public sealed class MethodLine : EditorObservableObject
{
    private Guid _uid = Guid.NewGuid();
    private int _lineNo;
    private int _sequenceNo;
    private int _nestingLevel;
    private bool _isActive = true;
    private string? _comment;
    private WorkflowAction? _action;
    private bool _isActionAvailable = true;
    private string? _actionAvailabilityMessage;

    public Guid Uid { get => _uid; set => SetProperty(ref _uid, value); }

    public int LineNo { get => _lineNo; set => SetProperty(ref _lineNo, value); }

    public int SequenceNo { get => _sequenceNo; set => SetProperty(ref _sequenceNo, value); }

    public int NestingLevel { get => _nestingLevel; set => SetProperty(ref _nestingLevel, value); }

    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }

    public string? Comment { get => _comment; set => SetProperty(ref _comment, value); }

    public WorkflowAction? Action { get => _action; set => SetProperty(ref _action, value); }

    public bool IsActionAvailable { get => _isActionAvailable; set => SetProperty(ref _isActionAvailable, value); }

    public string? ActionAvailabilityMessage
    {
        get => _actionAvailabilityMessage;
        set => SetProperty(ref _actionAvailabilityMessage, value);
    }

    internal JsonObject ExtensionData { get; set; } = new();

    public static MethodLine Create(int lineNo, int nestingLevel, WorkflowAction action, string? comment = null)
        => new()
        {
            LineNo = lineNo,
            SequenceNo = lineNo,
            NestingLevel = nestingLevel,
            Action = action,
            Comment = comment
        };
}

public sealed class WorkflowVariable : EditorObservableObject
{
    private Guid _uid = Guid.NewGuid();
    private string _variableName = string.Empty;
    private object? _value;
    private string _dataType = "object";
    private bool _isActive = true;
    private string? _description;
    private string? _requestText;
    private int _orderIndex;
    private object? _defaultValue;
    private bool _minCheck;
    private double _minValue;
    private bool _maxCheck;
    private double _maxValue;
    private string? _pickList;
    private bool _dataIsArray;
    private int _arrayLengthRefToOrder;

    public Guid Uid { get => _uid; set => SetProperty(ref _uid, value); }
    public string VariableName { get => _variableName; set => SetProperty(ref _variableName, value); }
    public object? Value { get => _value; set => SetProperty(ref _value, value); }
    public string DataType { get => _dataType; set => SetProperty(ref _dataType, value); }
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }
    public string? Description { get => _description; set => SetProperty(ref _description, value); }
    public string? RequestText { get => _requestText; set => SetProperty(ref _requestText, value); }
    public int OrderIndex { get => _orderIndex; set => SetProperty(ref _orderIndex, value); }
    public object? DefaultValue { get => _defaultValue; set => SetProperty(ref _defaultValue, value); }
    public bool MinCheck { get => _minCheck; set => SetProperty(ref _minCheck, value); }
    public double MinValue { get => _minValue; set => SetProperty(ref _minValue, value); }
    public bool MaxCheck { get => _maxCheck; set => SetProperty(ref _maxCheck, value); }
    public double MaxValue { get => _maxValue; set => SetProperty(ref _maxValue, value); }
    public string? PickList { get => _pickList; set => SetProperty(ref _pickList, value); }
    public bool DataIsArray { get => _dataIsArray; set => SetProperty(ref _dataIsArray, value); }
    public int ArrayLengthRefToOrder { get => _arrayLengthRefToOrder; set => SetProperty(ref _arrayLengthRefToOrder, value); }

    public WorkflowVariableScope VariableScope => WorkflowVariableNaming.GetScope(VariableName);
    public string Label => WorkflowVariableNaming.GetBaseName(VariableName);
    internal JsonObject ExtensionData { get; set; } = new();
}

public sealed class WorkflowAction
{
    private const string OutputBindingsPropertyName = "outputBindings";
    private readonly JsonObject _document;

    public WorkflowAction(JsonObject document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        if (Uid == Guid.Empty) Uid = Guid.NewGuid();
        if (!_document.ContainsKey("name")) Name = string.Empty;
        if (!_document.ContainsKey("isActive")) IsActive = true;
    }

    public Guid Uid
    {
        get => Guid.TryParse(_document["uid"]?.GetValue<string>(), out var uid) ? uid : Guid.Empty;
        set => _document["uid"] = value.ToString();
    }

    public string ActionType
    {
        get => _document["actionType"]?.GetValue<string>() ?? string.Empty;
        set => _document["actionType"] = value;
    }

    public string ActionId
    {
        get => _document["actionId"]?.GetValue<string>() ?? string.Empty;
        set => _document["actionId"] = value;
    }

    public string Name
    {
        get => _document["name"]?.GetValue<string>() ?? string.Empty;
        set => _document["name"] = value;
    }

    public bool IsActive
    {
        get => _document["isActive"]?.GetValue<bool>() ?? true;
        set => _document["isActive"] = value;
    }

    public JsonNode? GetProperty(string name)
        => FindPropertyName(name) is { } propertyName ? _document[propertyName] : null;

    public void SetProperty(string name, JsonNode? value)
    {
        var propertyName = FindPropertyName(name) ?? ToCamelCase(name);
        _document[propertyName] = value?.DeepClone();
    }

    public bool RemoveProperty(string name)
        => FindPropertyName(name) is { } propertyName && _document.Remove(propertyName);

    public string GetOutputBinding(string outputName)
    {
        if (_document[OutputBindingsPropertyName] is not JsonObject bindings)
        {
            return string.Empty;
        }

        var propertyName = bindings
            .Select(pair => pair.Key)
            .FirstOrDefault(name => string.Equals(name, outputName, StringComparison.OrdinalIgnoreCase));
        return propertyName == null
            ? string.Empty
            : bindings[propertyName]?.GetValue<string>() ?? string.Empty;
    }

    public void SetOutputBinding(string outputName, string? variableName)
    {
        var bindings = _document[OutputBindingsPropertyName] as JsonObject;
        if (string.IsNullOrWhiteSpace(variableName))
        {
            if (bindings == null)
            {
                return;
            }

            var propertyName = bindings
                .Select(pair => pair.Key)
                .FirstOrDefault(name => string.Equals(name, outputName, StringComparison.OrdinalIgnoreCase));
            if (propertyName != null)
            {
                bindings.Remove(propertyName);
            }

            if (bindings.Count == 0)
            {
                _document.Remove(OutputBindingsPropertyName);
            }

            return;
        }

        bindings ??= new JsonObject();
        _document[OutputBindingsPropertyName] = bindings;
        var existingName = bindings
            .Select(pair => pair.Key)
            .FirstOrDefault(name => string.Equals(name, outputName, StringComparison.OrdinalIgnoreCase));
        bindings[existingName ?? outputName] = variableName.Trim();
    }

    public IReadOnlyDictionary<string, string> GetOutputBindings()
        => _document[OutputBindingsPropertyName] is JsonObject bindings
            ? bindings
                .Where(pair => pair.Value is JsonValue)
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value?.GetValue<string>() ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<KeyValuePair<string, JsonNode?>> GetEditableProperties()
        => _document.Where(pair => pair.Key is not "actionId" and not "actionType" and not "uid" and not "name" and not "isActive" and not OutputBindingsPropertyName);

    public JsonObject ToJsonObject()
    {
        var result = new JsonObject();
        if (!string.IsNullOrWhiteSpace(ActionId))
        {
            result["actionId"] = ActionId;
        }

        result["actionType"] = ActionType;
        foreach (var pair in _document.Where(pair =>
                     !string.Equals(pair.Key, "actionId", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(pair.Key, "actionType", StringComparison.OrdinalIgnoreCase)))
        {
            result[pair.Key] = pair.Value?.DeepClone();
        }

        return result;
    }

    public static WorkflowAction Create(string actionType)
        => Create(actionType, actionType);

    public static WorkflowAction Create(string actionId, string actionType)
        => new(new JsonObject { ["actionId"] = actionId, ["actionType"] = actionType });

    private string? FindPropertyName(string name)
        => _document.Select(pair => pair.Key).FirstOrDefault(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase));

    private static string ToCamelCase(string value)
        => string.IsNullOrEmpty(value) || char.IsLower(value[0]) ? value : char.ToLowerInvariant(value[0]) + value[1..];
}
