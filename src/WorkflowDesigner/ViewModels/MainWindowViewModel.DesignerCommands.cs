using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorkflowDesigner.WpfSdk;

namespace WorkflowCore.WpfDemo.ViewModels;

public sealed partial class MainWindowViewModel
{
    internal Task RunDesignerCommandAsync(WorkflowDesignerCommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.CommandId))
            throw new ArgumentException("Designer command id is required.", nameof(request));

        return RunSelectedMethodAsync(designerCommand: request);
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
        if (designerCommand != null && SelectedMethodLine?.Action is { } selectedAction)
        {
            workflowJson = ApplyDesignerCommandOverrides(
                workflowJson,
                selectedAction.Uid,
                designerCommand.PropertyOverrides);
        }

        var result = await _runSession.RunPreviewAsync(workflowJson, SelectedMethod, inputs, stepMode);
        StatusText = result.Message;
        ClearDebugLocation();
        RefreshSelectedVisionPreview();
        if (!result.Succeeded) _actionRunLog.AddRunFailure(SelectedMethod.Name, result.Message);
    }

    private static string ApplyDesignerCommandOverrides(
        string workflowJson,
        Guid actionUid,
        IReadOnlyDictionary<string, object?> overrides)
    {
        var root = JsonNode.Parse(workflowJson)
            ?? throw new InvalidDataException("The workflow snapshot is empty.");
        var action = FindActionDocument(root, actionUid)
            ?? throw new InvalidOperationException($"Action '{actionUid}' was not found in the execution snapshot.");

        foreach (var (name, value) in overrides)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Designer command property names cannot be empty.", nameof(overrides));

            var existingName = action.Select(pair => pair.Key).FirstOrDefault(
                key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase));
            var propertyName = existingName ?? char.ToLowerInvariant(name[0]) + name[1..];
            action[propertyName] = JsonSerializer.SerializeToNode(value);
        }

        return root.ToJsonString();
    }

    private static JsonObject? FindActionDocument(JsonNode node, Guid actionUid)
    {
        if (node is JsonObject candidate
            && Guid.TryParse(candidate["uid"]?.GetValue<string>(), out var uid)
            && uid == actionUid
            && candidate.ContainsKey("actionType"))
            return candidate;

        var children = node switch
        {
            JsonObject jsonObject => jsonObject.Select(pair => pair.Value),
            JsonArray jsonArray => jsonArray.AsEnumerable(),
            _ => Enumerable.Empty<JsonNode?>()
        };
        foreach (var child in children.Where(value => value != null))
        {
            var result = FindActionDocument(child!, actionUid);
            if (result != null) return result;
        }

        return null;
    }
}
