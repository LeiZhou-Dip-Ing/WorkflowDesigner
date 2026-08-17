using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Models;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.Services.Editing;

public interface IActionPropertyEditor
{
    WorkflowActionDescriptorDto? FindDescriptor(string actionType);
    WorkflowActionDescriptorDto? FindDescriptor(WorkflowAction action);
    WorkflowAction? CreateDefaultAction(string actionType);
    ActionTemplateItem? FindTemplate(IEnumerable<ActionTemplateItem> toolbox, string actionType);

    IReadOnlyList<ActionPropertyItem> BuildProperties(
        MethodLine line,
        WorkflowMethod method,
        Func<string, WorkflowMethod?> methodResolver,
        Func<string?, IReadOnlyList<string>> suggestionProvider,
        Action valueChanged,
        Action valueChanging);

    void RefreshSuggestions(
        IEnumerable<ActionPropertyItem> properties,
        Func<string?, IReadOnlyList<string>> suggestionProvider);

    bool EnsureStableActionIds(WorkflowProject project);
    void Normalize(WorkflowEditorDocument document);
    void Normalize(IEnumerable<WorkflowMethod> methods);

    PropertyVariableCreationResult CreatePropertyVariable(
        WorkflowMethod method,
        ActionPropertyItem property);
}

public sealed record PropertyVariableCreationResult(
    bool Succeeded,
    string? VariableName,
    string Message);
