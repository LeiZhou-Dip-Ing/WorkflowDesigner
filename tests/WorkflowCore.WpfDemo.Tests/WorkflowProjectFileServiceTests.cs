using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Services;
using WorkflowCore.WpfDemo.Services.Projects;
using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class WorkflowProjectFileServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"workflow-project-{Guid.NewGuid():N}");

    [Fact]
    public void Create_WritesACompleteCurrentProjectAndOpenReadsIt()
    {
        var service = CreateService();
        var projectPath = Path.Combine(_directory, "Production Line.json");

        var created = service.Create(projectPath);
        var opened = service.Open(projectPath);

        Assert.Equal("Production Line", created.Project.Name);
        Assert.Equal("1.0", opened.Project.Version);
        Assert.NotEqual(Guid.Empty, created.Project.ProjectId);
        Assert.Equal(created.Project.ProjectId, opened.Project.ProjectId);
        Assert.Empty(opened.Project.Methods);
        Assert.Empty(opened.Project.Scripts);
        Assert.Equal(ProjectPathIdentity.Normalize(projectPath), opened.FullPath);
    }

    [Fact]
    public void Open_AssignsAndPersistsAStableProjectIdWhenTheFileHasNone()
    {
        Directory.CreateDirectory(_directory);
        var service = CreateService();
        var projectPath = Path.Combine(_directory, "Existing.json");
        File.WriteAllText(projectPath, """
            {
              "editorSchemaVersion": 2,
              "name": "Existing",
              "version": "1.0",
              "methods": [],
              "scripts": [],
              "scriptLibraries": []
            }
            """);

        var firstOpen = service.Open(projectPath);
        var secondOpen = service.Open(projectPath);

        Assert.NotEqual(Guid.Empty, firstOpen.Project.ProjectId);
        Assert.Equal(firstOpen.Project.ProjectId, secondOpen.Project.ProjectId);
        Assert.Contains(firstOpen.Project.ProjectId.ToString("D"), File.ReadAllText(projectPath));
    }

    [Fact]
    public void Save_ReplacesTheProjectFileWithoutTemporaryArtifacts()
    {
        var service = CreateService();
        var projectPath = Path.Combine(_directory, "Line.json");
        var opened = service.Create(projectPath);
        opened.Project.Methods.Add(new WorkflowMethod { Name = "Main" });

        service.Save(projectPath, opened.Project);

        Assert.Equal("Main", Assert.Single(service.Open(projectPath).Project.Methods).Name);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static WorkflowProjectFileService CreateService()
    {
        var serializer = new WorkflowEditorJsonSerializer();
        return new WorkflowProjectFileService(new JsonEditorDocumentPersistence(serializer));
    }
}
