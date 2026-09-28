namespace WorkflowCore.WpfDemo.Services.Grafana;

public sealed class GrafanaDashboardUrlBuilder
{
    private readonly Uri _baseUri;

    public GrafanaDashboardUrlBuilder(GrafanaConnectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _baseUri = new Uri(options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
    }

    public Uri Build(string dashboardUid)
    {
        if (string.IsNullOrWhiteSpace(dashboardUid))
            throw new ArgumentException("Grafana dashboard UID is required.", nameof(dashboardUid));
        return new Uri(_baseUri, $"d/{Uri.EscapeDataString(dashboardUid)}/_");
    }
}
