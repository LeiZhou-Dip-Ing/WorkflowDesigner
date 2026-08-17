using WorkflowDesigner.Contracts;
using System.Text.Json.Nodes;
using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Models;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.Services.Editing;

/// <summary>Maps Action Catalog metadata to editable fields and applies typed property changes.</summary>
public sealed class ActionPropertyEditor : IActionPropertyEditor
{
    private readonly IEditorActionCatalog _catalog;
    private readonly IVariableEditor _variableEditor;

    public ActionPropertyEditor(
        IEditorActionCatalog catalog,
        IVariableEditor variableEditor)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _variableEditor = variableEditor ?? throw new ArgumentNullException(nameof(variableEditor));
    }

    public WorkflowActionDescriptorDto? FindDescriptor(string actionType)
        => _catalog.Current.Actions.FirstOrDefault(
               descriptor => !descriptor.IsDeprecated
                             && string.Equals(descriptor.ActionType, actionType, StringComparison.OrdinalIgnoreCase))
           ?? _catalog.Current.Actions.FirstOrDefault(
               descriptor => string.Equals(descriptor.ActionType, actionType, StringComparison.OrdinalIgnoreCase));

    public WorkflowActionDescriptorDto? FindDescriptor(WorkflowAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!string.IsNullOrWhiteSpace(action.ActionId))
        {
            var byId = _catalog.Current.Actions.FirstOrDefault(
                           descriptor => !descriptor.IsDeprecated
                                         && string.Equals(
                                             GetDescriptorActionId(descriptor),
                                             action.ActionId,
                                             StringComparison.OrdinalIgnoreCase))
                       ?? _catalog.Current.Actions.FirstOrDefault(
                           descriptor => string.Equals(
                               GetDescriptorActionId(descriptor),
                               action.ActionId,
                               StringComparison.OrdinalIgnoreCase));
            if (byId != null)
            {
                return byId;
            }
        }

        var byType = FindDescriptor(action.ActionType);
        if (byType == null)
        {
            return null;
        }

        var actionId = GetDescriptorActionId(byType);
        return _catalog.Current.Actions.FirstOrDefault(
                   descriptor => !descriptor.IsDeprecated
                                 && string.Equals(
                                     GetDescriptorActionId(descriptor),
                                     actionId,
                                     StringComparison.OrdinalIgnoreCase))
               ?? byType;
    }

    public WorkflowAction? CreateDefaultAction(string actionType)
    {
        var descriptor = FindDescriptor(actionType);
        if (descriptor == null)
        {
            return null;
        }

        var action = WorkflowAction.Create(GetDescriptorActionId(descriptor), descriptor.ActionType);
        foreach (var field in descriptor.GetAllFields().Where(field => !field.IsReadOnly && field.DefaultValue != null))
        {
            action.SetProperty(field.Name, field.DefaultValue);
        }

        return action;
    }

    public ActionTemplateItem? FindTemplate(IEnumerable<ActionTemplateItem> toolbox, string actionType)
        => toolbox
            .SelectMany(category => category.Children)
            .FirstOrDefault(item => string.Equals(item.ActionType, actionType, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<ActionPropertyItem> BuildProperties(
        MethodLine line,
        WorkflowMethod method,
        Func<string, WorkflowMethod?> methodResolver,
        Func<string?, IReadOnlyList<string>> suggestionProvider,
        Action valueChanged,
        Action valueChanging)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(methodResolver);
        ArgumentNullException.ThrowIfNull(suggestionProvider);
        ArgumentNullException.ThrowIfNull(valueChanged);
        ArgumentNullException.ThrowIfNull(valueChanging);
        if (line.Action == null)
        {
            return Array.Empty<ActionPropertyItem>();
        }

        var action = line.Action;
        var descriptor = FindDescriptor(action);
        var targetMethod = ResolveTargetMethod(action, line, method, methodResolver);
        var result = new List<ActionPropertyItem>();
        foreach (var property in GetActionFields(descriptor, action)
            .Where(property => !IsEditorCommonActionField(property.Name))
            .OrderBy(property => property.Order))
        {
            if (targetMethod != null && IsMethodParametersField(action, property))
            {
                result.AddRange(CreateMethodParameterItems(
                    action,
                    targetMethod,
                    property.Order,
                    suggestionProvider,
                    valueChanged,
                    valueChanging));
                continue;
            }

            if (targetMethod != null && IsMethodReturnsField(action, property))
            {
                result.AddRange(CreateMethodReturnItems(
                    action,
                    targetMethod,
                    property.Order,
                    suggestionProvider,
                    valueChanged,
                    valueChanging));
                continue;
            }

            result.Add(CreatePropertyItem(
                action,
                descriptor,
                property,
                suggestionProvider,
                valueChanged,
                valueChanging));
        }

        result.Add(ActionPropertyItem.CreateComment(line, valueChanged, valueChanging));
        result.Add(ActionPropertyItem.CreateDeactivate(line, valueChanged, valueChanging));
        return result;
    }

    public void RefreshSuggestions(
        IEnumerable<ActionPropertyItem> properties,
        Func<string?, IReadOnlyList<string>> suggestionProvider)
    {
        foreach (var property in properties)
        {
            property.RefreshSuggestions(suggestionProvider(property.DataSource));
        }
    }

    public bool EnsureStableActionIds(WorkflowProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var changed = false;
        foreach (var action in GetActions(project.Methods))
        {
            var descriptor = FindDescriptor(action);
            if (descriptor == null
                || string.IsNullOrWhiteSpace(descriptor.ActionId)
                || string.Equals(action.ActionId, descriptor.ActionId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            action.ActionId = descriptor.ActionId;
            changed = true;
        }

        return changed;
    }

    public void Normalize(WorkflowEditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Method != null)
        {
            Normalize([document.Method]);
        }
    }

    public void Normalize(IEnumerable<WorkflowMethod> methods)
    {
        foreach (var action in GetActions(methods))
        {
            var descriptor = FindDescriptor(action);
            if (descriptor == null)
            {
                continue;
            }

            action.ActionId = GetDescriptorActionId(descriptor);
            action.ActionType = descriptor.ActionType;
        }
    }

    public PropertyVariableCreationResult CreatePropertyVariable(
        WorkflowMethod method,
        ActionPropertyItem property)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(property);

        var createKindParts = property.CreateKind.Split(':', 2, StringSplitOptions.TrimEntries);
        var createsVariable = string.Equals(createKindParts[0], "variable", StringComparison.OrdinalIgnoreCase);
        var createsTaskVariable = string.Equals(
            property.CreateKind,
            ThreadTaskVariables.CreateKindName,
            StringComparison.OrdinalIgnoreCase);
        if (!createsVariable && !createsTaskVariable)
        {
            return new PropertyVariableCreationResult(false, null, "This property cannot create a variable.");
        }

        var variableName = property.ValueText.Trim();
        if (string.IsNullOrWhiteSpace(variableName))
        {
            variableName = _variableEditor.GetUniqueName(
                method,
                createsTaskVariable ? "taskId" : "variable");
        }

        if (!_variableEditor.IsValidName(variableName))
        {
            return new PropertyVariableCreationResult(false, null, $"'{variableName}' is not a valid variable name.");
        }

        var variable = method.MethodVariables.FirstOrDefault(item =>
            string.Equals(item.VariableName, variableName, StringComparison.OrdinalIgnoreCase));
        if (variable == null)
        {
            variable = new WorkflowVariable
            {
                VariableName = variableName,
                DataType = createsTaskVariable
                    ? ThreadTaskVariables.DataTypeName
                    : createKindParts.Length > 1 && !string.IsNullOrWhiteSpace(createKindParts[1])
                        ? createKindParts[1]
                        : "object",
                OrderIndex = method.MethodVariables.Count
            };
            method.MethodVariables.Add(variable);
        }

        property.ValueText = variableName;
        return new PropertyVariableCreationResult(true, variableName, $"Variable '{variableName}' is ready.");
    }

    private static IEnumerable<WorkflowAction> GetActions(IEnumerable<WorkflowMethod> methods)
        => methods
            .SelectMany(method => method.MethodLines)
            .Select(line => line.Action)
            .Where(action => action != null)
            .Cast<WorkflowAction>();

    private static IReadOnlyList<WorkflowActionFieldDto> GetActionFields(
        WorkflowActionDescriptorDto? descriptor,
        WorkflowAction action)
    {
        var fields = descriptor?.GetAllFields().ToList()
                     ?? new List<WorkflowActionFieldDto>();
        var representedNames = fields
            .Select(field => field.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        fields.AddRange(action.GetEditableProperties()
            .Where(property => !representedNames.Contains(property.Key))
            .Select(property => CreateUnknownField(property.Key, property.Value)));
        return fields;
    }

    private static WorkflowActionFieldDto CreateUnknownField(string name, JsonNode? value)
    {
        var valueType = value switch
        {
            JsonArray => "array",
            JsonObject => "object",
            JsonValue jsonValue when jsonValue.TryGetValue<bool>(out _) => "boolean",
            JsonValue jsonValue when jsonValue.TryGetValue<long>(out _) => "integer",
            JsonValue jsonValue when jsonValue.TryGetValue<double>(out _) => "number",
            _ => "string"
        };

        return new WorkflowActionFieldDto
        {
            Name = name,
            DisplayName = name,
            Description = "Preserved configuration for an Action that is not currently available.",
            ValueType = valueType,
            Category = "Action",
            Editor = valueType is "array" or "object" ? "json" : valueType == "boolean" ? "checkbox" : "text"
        };
    }

    private static ActionPropertyItem CreatePropertyItem(
        WorkflowAction action,
        WorkflowActionDescriptorDto? actionDescriptor,
        WorkflowActionFieldDto field,
        Func<string?, IReadOnlyList<string>> suggestionProvider,
        Action valueChanged,
        Action valueChanging)
    {
        if (field.SupportsOutputBinding && IsOutputField(field))
        {
            return ActionPropertyItem.CreateOutputBinding(
                action,
                field,
                valueChanged,
                suggestionProvider("methodVariables"),
                valueChanging);
        }

        var editorField = field.SupportsVariableExpression && RequiresVariableExpressionEditor(field)
            ? CreateVariableExpressionField(field)
            : field;
        return new ActionPropertyItem(
            action,
            editorField,
            valueChanged,
            suggestionProvider(editorField.EditorOptions?.DataSource),
            valueChanging);
    }

    private static IEnumerable<ActionPropertyItem> CreateMethodParameterItems(
        WorkflowAction action,
        WorkflowMethod targetMethod,
        int order,
        Func<string?, IReadOnlyList<string>> suggestionProvider,
        Action valueChanged,
        Action valueChanging)
    {
        var inputs = targetMethod.Inputs
            .OrderBy(input => input.Order)
            .ThenBy(input => input.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        for (var index = 0; index < inputs.Length; index++)
        {
            var input = inputs[index];
            var inputName = input.Name;
            var variable = targetMethod.MethodVariables.FirstOrDefault(candidate =>
                candidate.IsActive
                && string.Equals(candidate.VariableName, input.VariableName, StringComparison.OrdinalIgnoreCase));
            var valueType = variable?.DataType ?? input.ValueType;
            var defaultValue = variable?.DefaultValue ?? input.DefaultValue;
            var descriptor = new WorkflowActionFieldDto
            {
                Name = $"Parameters.{inputName}",
                DisplayName = string.IsNullOrWhiteSpace(input.DisplayName) ? input.Name : input.DisplayName,
                Description = string.IsNullOrWhiteSpace(input.Description)
                    ? $"Input '{inputName}' ({valueType}) for method '{targetMethod.Name}'. Enter a literal expression or select a caller variable."
                    : input.Description,
                Category = "Action",
                ValueType = "string",
                Direction = "input",
                Required = input.Required,
                Editor = WorkflowPropertyEditorKeys.Variable,
                Order = order,
                EditorOptions = new WorkflowActionEditorOptionsDto
                {
                    DataSource = "methodVariableExpressions",
                    AllowCustomValue = true,
                    AllowCreate = true,
                    AllowClear = true,
                    CreateKind = $"variable:{valueType}",
                    Placeholder = defaultValue == null
                        ? "Enter a value or select a caller variable"
                        : $"Default: {defaultValue}"
                }
            };
            yield return ActionPropertyItem.CreateMappedBinding(
                descriptor,
                () => ReadParameter(action, inputName),
                value => WriteParameter(action, inputName, value),
                valueChanged,
                suggestionProvider("methodVariableExpressions"),
                valueChanging);
        }
    }

    private static IEnumerable<ActionPropertyItem> CreateMethodReturnItems(
        WorkflowAction action,
        WorkflowMethod targetMethod,
        int order,
        Func<string?, IReadOnlyList<string>> suggestionProvider,
        Action valueChanged,
        Action valueChanging)
    {
        var returns = targetMethod.Outputs
            .OrderBy(output => output.Order)
            .ThenBy(output => output.Name, StringComparer.OrdinalIgnoreCase)
            .Select(output =>
            {
                var variable = targetMethod.MethodVariables.FirstOrDefault(candidate =>
                    candidate.IsActive
                    && string.Equals(candidate.VariableName, output.VariableName, StringComparison.OrdinalIgnoreCase));
                return new MethodReturnSlot(
                    output.Name,
                    string.IsNullOrWhiteSpace(output.DisplayName) ? output.Name : output.DisplayName,
                    variable?.DataType ?? output.ValueType,
                    output.Description);
            })
            .ToArray();
        for (var index = 0; index < returns.Length; index++)
        {
            var returnIndex = index;
            var returnSlot = returns[index];
            var descriptor = new WorkflowActionFieldDto
            {
                Name = $"ReturnVarNames.{returnSlot.Name}",
                DisplayName = $"Store {returnSlot.DisplayName} as",
                Description = string.IsNullOrWhiteSpace(returnSlot.Description)
                    ? $"Caller variable that receives output '{returnSlot.Name}' from method '{targetMethod.Name}'."
                    : returnSlot.Description,
                Category = "Action",
                ValueType = "string",
                Direction = "output",
                Required = false,
                Editor = WorkflowPropertyEditorKeys.Variable,
                Order = order,
                EditorOptions = new WorkflowActionEditorOptionsDto
                {
                    DataSource = "methodVariables",
                    AllowCustomValue = true,
                    AllowCreate = true,
                    AllowClear = true,
                    CreateKind = $"variable:{returnSlot.DataType}",
                    Placeholder = "Select or create a caller variable"
                }
            };
            yield return ActionPropertyItem.CreateMappedBinding(
                descriptor,
                () => ReadReturnDestination(action, returnIndex),
                value => WriteReturnDestination(action, returnIndex, returns.Length, value),
                valueChanged,
                suggestionProvider("methodVariables"),
                valueChanging,
                isOutputBinding: true);
        }
    }

    private static WorkflowMethod? ResolveTargetMethod(
        WorkflowAction action,
        MethodLine line,
        WorkflowMethod method,
        Func<string, WorkflowMethod?> methodResolver)
    {
        if (IsActionType(action, "runMethod") || IsActionType(action, "threadStart"))
        {
            return ReadString(action.GetProperty("MethodName")) is { Length: > 0 } methodName
                ? methodResolver(methodName)
                : null;
        }

        return null;
    }

    private static bool IsMethodParametersField(WorkflowAction action, WorkflowActionFieldDto field)
        => string.Equals(field.Name, "Parameters", StringComparison.OrdinalIgnoreCase)
           && (IsActionType(action, "runMethod") || IsActionType(action, "threadStart"));

    private static bool IsMethodReturnsField(WorkflowAction action, WorkflowActionFieldDto field)
        => string.Equals(field.Name, "ReturnVarNames", StringComparison.OrdinalIgnoreCase)
           && IsActionType(action, "runMethod");

    private static bool IsActionType(WorkflowAction action, string actionType)
        => string.Equals(action.ActionType, actionType, StringComparison.OrdinalIgnoreCase);

    private static JsonNode? ReadParameter(WorkflowAction action, string inputName)
        => action.GetProperty("Parameters") is JsonObject parameters
            && parameters.TryGetPropertyValue(inputName, out var value)
                ? value?.DeepClone()
                : null;

    private static void WriteParameter(WorkflowAction action, string inputName, JsonNode? value)
    {
        var parameters = action.GetProperty("Parameters") is JsonObject current
            ? (JsonObject)current.DeepClone()
            : new JsonObject();
        if (string.IsNullOrWhiteSpace(ReadString(value)))
        {            parameters.Remove(inputName);
        }
        else
        {
            parameters[inputName] = value?.DeepClone();
        }

        action.SetProperty("Parameters", parameters);
    }

    private static JsonNode? ReadReturnDestination(WorkflowAction action, int index)
    {
        var values = (ReadString(action.GetProperty("ReturnVarNames")) ?? string.Empty).Split(',');
        return index < values.Length ? JsonValue.Create(values[index].Trim()) : null;
    }

    private static void WriteReturnDestination(
        WorkflowAction action,
        int index,
        int returnCount,
        JsonNode? value)
    {
        var current = (ReadString(action.GetProperty("ReturnVarNames")) ?? string.Empty).Split(',');
        var values = Enumerable.Repeat(string.Empty, Math.Max(returnCount, current.Length)).ToArray();
        for (var currentIndex = 0; currentIndex < current.Length; currentIndex++)
        {
            values[currentIndex] = current[currentIndex].Trim();
        }

        values[index] = ReadString(value)?.Trim() ?? string.Empty;
        var lastValueIndex = Array.FindLastIndex(values, item => item.Length > 0);
        action.SetProperty(
            "ReturnVarNames",
            JsonValue.Create(lastValueIndex < 0 ? string.Empty : string.Join(',', values.Take(lastValueIndex + 1))));
    }

    private static string? ReadString(JsonNode? value)
        => value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text) ? text : null;

    private static bool IsOutputField(WorkflowActionFieldDto field)
        => string.Equals(field.Direction, "output", StringComparison.OrdinalIgnoreCase);

    private static bool RequiresVariableExpressionEditor(WorkflowActionFieldDto field)
    {
        if (!string.Equals(field.Direction, "input", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(field.Direction, "property", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var valueType = field.ValueType.ToLowerInvariant();
        if (valueType is not ("string" or "integer" or "number"))
        {
            return false;
        }

        return !string.Equals(DesignerKeyCompatibility.NormalizePropertyEditor(field.Editor), WorkflowPropertyEditorKeys.Select, StringComparison.OrdinalIgnoreCase)
               && !string.Equals(DesignerKeyCompatibility.NormalizePropertyEditor(field.Editor), WorkflowPropertyEditorKeys.Method, StringComparison.OrdinalIgnoreCase)
               && !string.Equals(DesignerKeyCompatibility.NormalizePropertyEditor(field.Editor), WorkflowPropertyEditorKeys.Variable, StringComparison.OrdinalIgnoreCase)
               && string.IsNullOrWhiteSpace(field.EditorOptions?.DataSource);
    }

    private sealed record MethodReturnSlot(string Name, string DisplayName, string DataType, string Description);

    private static WorkflowActionFieldDto CreateVariableExpressionField(WorkflowActionFieldDto field)
        => new()
        {
            Name = field.Name,
            DisplayName = field.DisplayName,
            Description = string.IsNullOrWhiteSpace(field.Description)
                ? "Enter a literal value or select a method variable."
                : $"{field.Description} Enter a literal value or select a method variable.",
            Order = field.Order,
            ValueType = "string",
            Direction = field.Direction,
            Category = field.Category,
            Required = field.Required,
            IsReadOnly = field.IsReadOnly,
            DefaultValue = field.DefaultValue?.DeepClone(),
            Editor = WorkflowPropertyEditorKeys.Variable,
            EditorOptions = new WorkflowActionEditorOptionsDto
            {
                DataSource = "methodVariableExpressions",
                AllowCustomValue = true,
                AllowCreate = true,
                AllowClear = !field.Required,
                CreateKind = $"variable:{field.ValueType}",
                Placeholder = "Enter a value or select a method variable"
            },
            Minimum = field.Minimum,
            Maximum = field.Maximum,
            Step = field.Step,
            EnumValues = field.EnumValues,
            SupportsVariableExpression = field.SupportsVariableExpression,
            SupportsOutputBinding = field.SupportsOutputBinding
        };

    private static bool IsEditorCommonActionField(string fieldName)
        => string.Equals(fieldName, "Name", StringComparison.OrdinalIgnoreCase)
           || string.Equals(fieldName, "IsActive", StringComparison.OrdinalIgnoreCase)
           || string.Equals(fieldName, "ResultVariables", StringComparison.OrdinalIgnoreCase);

    private static string GetDescriptorActionId(WorkflowActionDescriptorDto descriptor)
        => string.IsNullOrWhiteSpace(descriptor.ActionId) ? descriptor.ActionType : descriptor.ActionId;
}
