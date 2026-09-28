using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Http;
using System.IO;
using System.Text.Json.Nodes;

namespace WorkflowCore.WpfDemo.Services.Grafana;

public sealed class GrafanaDashboardClient : IGrafanaDashboardClient
{
    private readonly GrafanaConnectionOptions _options;
    private readonly HttpClient _httpClient;

    public GrafanaDashboardClient(GrafanaConnectionOptions options, HttpClient? httpClient = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.BaseAddress ??= new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        if (!string.IsNullOrWhiteSpace(_options.ApiToken))
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiToken);
        if (_options.OrganizationId is { } organizationId)
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Grafana-Org-Id", organizationId.ToString());
    }

    public async Task<GrafanaDashboard> CreateDashboardAsync(string title, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Dashboard title is required.", nameof(title));
        var dashboard = new JsonObject
        {
            ["id"] = null,
            ["uid"] = null,
            ["title"] = title.Trim(),
            ["tags"] = new JsonArray("workflow-runtime-display"),
            ["timezone"] = "browser",
            ["schemaVersion"] = 39,
            ["version"] = 0,
            ["panels"] = new JsonArray()
        };
        var payload = new JsonObject { ["dashboard"] = dashboard, ["overwrite"] = false };
        if (!string.IsNullOrWhiteSpace(_options.FolderUid)) payload["folderUid"] = _options.FolderUid;
        using var response = await SendAsync(
            () => _httpClient.PostAsJsonAsync("api/dashboards/db", payload, cancellationToken))
            .ConfigureAwait(false);
        var responseDocument = await ReadObjectAsync(response, cancellationToken).ConfigureAwait(false);
        var uid = responseDocument["uid"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(uid)) throw new InvalidDataException("Grafana did not return a dashboard UID.");
        dashboard["uid"] = uid;
        return new GrafanaDashboard(uid, title.Trim(), dashboard);
    }

    public async Task<GrafanaDashboard> GetDashboardAsync(string dashboardUid, CancellationToken cancellationToken = default)
    {
        ValidateUid(dashboardUid);
        using var response = await SendAsync(() => _httpClient.GetAsync(
            $"api/dashboards/uid/{Uri.EscapeDataString(dashboardUid)}", cancellationToken)).ConfigureAwait(false);
        var root = await ReadObjectAsync(response, cancellationToken).ConfigureAwait(false);
        var dashboard = root["dashboard"] as JsonObject
            ?? throw new InvalidDataException("Grafana dashboard response has no dashboard document.");
        return new GrafanaDashboard(dashboardUid, dashboard["title"]?.GetValue<string>() ?? dashboardUid, dashboard);
    }

    public async Task<GrafanaDashboard> UpdateDashboardAsync(
        string dashboardUid, string title, CancellationToken cancellationToken = default)
    {
        var existing = await GetDashboardAsync(dashboardUid, cancellationToken).ConfigureAwait(false);
        var dashboard = (JsonObject)existing.Document.DeepClone();
        dashboard["title"] = title.Trim();
        var payload = new JsonObject { ["dashboard"] = dashboard, ["overwrite"] = true };
        if (!string.IsNullOrWhiteSpace(_options.FolderUid)) payload["folderUid"] = _options.FolderUid;
        using var response = await SendAsync(
            () => _httpClient.PostAsJsonAsync("api/dashboards/db", payload, cancellationToken))
            .ConfigureAwait(false);
        _ = await ReadObjectAsync(response, cancellationToken).ConfigureAwait(false);
        return new GrafanaDashboard(dashboardUid, title.Trim(), dashboard);
    }

    public async Task DeleteDashboardAsync(string dashboardUid, CancellationToken cancellationToken = default)
    {
        ValidateUid(dashboardUid);
        using var response = await SendAsync(() => _httpClient.DeleteAsync(
            $"api/dashboards/uid/{Uri.EscapeDataString(dashboardUid)}", cancellationToken)).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) await ThrowGrafanaErrorAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(Func<Task<HttpResponseMessage>> send)
    {
        try
        {
            return await send().ConfigureAwait(false);
        }
        catch (HttpRequestException error) when (error.StatusCode == null)
        {
            throw new InvalidOperationException(
                $"Grafana Server is not reachable at '{_options.BaseUrl}'. Start Grafana or enable and configure "
                + "WorkflowRuntime:Grafana in the Runtime service settings.",
                error);
        }
    }

    private static async Task<JsonObject> ReadObjectAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode) await ThrowGrafanaErrorAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken)
               ?? throw new InvalidDataException("Grafana returned an empty response.");
    }

    private static async Task ThrowGrafanaErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException(
                "Grafana rejected the request. Configure a Grafana service-account token with dashboard write "
                + "permission in WorkflowDesigner appsettings.json or WORKFLOW_GRAFANA_API_TOKEN.");
        }
        throw new HttpRequestException(
            $"Grafana request failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). {detail}".Trim(),
            null, response.StatusCode);
    }

    private static void ValidateUid(string dashboardUid)
    {
        if (string.IsNullOrWhiteSpace(dashboardUid))
            throw new ArgumentException("Grafana dashboard UID is required.", nameof(dashboardUid));
    }
}
