using System.Text.Json.Nodes;
using WorkflowCore.WpfDemo.Services;
using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class LocalDraftStoreTests
{
    [Fact]
    public void MissingDraft_ReturnsNull()
    {
        using var context = new DraftTestContext();

        Assert.Null(context.Store.Load("editor-default"));
    }

    [Fact]
    public async Task SaveAndLoad_PreservesWorkflowAndDirtyState()
    {
        using var context = new DraftTestContext();
        var workflow = JsonNode.Parse("""
            {
              "name": "Offline draft",
              "methods": [
                {
                  "name": "Main",
                  "methodLines": []
                }
              ]
            }
            """)!;
        var savedWorkflow = JsonNode.Parse("""
            {
              "name": "Saved project",
              "methods": []
            }
            """)!;

        await context.Store.SaveAsync("editor-default", workflow, savedWorkflow, isDirty: true);
        var loaded = context.Store.Load("editor-default");

        Assert.NotNull(loaded);
        Assert.Equal("editor-default", loaded.WorkflowId);
        Assert.True(loaded.IsDirty);
        Assert.Equal("Offline draft", loaded.Workflow["name"]?.GetValue<string>());
        Assert.Equal("Main", loaded.Workflow["methods"]?[0]?["name"]?.GetValue<string>());
        Assert.Equal("Saved project", loaded.SavedWorkflow?["name"]?.GetValue<string>());
    }

    [Fact]
    public async Task Save_UsesAnIndependentFileForEachProjectId()
    {
        using var context = new DraftTestContext();
        var firstId = Guid.NewGuid().ToString("D");
        var secondId = Guid.NewGuid().ToString("D");

        await context.Store.SaveAsync(
            firstId,
            JsonNode.Parse("""{"name":"First"}""")!,
            JsonNode.Parse("""{"name":"First"}""")!,
            isDirty: false);
        await context.Store.SaveAsync(
            secondId,
            JsonNode.Parse("""{"name":"Second"}""")!,
            JsonNode.Parse("""{"name":"Second"}""")!,
            isDirty: true);

        Assert.Equal("First", context.Store.Load(firstId)?.Workflow["name"]?.GetValue<string>());
        Assert.Equal("Second", context.Store.Load(secondId)?.Workflow["name"]?.GetValue<string>());
        Assert.Equal(2, System.IO.Directory.GetFiles(context.Directory, "*.draft.json").Length);
    }

    [Fact]
    public void CorruptDraft_IsQuarantinedAndReported()
    {
        using var context = new DraftTestContext();
        var projectId = Guid.NewGuid().ToString("D");
        var draftPath = Path.Combine(context.Directory, projectId + ".draft.json");
        System.IO.Directory.CreateDirectory(context.Directory);
        File.WriteAllText(draftPath, "{ invalid json");

        var exception = Assert.Throws<InvalidDataException>(() => context.Store.Load(projectId));

        Assert.Contains("quarantined", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(draftPath));
        Assert.Single(System.IO.Directory.GetFiles(context.Directory, "*.corrupt-*"));
    }

    private sealed class DraftTestContext : IDisposable
    {
        public DraftTestContext()
        {
            Directory = Path.Combine(Path.GetTempPath(), "workflow-draft-tests", Guid.NewGuid().ToString("N"));
            Store = new LocalDraftStore(Directory);
        }

        public string Directory { get; }

        public LocalDraftStore Store { get; }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, true);
            }
        }
    }
}
