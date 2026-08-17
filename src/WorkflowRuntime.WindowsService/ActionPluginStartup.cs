using WorkflowRuntime.Application.Plugins;

namespace WorkflowRuntime.WindowsService;

public sealed class ActionPluginStartup
{
    private readonly ActionPluginLoader _pluginLoader;
    private readonly WorkflowRuntimeOptions _options;
    private readonly ILogger<ActionPluginStartup> _logger;

    public ActionPluginStartup(
        ActionPluginLoader pluginLoader,
        WorkflowRuntimeOptions options,
        ILogger<ActionPluginStartup> logger)
    {
        _pluginLoader = pluginLoader;
        _options = options;
        _logger = logger;
    }

    public void Load()
    {
        var results = _pluginLoader.LoadFromDirectory(_options.PluginDirectory);
        if (results.Count == 0)
        {
            _logger.LogInformation("No workflow Action plugins found in {PluginDirectory}.", _options.PluginDirectory);
            return;
        }

        var failures = new List<WorkflowActionPluginLoadResult>();
        foreach (var result in results)
        {
            if (!result.Succeeded)
            {
                failures.Add(result);
                _logger.LogError(
                    "Failed to load workflow Action plugin from {AssemblyPath}: {ErrorMessage}",
                    result.AssemblyPath,
                    result.ErrorMessage);
                continue;
            }

            _logger.LogInformation(
                "Loaded workflow Action plugin {PluginId} {PluginVersion} with Actions: {ActionTypes}",
                result.PluginId,
                result.PluginVersion,
                string.Join(", ", result.ActionTypes));
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "Workflow Runtime cannot start because one or more Action plugins are stale, invalid, or incompatible. "
                + "Rebuild and redeploy the plugin assemblies. "
                + string.Join(
                    "; ",
                    failures.Select(result => $"{Path.GetFileName(result.AssemblyPath)}: {result.ErrorMessage}")));
        }
    }
}
