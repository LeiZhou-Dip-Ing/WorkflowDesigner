using System.Text.Json;
using WorkflowCore.WpfDemo.Services.Projects;
using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class RecentProjectRepositoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"workflow-recents-{Guid.NewGuid():N}");

    [Fact]
    public void AddOrUpdate_NormalizesAndDeduplicatesWindowsPaths()
    {
        var repository = CreateRepository();
        var path = Path.Combine(_directory, "Project.json");
        var firstOpened = new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);
        var lastOpened = firstOpened.AddHours(3);

        repository.AddOrUpdate(path + Path.DirectorySeparatorChar, "Old name", firstOpened);
        repository.AddOrUpdate(path.ToUpperInvariant(), "Current name", lastOpened);

        var project = Assert.Single(repository.Load());
        Assert.Equal(ProjectPathIdentity.Normalize(path), project.FullPath, ignoreCase: true);
        Assert.Equal("Current name", project.DisplayName);
        Assert.Equal(lastOpened, project.LastOpenedAt);
    }

    [Fact]
    public void AddOrUpdate_DeduplicatesRelativeAndAbsolutePaths()
    {
        var repository = CreateRepository();
        var absolutePath = Path.Combine(_directory, "Project.json");
        var relativePath = Path.GetRelativePath(Environment.CurrentDirectory, absolutePath);

        repository.AddOrUpdate(relativePath, "Relative", DateTimeOffset.UtcNow.AddMinutes(-1));
        repository.AddOrUpdate(absolutePath, "Absolute", DateTimeOffset.UtcNow);

        Assert.Equal("Absolute", Assert.Single(repository.Load()).DisplayName);
    }

    [Fact]
    public void AddOrUpdate_DeduplicatesDirectorySeparatorVariants()
    {
        var repository = CreateRepository();
        var path = Path.Combine(_directory, "Nested", "Project.json");

        repository.AddOrUpdate(path, "Backslash", DateTimeOffset.UtcNow.AddMinutes(-1));
        repository.AddOrUpdate(path.Replace('\\', '/'), "Forward slash", DateTimeOffset.UtcNow);

        Assert.Equal("Forward slash", Assert.Single(repository.Load()).DisplayName);
    }

    [Fact]
    public void Load_DamagedJsonReturnsAnEmptyList()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "recent.json"), "{ damaged");

        Assert.Empty(CreateRepository().Load());
    }

    [Fact]
    public void Remove_UsesNormalizedPathIdentity()
    {
        var repository = CreateRepository();
        var path = Path.Combine(_directory, "Project.json");
        repository.AddOrUpdate(path, "Project", DateTimeOffset.UtcNow);

        repository.Remove(path.ToUpperInvariant());

        Assert.Empty(repository.Load());
    }

    [Fact]
    public void StoredJsonIsCompleteAfterRepeatedUpdates()
    {
        var repository = CreateRepository();
        repository.AddOrUpdate(Path.Combine(_directory, "A.json"), "A", DateTimeOffset.UtcNow);
        repository.AddOrUpdate(Path.Combine(_directory, "B.json"), "B", DateTimeOffset.UtcNow.AddMinutes(1));

        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "recent.json")));
        Assert.Equal(2, document.RootElement.GetArrayLength());
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private JsonRecentProjectRepository CreateRepository()
        => new(Path.Combine(_directory, "recent.json"));
}
