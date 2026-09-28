using System.Text.Json.Nodes;
using WorkflowRuntime.Application.Storage;

namespace WorkflowRuntime.WindowsService.Grafana;

public sealed class PublishedRuntimeDisplayCatalog
{
    private readonly WorkflowRuntimeOptions _options;
    private readonly PublishedWorkflowStore _publishedWorkflows;

    public PublishedRuntimeDisplayCatalog(
        WorkflowRuntimeOptions options,
        PublishedWorkflowStore publishedWorkflows)
    {
        _options = options;
        _publishedWorkflows = publishedWorkflows;
    }

    public async Task<IReadOnlyList<PublishedRuntimeDisplay>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        JsonNode workflow;
        try
        {
            workflow = await _publishedWorkflows
                .LoadAsync(_options.DefaultWorkflowId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return [];
        }

        if (workflow["runtimeDisplays"] is not JsonArray displays) return [];
        return displays.OfType<JsonObject>()
            .Select(ReadDisplay)
            .Where(display => display != null)
            .Cast<PublishedRuntimeDisplay>()
            .ToArray();
    }

    public async Task<PublishedRuntimeDisplay?> FindAsync(
        Guid runtimeDisplayId,
        CancellationToken cancellationToken = default)
        => (await GetAllAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(display => display.RuntimeDisplayId == runtimeDisplayId);

    private PublishedRuntimeDisplay? ReadDisplay(JsonObject display)
    {
        if (!Guid.TryParse(display["runtimeDisplayId"]?.GetValue<string>(), out var id)) return null;
        var dashboardUid = display["grafanaDashboardUid"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(dashboardUid)) return null;
        return new PublishedRuntimeDisplay(
            id,
            display["name"]?.GetValue<string>() ?? "Runtime Display",
            display["provider"]?.GetValue<string>() ?? "Grafana",
            dashboardUid,
            new Uri($"{_options.Grafana.BaseUrl.TrimEnd('/')}/d/{Uri.EscapeDataString(dashboardUid)}/_"),
            ReadVariableBindings(display["variableBindings"] as JsonArray),
            ReadMethodBindings(display["methodBindings"] as JsonArray));
    }

    private static IReadOnlyList<PublishedRuntimeDisplayVariableBinding> ReadVariableBindings(JsonArray? bindings)
        => bindings?.OfType<JsonObject>()
            .Select(binding => new PublishedRuntimeDisplayVariableBinding(
                binding["alias"]?.GetValue<string>() ?? string.Empty,
                binding["variableName"]?.GetValue<string>() ?? string.Empty))
            .Where(binding => binding.Alias.Length > 0 && binding.VariableName.Length > 0)
            .ToArray() ?? [];

    private static IReadOnlyList<PublishedRuntimeDisplayMethodBinding> ReadMethodBindings(JsonArray? bindings)
        => bindings?.OfType<JsonObject>()
            .Select(binding => new PublishedRuntimeDisplayMethodBinding(
                binding["alias"]?.GetValue<string>() ?? string.Empty,
                Guid.TryParse(binding["methodId"]?.GetValue<string>(), out var methodId)
                    ? methodId
                    : Guid.Empty))
            .Where(binding => binding.Alias.Length > 0 && binding.MethodId != Guid.Empty)
            .ToArray() ?? [];
}

public sealed record PublishedRuntimeDisplay(
    Guid RuntimeDisplayId,
    string Name,
    string Provider,
    string GrafanaDashboardUid,
    Uri DashboardUrl,
    IReadOnlyList<PublishedRuntimeDisplayVariableBinding> VariableBindings,
    IReadOnlyList<PublishedRuntimeDisplayMethodBinding> MethodBindings);

public sealed record PublishedRuntimeDisplayVariableBinding(string Alias, string VariableName);

public sealed record PublishedRuntimeDisplayMethodBinding(string Alias, Guid MethodId);
