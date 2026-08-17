using WorkflowRuntime.Application.SharpScripts;

namespace WorkflowRuntime.WindowsService;

/// <summary>Restores active script Actions before published workflows are validated or started.</summary>
public sealed class SharpScriptStartup
{
    private readonly SharpScriptArtifactStore _artifactStore;
    private readonly SharpScriptRuntimeRegistry _runtimeRegistry;
    private readonly ILogger<SharpScriptStartup> _logger;

    public SharpScriptStartup(
        SharpScriptArtifactStore artifactStore,
        SharpScriptRuntimeRegistry runtimeRegistry,
        ILogger<SharpScriptStartup> logger)
    {
        _artifactStore = artifactStore;
        _runtimeRegistry = runtimeRegistry;
        _logger = logger;
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _runtimeRegistry.RestoreAsync(_artifactStore, cancellationToken).ConfigureAwait(false);
        var manifests = _runtimeRegistry.GetActiveManifests();
        _logger.LogInformation(
            "Restored {ScriptCount} published CSharp script Action(s): {Scripts}",
            manifests.Count,
            string.Join(", ", manifests.Select(item => $"{item.ScriptName}@{item.Revision}")));
    }
}
