using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Services.Grafana;
using WorkflowCore.WpfDemo.ViewModels;
using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class RuntimeDisplayTests
{
    [Fact]
    public void GrafanaOptions_WhenProcessTokenIsMissing_UsesCurrentUserToken()
    {
        var token = GrafanaConnectionOptions.ResolveApiToken(null, "user-token");

        Assert.Equal("user-token", token);
    }

    [Fact]
    public void EditorBindings_ExposeOnlyProjectGlobalsAndPersistMethodUid()
    {
        var method = new WorkflowMethod { Uid = Guid.NewGuid(), Name = "Start line" };
        method.MethodVariables.Add(new WorkflowVariable { VariableName = "_temperature" });
        method.MethodVariables.Add(new WorkflowVariable { VariableName = "_$localValue" });
        var project = new WorkflowProject { Methods = [method] };
        var display = new RuntimeDisplayDefinition { Name = "Overview" };

        var editor = new RuntimeDisplayEditorViewModel(
            project,
            display,
            new Uri("http://grafana.test/d/overview/_"));

        Assert.Equal("_temperature", Assert.Single(editor.ProjectVariables));
        editor.SelectedProjectVariable = "_temperature";
        editor.AddVariableBindingCommand.Execute(null);
        editor.SelectedMethod = method;
        editor.AddMethodBindingCommand.Execute(null);

        Assert.Equal("temperature", Assert.Single(display.VariableBindings).Alias);
        Assert.Equal(method.Uid, Assert.Single(display.MethodBindings).MethodId);
        Assert.True(editor.IsDirty);
    }

    [Fact]
    public async Task Create_AddsDefinitionOnlyAfterGrafanaReturnsUid()
    {
        var project = new WorkflowProject();
        var grafana = new FakeGrafanaClient { CreatedUid = "dashboard-42" };
        var service = new RuntimeDisplayProjectService(grafana);

        var created = await service.CreateAsync(project, "Production Overview");

        Assert.Same(created, Assert.Single(project.RuntimeDisplays));
        Assert.NotEqual(Guid.Empty, created.RuntimeDisplayId);
        Assert.Equal(RuntimeDisplayProvider.Grafana, created.Provider);
        Assert.Equal("dashboard-42", created.GrafanaDashboardUid);
    }

    [Fact]
    public async Task Create_WhenGrafanaFails_DoesNotLeaveDefinition()
    {
        var project = new WorkflowProject();
        var service = new RuntimeDisplayProjectService(new FakeGrafanaClient
        {
            CreateError = new HttpRequestException("offline")
        });

        await Assert.ThrowsAsync<HttpRequestException>(() => service.CreateAsync(project, "Overview"));

        Assert.Empty(project.RuntimeDisplays);
    }

    [Fact]
    public async Task Delete_RemovesDefinitionAfterGrafanaSucceeds()
    {
        var display = new RuntimeDisplayDefinition
        {
            Name = "Overview",
            GrafanaDashboardUid = "overview"
        };
        var project = new WorkflowProject { RuntimeDisplays = [display] };
        var grafana = new FakeGrafanaClient();
        var service = new RuntimeDisplayProjectService(grafana);

        await service.DeleteAsync(project, display);

        Assert.Empty(project.RuntimeDisplays);
        Assert.Equal("overview", grafana.DeletedUid);
    }

    [Fact]
    public void DashboardUrl_UsesConfiguredBaseUrlAndEscapedUid()
    {
        var builder = new GrafanaDashboardUrlBuilder(new GrafanaConnectionOptions
        {
            BaseUrl = "https://grafana.example.test/root"
        });

        var uri = builder.Build("line/a");

        Assert.Equal("https://grafana.example.test/root/d/line%2Fa/_", uri.AbsoluteUri);
    }

    [Fact]
    public async Task GrafanaClient_Create_UsesDashboardApiAndReadsUid()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"uid\":\"created-dashboard\",\"status\":\"success\"}", Encoding.UTF8, "application/json")
        });
        var client = new GrafanaDashboardClient(
            new GrafanaConnectionOptions { BaseUrl = "http://grafana.test", ApiToken = "token" },
            new HttpClient(handler));

        var created = await client.CreateDashboardAsync("Production Overview");

        Assert.Equal("created-dashboard", created.Uid);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("http://grafana.test/api/dashboards/db", handler.Request.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer", handler.Request.Headers.Authorization?.Scheme);
        Assert.Contains("Production Overview", handler.Body);
    }

    private sealed class FakeGrafanaClient : IGrafanaDashboardClient
    {
        public string CreatedUid { get; init; } = "created";
        public Exception? CreateError { get; init; }
        public string? DeletedUid { get; private set; }

        public Task<GrafanaDashboard> CreateDashboardAsync(string title, CancellationToken cancellationToken = default)
            => CreateError == null
                ? Task.FromResult(new GrafanaDashboard(CreatedUid, title, new JsonObject()))
                : Task.FromException<GrafanaDashboard>(CreateError);

        public Task<GrafanaDashboard> GetDashboardAsync(string dashboardUid, CancellationToken cancellationToken = default)
            => Task.FromResult(new GrafanaDashboard(dashboardUid, dashboardUid, new JsonObject()));

        public Task<GrafanaDashboard> UpdateDashboardAsync(
            string dashboardUid, string title, CancellationToken cancellationToken = default)
            => Task.FromResult(new GrafanaDashboard(dashboardUid, title, new JsonObject()));

        public Task DeleteDashboardAsync(string dashboardUid, CancellationToken cancellationToken = default)
        {
            DeletedUid = dashboardUid;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }
}
