using System.Text.Json.Nodes;

namespace WorkflowCore.WpfDemo.Services.Grafana;

public interface IGrafanaDashboardClient
{
    Task<GrafanaDashboard> CreateDashboardAsync(string title, CancellationToken cancellationToken = default);
    Task<GrafanaDashboard> GetDashboardAsync(string dashboardUid, CancellationToken cancellationToken = default);
    Task<GrafanaDashboard> UpdateDashboardAsync(string dashboardUid, string title, CancellationToken cancellationToken = default);
    Task DeleteDashboardAsync(string dashboardUid, CancellationToken cancellationToken = default);
}

public sealed record GrafanaDashboard(string Uid, string Title, JsonObject Document);

public sealed class UnavailableGrafanaDashboardClient : IGrafanaDashboardClient
{
    private static InvalidOperationException Error()
        => new("Grafana is not configured for this Designer workspace.");

    public Task<GrafanaDashboard> CreateDashboardAsync(string title, CancellationToken cancellationToken = default)
        => Task.FromException<GrafanaDashboard>(Error());

    public Task<GrafanaDashboard> GetDashboardAsync(string dashboardUid, CancellationToken cancellationToken = default)
        => Task.FromException<GrafanaDashboard>(Error());

    public Task<GrafanaDashboard> UpdateDashboardAsync(string dashboardUid, string title, CancellationToken cancellationToken = default)
        => Task.FromException<GrafanaDashboard>(Error());

    public Task DeleteDashboardAsync(string dashboardUid, CancellationToken cancellationToken = default)
        => Task.FromException(Error());
}
