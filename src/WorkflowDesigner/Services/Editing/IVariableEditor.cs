using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Models;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.Services.Editing;

public interface IVariableEditor
{
    IReadOnlyList<MethodVariableOverviewItem> Discover(
        WorkflowMethod method,
        Func<string, WorkflowActionDescriptorDto?> descriptorResolver);

    int EnsureDeclarations(
        WorkflowMethod method,
        Func<string, WorkflowActionDescriptorDto?> descriptorResolver);

    int Rename(
        WorkflowProject project,
        WorkflowMethod currentMethod,
        string oldName,
        string newName,
        bool acrossAllMethods,
        Func<string, WorkflowActionDescriptorDto?> descriptorResolver);

    bool IsValidName(string name);

    string GetUniqueName(WorkflowMethod method, string prefix = "variable");
}
