using System.Text.Json;
using WorkflowDesigner.WpfSdk;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.ViewModels;

public sealed partial class MainWindowViewModel
{
    internal async Task<WorkflowDesignerCommandResult> RunDesignerCommandAsync(
        WorkflowDesignerCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.CommandId))
            throw new ArgumentException("Designer command id is required.", nameof(request));

        var descriptor = SelectedActionDescriptor
            ?? throw new InvalidOperationException("No Action is selected.");
        var extensionId = descriptor.PluginId
            ?? throw new InvalidOperationException("The selected Action is not owned by an extension.");
        var payload = request.Payload.ToDictionary(
            item => item.Key,
            item => JsonSerializer.SerializeToElement(item.Value),
            StringComparer.OrdinalIgnoreCase);
        var response = await _runtimeApi.ExecuteExtensionCommandAsync(
            new WorkflowExtensionCommandRequestDto
            {
                ExtensionId = extensionId,
                CommandId = request.CommandId,
                TargetActionId = SelectedMethodLine?.Action?.Uid.ToString("D"),
                TargetActionType = descriptor.ActionType,
                Payload = payload
            },
            cancellationToken);
        return new WorkflowDesignerCommandResult(
            response.Succeeded,
            response.Message,
            response.Data.ToDictionary(item => item.Key, item => (object?)item.Value));
    }

    private async Task RunSelectedMethodAsync(
        bool stepMode = false,
        WorkflowDesignerCommandRequest? designerCommand = null)
    {
        if (SelectedMethod == null || IsRunning) return;

        _actionRunLog.Clear();
        ClearDebugLocation();
        Variables.Clear();
        PrepareProject();
        StatusText = stepMode
            ? $"Starting step run for '{SelectedMethod.Name}'..."
            : $"Testing current editor method '{SelectedMethod.Name}' on Runtime...";
        var inputs = SelectedMethod.Inputs
            .OrderBy(input => input.Order)
            .ToDictionary(
                input => input.Name,
                input =>
                {
                    var variable = SelectedMethod.MethodVariables.FirstOrDefault(candidate =>
                        candidate.IsActive
                        && string.Equals(candidate.VariableName, input.VariableName, StringComparison.OrdinalIgnoreCase));
                    return JsonSerializer.SerializeToNode(variable?.Value ?? variable?.DefaultValue ?? input.DefaultValue);
                },
                StringComparer.OrdinalIgnoreCase);
        var workflowJson = SerializeCurrentProjectSnapshot(force: true);
        _ = designerCommand; // Legacy call shape retained; commands no longer mutate workflow JSON.

        var result = await _runSession.RunPreviewAsync(workflowJson, SelectedMethod, inputs, stepMode);
        StatusText = result.Message;
        ClearDebugLocation();
        RefreshSelectedResourcePreview();
        if (!result.Succeeded) _actionRunLog.AddRunFailure(SelectedMethod.Name, result.Message);
    }

}
