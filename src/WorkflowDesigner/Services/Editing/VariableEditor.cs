using System.Text.RegularExpressions;
using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Models;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.Services.Editing;

/// <summary>Discovers, validates, and renames variables referenced by one or more workflow methods.</summary>
public sealed partial class VariableEditor : IVariableEditor
{
    public IReadOnlyList<MethodVariableOverviewItem> Discover(
        WorkflowMethod method,
        Func<string, WorkflowActionDescriptorDto?> descriptorResolver)
        => MethodVariableReferences.Discover(method, descriptorResolver);

    public int EnsureDeclarations(
        WorkflowMethod method,
        Func<string, WorkflowActionDescriptorDto?> descriptorResolver)
    {
        ArgumentNullException.ThrowIfNull(method);
        var changes = 0;
        var referenced = MethodVariableReferences.DiscoverReferences(method, descriptorResolver);
        var referencedNames = referenced
            .Select(item => item.VariableName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        referencedNames.UnionWith(method.Inputs.Select(input => input.VariableName));
        referencedNames.UnionWith(method.Outputs.Select(output => output.VariableName));
        var hasUnavailableAction = method.MethodLines
            .Where(line => line.IsActive && line.Action is { IsActive: true })
            .Select(line => line.Action!)
            .Any(action => descriptorResolver(action.ActionType) == null);
        for (var index = method.MethodVariables.Count - 1; !hasUnavailableAction && index >= 0; index--)
        {
            if (referencedNames.Contains(method.MethodVariables[index].VariableName))
            {
                continue;
            }

            method.MethodVariables.RemoveAt(index);
            changes++;
        }

        foreach (var item in referenced)
        {
            var existing = method.MethodVariables.FirstOrDefault(variable => string.Equals(
                    variable.VariableName,
                    item.VariableName,
                    StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                continue;
            }

            var variable = new WorkflowVariable
            {
                VariableName = item.VariableName,
                DataType = item.DataType,
                OrderIndex = method.MethodVariables.Count
            };
            method.MethodVariables.Add(variable);
            changes++;
        }

        foreach (var parameter in method.Inputs.Concat(method.Outputs))
        {
            if (method.MethodVariables.Any(variable => string.Equals(
                    variable.VariableName,
                    parameter.VariableName,
                    StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            method.MethodVariables.Add(new WorkflowVariable
            {
                VariableName = parameter.VariableName,
                DataType = string.IsNullOrWhiteSpace(parameter.ValueType) ? "object" : parameter.ValueType,
                DefaultValue = parameter.DefaultValue,
                Description = parameter.Description,
                OrderIndex = method.MethodVariables.Count
            });
            changes++;
        }

        return changes;
    }

    public int Rename(
        WorkflowProject project,
        WorkflowMethod currentMethod,
        string oldName,
        string newName,
        bool acrossAllMethods,
        Func<string, WorkflowActionDescriptorDto?> descriptorResolver)
        => MethodVariableReferences.Rename(
            project,
            currentMethod,
            oldName,
            newName,
            acrossAllMethods,
            descriptorResolver);

    public bool IsValidName(string name)
        => WorkflowVariableNaming.IsVariable(name?.Trim());

    public string GetUniqueName(WorkflowMethod method, string prefix = "variable")
    {
        ArgumentNullException.ThrowIfNull(method);
        var baseName = WorkflowVariableNaming.GetBaseName(
            string.IsNullOrWhiteSpace(prefix) ? "variable" : prefix.Trim());
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "variable";
        }
        var existing = method.MethodVariables
            .Select(variable => variable.VariableName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var suffix = 1;
        while (existing.Contains($"{WorkflowVariableNaming.LocalInternalPrefix}{baseName}{suffix}"))
        {
            suffix++;
        }

        return $"{WorkflowVariableNaming.LocalInternalPrefix}{baseName}{suffix}";
    }
}
