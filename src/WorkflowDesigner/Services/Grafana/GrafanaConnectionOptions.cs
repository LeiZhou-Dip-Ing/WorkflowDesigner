using System.Text.Json;
using System.IO;

namespace WorkflowCore.WpfDemo.Services.Grafana;

public sealed class GrafanaConnectionOptions
{
    public string BaseUrl { get; set; } = "http://localhost:3000";
    public string ApiToken { get; set; } = string.Empty;
    public int? OrganizationId { get; set; }
    public string FolderUid { get; set; } = string.Empty;

    public static GrafanaConnectionOptions Load(string baseDirectory)
    {
        var options = new GrafanaConnectionOptions();
        var path = Path.Combine(baseDirectory, "appsettings.json");
        if (File.Exists(path))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("Grafana", out var grafana))
            {
                options = grafana.Deserialize<GrafanaConnectionOptions>(new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? options;
            }
        }

        var token = ResolveApiToken(
            Environment.GetEnvironmentVariable("WORKFLOW_GRAFANA_API_TOKEN", EnvironmentVariableTarget.Process),
            Environment.GetEnvironmentVariable("WORKFLOW_GRAFANA_API_TOKEN", EnvironmentVariableTarget.User));
        if (!string.IsNullOrWhiteSpace(token)) options.ApiToken = token;
        options.Validate();
        return options;
    }

    internal static string? ResolveApiToken(string? processToken, string? userToken)
        => !string.IsNullOrWhiteSpace(processToken) ? processToken : userToken;

    public void Validate()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("Grafana BaseUrl must be an absolute HTTP or HTTPS URL.");
        BaseUrl = BaseUrl.TrimEnd('/');
    }
}
