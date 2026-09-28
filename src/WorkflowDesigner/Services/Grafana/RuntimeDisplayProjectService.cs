using WorkflowCore.WpfDemo.Editor;

namespace WorkflowCore.WpfDemo.Services.Grafana;

public interface IRuntimeDisplayProjectService
{
    Task<RuntimeDisplayDefinition> CreateAsync(
        WorkflowProject project, string name, CancellationToken cancellationToken = default);
    Task RenameAsync(
        WorkflowProject project, RuntimeDisplayDefinition runtimeDisplay, string name,
        CancellationToken cancellationToken = default);
    Task DeleteAsync(
        WorkflowProject project, RuntimeDisplayDefinition runtimeDisplay,
        CancellationToken cancellationToken = default);
}

public sealed class RuntimeDisplayProjectService : IRuntimeDisplayProjectService
{
    private readonly IGrafanaDashboardClient _grafana;

    public RuntimeDisplayProjectService(IGrafanaDashboardClient grafana)
    {
        _grafana = grafana ?? throw new ArgumentNullException(nameof(grafana));
    }

    public async Task<RuntimeDisplayDefinition> CreateAsync(
        WorkflowProject project, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        var normalizedName = ValidateUniqueName(project, name);
        var dashboard = await _grafana.CreateDashboardAsync(normalizedName, cancellationToken).ConfigureAwait(false);
        var definition = new RuntimeDisplayDefinition
        {
            RuntimeDisplayId = Guid.NewGuid(),
            Name = normalizedName,
            Provider = RuntimeDisplayProvider.Grafana,
            GrafanaDashboardUid = dashboard.Uid
        };
        project.RuntimeDisplays.Add(definition);
        return definition;
    }

    public async Task RenameAsync(
        WorkflowProject project, RuntimeDisplayDefinition runtimeDisplay, string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(runtimeDisplay);
        if (!project.RuntimeDisplays.Contains(runtimeDisplay))
            throw new InvalidOperationException("Runtime Display does not belong to the current Project.");
        var normalizedName = ValidateUniqueName(project, name, runtimeDisplay.RuntimeDisplayId);
        await _grafana.UpdateDashboardAsync(
            runtimeDisplay.GrafanaDashboardUid, normalizedName, cancellationToken).ConfigureAwait(false);
        runtimeDisplay.Name = normalizedName;
    }

    public async Task DeleteAsync(
        WorkflowProject project, RuntimeDisplayDefinition runtimeDisplay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(runtimeDisplay);
        if (!project.RuntimeDisplays.Contains(runtimeDisplay)) return;
        await _grafana.DeleteDashboardAsync(runtimeDisplay.GrafanaDashboardUid, cancellationToken)
            .ConfigureAwait(false);
        project.RuntimeDisplays.Remove(runtimeDisplay);
    }

    private static string ValidateUniqueName(
        WorkflowProject project, string name, Guid? exceptRuntimeDisplayId = null)
    {
        var normalizedName = name?.Trim() ?? string.Empty;
        if (normalizedName.Length == 0) throw new ArgumentException("Runtime Display name is required.", nameof(name));
        if (project.RuntimeDisplays.Any(display => display.RuntimeDisplayId != exceptRuntimeDisplayId
            && string.Equals(display.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Runtime Display '{normalizedName}' already exists.");
        return normalizedName;
    }
}
