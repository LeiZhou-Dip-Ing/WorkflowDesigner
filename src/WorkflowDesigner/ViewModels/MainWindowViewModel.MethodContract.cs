using System.ComponentModel;
using System.Text.Json.Nodes;
using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Services.Ui;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly Dictionary<Guid, string> _methodParameterNames = new();

    private void AddMethodInput() => AddMethodParameter(isInput: true);

    private void AddMethodOutput() => AddMethodParameter(isInput: false);

    private void AddMethodParameter(bool isInput)
    {
        if (SelectedMethod == null) return;
        _documents.BeginEdit(SelectedMethod);
        var existingNames = SelectedMethod.Inputs.Concat(SelectedMethod.Outputs)
            .Select(parameter => parameter.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var baseName = isInput ? "input" : "result";
        var name = baseName;
        var suffix = 2;
        while (existingNames.Contains(name)) name = $"{baseName}{suffix++}";

        var variableName = (isInput
            ? WorkflowVariableNaming.LocalDeterminedPrefix
            : WorkflowVariableNaming.LocalInternalPrefix) + name;
        var variable = SelectedMethod.MethodVariables.FirstOrDefault(candidate =>
            string.Equals(candidate.VariableName, variableName, StringComparison.OrdinalIgnoreCase));
        if (variable == null)
        {
            variable = new WorkflowVariable
            {
                VariableName = variableName,
                DataType = "object",
                OrderIndex = SelectedMethod.MethodVariables.Count
            };
            SelectedMethod.MethodVariables.Add(variable);
        }

        var parameter = new WorkflowMethodParameter
        {
            Name = name,
            DisplayName = name,
            VariableName = variableName,
            ValueType = variable.DataType,
            DefaultValue = variable.DefaultValue,
            Description = variable.Description ?? string.Empty,
            Required = isInput,
            Order = isInput ? SelectedMethod.Inputs.Count : SelectedMethod.Outputs.Count
        };
        (isInput ? SelectedMethod.Inputs : SelectedMethod.Outputs).Add(parameter);
        _documents.CompleteEdit(SelectedMethod);
        RefreshSelectedMethodVariables();
        if (isInput) SelectedMethodInput = parameter;
        else SelectedMethodOutput = parameter;
        RefreshActionProperties();
        RefreshJsonPreview();
    }

    private void DeleteMethodInput() => DeleteMethodParameter(SelectedMethodInput, isInput: true);

    private void DeleteMethodOutput() => DeleteMethodParameter(SelectedMethodOutput, isInput: false);

    private void DeleteMethodParameter(WorkflowMethodParameter? parameter, bool isInput)
    {
        if (SelectedMethod == null || parameter == null) return;
        _documents.BeginEdit(SelectedMethod);
        if (isInput)
        {
            RemoveInputMappings(SelectedMethod, parameter.Name);
        }
        else
        {
            var outputIndex = SelectedMethod.Outputs
                .OrderBy(output => output.Order)
                .Select((output, index) => (output, index))
                .First(item => item.output.Uid == parameter.Uid)
                .index;
            RemoveOutputMappings(SelectedMethod, outputIndex);
        }

        (isInput ? SelectedMethod.Inputs : SelectedMethod.Outputs).Remove(parameter);
        var parameters = isInput ? SelectedMethod.Inputs : SelectedMethod.Outputs;
        for (var index = 0; index < parameters.Count; index++)
        {
            parameters[index].Order = index;
        }
        _documents.CompleteEdit(SelectedMethod);
        RefreshSelectedMethodContract();
        RefreshActionProperties();
        RefreshJsonPreview();
    }

    private void RefreshSelectedMethodContract()
    {
        var selectedInputUid = SelectedMethodInput?.Uid;
        var selectedOutputUid = SelectedMethodOutput?.Uid;
        foreach (var parameter in SelectedMethodInputs.Concat(SelectedMethodOutputs))
        {
            parameter.PropertyChanged -= OnMethodParameterPropertyChanged;
        }

        SelectedMethodInputs.Clear();
        SelectedMethodOutputs.Clear();
        _methodParameterNames.Clear();
        if (SelectedMethod == null)
        {
            SelectedMethodInput = null;
            SelectedMethodOutput = null;
            return;
        }

        foreach (var input in SelectedMethod.Inputs.OrderBy(parameter => parameter.Order))
        {
            input.PropertyChanged += OnMethodParameterPropertyChanged;
            SelectedMethodInputs.Add(input);
            _methodParameterNames[input.Uid] = input.Name;
        }

        foreach (var output in SelectedMethod.Outputs.OrderBy(parameter => parameter.Order))
        {
            output.PropertyChanged += OnMethodParameterPropertyChanged;
            SelectedMethodOutputs.Add(output);
            _methodParameterNames[output.Uid] = output.Name;
        }

        SelectedMethodInput = selectedInputUid.HasValue
            ? SelectedMethodInputs.FirstOrDefault(parameter => parameter.Uid == selectedInputUid)
              ?? SelectedMethodInputs.FirstOrDefault()
            : SelectedMethodInputs.FirstOrDefault();
        SelectedMethodOutput = selectedOutputUid.HasValue
            ? SelectedMethodOutputs.FirstOrDefault(parameter => parameter.Uid == selectedOutputUid)
              ?? SelectedMethodOutputs.FirstOrDefault()
            : SelectedMethodOutputs.FirstOrDefault();
    }

    private void OnMethodParameterPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (SelectedMethod == null || sender is not WorkflowMethodParameter parameter) return;
        var refreshVariableList = false;
        if (eventArgs.PropertyName == nameof(WorkflowMethodParameter.Name))
        {
            var oldName = _methodParameterNames.GetValueOrDefault(parameter.Uid) ?? parameter.Name;
            var isInput = SelectedMethod.Inputs.Contains(parameter);
            var parameters = SelectedMethod.Inputs.Concat(SelectedMethod.Outputs);
            if (string.IsNullOrWhiteSpace(parameter.Name)
                || parameters.Any(candidate => candidate.Uid != parameter.Uid
                                               && string.Equals(candidate.Name, parameter.Name, StringComparison.OrdinalIgnoreCase)))
            {
                parameter.Name = oldName;
                StatusText = "Method input/output names must be non-empty and unique.";
                return;
            }

            var desiredVariableName = (isInput
                ? WorkflowVariableNaming.LocalDeterminedPrefix
                : WorkflowVariableNaming.LocalInternalPrefix) + parameter.Name;
            if (!string.Equals(parameter.VariableName, desiredVariableName, StringComparison.OrdinalIgnoreCase))
            {
                var conflict = SelectedMethod.MethodVariables.Any(variable =>
                    string.Equals(variable.VariableName, desiredVariableName, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(variable.VariableName, parameter.VariableName, StringComparison.OrdinalIgnoreCase));
                if (conflict)
                {
                    parameter.Name = oldName;
                    StatusText = $"Variable '{desiredVariableName}' already exists in this method.";
                    return;
                }

                var oldVariableName = parameter.VariableName;
                _variables.Rename(
                    Project,
                    SelectedMethod,
                    oldVariableName,
                    desiredVariableName,
                    acrossAllMethods: false,
                    FindActionDescriptor);
                parameter.VariableName = desiredVariableName;
                refreshVariableList = true;
            }

            if (isInput
                && !string.Equals(oldName, parameter.Name, StringComparison.OrdinalIgnoreCase))
            {
                RenameInputMappings(SelectedMethod, oldName, parameter.Name);
            }

            if (string.IsNullOrWhiteSpace(parameter.DisplayName)
                || string.Equals(parameter.DisplayName, oldName, StringComparison.Ordinal))
            {
                parameter.DisplayName = parameter.Name;
            }

            _methodParameterNames[parameter.Uid] = parameter.Name;
            if (refreshVariableList)
            {
                var methodUid = SelectedMethod.Uid;
                _uiDispatcher.Post(
                    () =>
                    {
                        if (SelectedMethod?.Uid == methodUid)
                        {
                            RefreshSelectedMethodVariables();
                        }
                    },
                    UiDispatchPriority.DataBinding);
            }
        }

        var variable = SelectedMethod.MethodVariables.FirstOrDefault(candidate =>
            string.Equals(candidate.VariableName, parameter.VariableName, StringComparison.OrdinalIgnoreCase));
        if (variable != null)
        {
            variable.DataType = string.IsNullOrWhiteSpace(parameter.ValueType) ? "object" : parameter.ValueType;
            variable.DefaultValue = parameter.DefaultValue;
            variable.Description = parameter.Description;
        }

        PrepareProject();
        RefreshActionProperties();
        RefreshJsonPreview();
        StatusText = "Method input/output contract updated.";
        RaiseCommandStates();
    }

    private void RenameInputMappings(WorkflowMethod target, string oldName, string newName)
    {
        foreach (var action in GetCallsTo(target))
        {
            if (action.GetProperty("Parameters") is not JsonObject parameters)
            {
                continue;
            }

            var propertyName = parameters
                .Select(pair => pair.Key)
                .FirstOrDefault(name => string.Equals(name, oldName, StringComparison.OrdinalIgnoreCase));
            if (propertyName == null)
            {
                continue;
            }

            var normalized = (JsonObject)parameters.DeepClone();
            var value = normalized[propertyName]?.DeepClone();
            normalized.Remove(propertyName);
            normalized[newName] = value;
            action.SetProperty("Parameters", normalized);
        }
    }

    private void RemoveInputMappings(WorkflowMethod target, string inputName)
    {
        foreach (var action in GetCallsTo(target))
        {
            if (action.GetProperty("Parameters") is not JsonObject parameters)
            {
                continue;
            }

            var propertyName = parameters
                .Select(pair => pair.Key)
                .FirstOrDefault(name => string.Equals(name, inputName, StringComparison.OrdinalIgnoreCase));
            if (propertyName == null)
            {
                continue;
            }

            var normalized = (JsonObject)parameters.DeepClone();
            normalized.Remove(propertyName);
            action.SetProperty("Parameters", normalized);
        }
    }

    private void RemoveOutputMappings(WorkflowMethod target, int outputIndex)
    {
        foreach (var action in GetCallsTo(target).Where(action =>
                     string.Equals(action.ActionType, "runMethod", StringComparison.OrdinalIgnoreCase)))
        {
            action.SetProperty(
                "ReturnVarNames",
                JsonValue.Create(RemovePosition(ReadString(action.GetProperty("ReturnVarNames")), outputIndex)));
        }

        foreach (var action in target.MethodLines
                     .Select(line => line.Action)
                     .Where(action => action != null
                                      && string.Equals(action.ActionType, "return", StringComparison.OrdinalIgnoreCase))
                     .Cast<WorkflowAction>())
        {
            action.SetProperty(
                "ReturnValues",
                JsonValue.Create(RemovePosition(ReadString(action.GetProperty("ReturnValues")), outputIndex)));
        }
    }

    private IEnumerable<WorkflowAction> GetCallsTo(WorkflowMethod target)
        => Project.Methods
            .SelectMany(method => method.MethodLines)
            .Select(line => line.Action)
            .Where(action => action != null
                             && (string.Equals(action.ActionType, "runMethod", StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(action.ActionType, "threadStart", StringComparison.OrdinalIgnoreCase))
                             && string.Equals(
                                 ReadString(action.GetProperty("MethodName")),
                                 target.Name,
                                 StringComparison.OrdinalIgnoreCase))
            .Cast<WorkflowAction>();

    private static string RemovePosition(string? value, int index)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var values = value.Split(',').Select(item => item.Trim()).ToList();
        if (index < values.Count)
        {
            values.RemoveAt(index);
        }

        while (values.Count > 0 && values[^1].Length == 0)
        {
            values.RemoveAt(values.Count - 1);
        }

        return string.Join(',', values);
    }

    private static string? ReadString(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
