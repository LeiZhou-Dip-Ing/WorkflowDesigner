using System.Diagnostics;
using System.IO;
using System.Text.Json;
using WorkflowCore.WpfDemo.Models;

namespace WorkflowCore.WpfDemo.Services.Projects;

public sealed class JsonRecentProjectRepository : IRecentProjectRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _storagePath;
    private readonly object _sync = new();

    public JsonRecentProjectRepository()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WorkflowCore.WpfDemo",
            "recent-projects.json"))
    {
    }

    internal JsonRecentProjectRepository(string storagePath)
    {
        _storagePath = Path.GetFullPath(storagePath);
    }

    public IReadOnlyList<RecentProjectEntry> Load()
    {
        lock (_sync)
        {
            return LoadUnsafe();
        }
    }

    public void AddOrUpdate(string fullPath, string displayName, DateTimeOffset lastOpenedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        var normalizedPath = ProjectPathIdentity.Normalize(fullPath);
        lock (_sync)
        {
            var projects = LoadUnsafe()
                .Where(entry => !ProjectPathIdentity.Equals(entry.FullPath, normalizedPath))
                .Append(new RecentProjectEntry(normalizedPath, displayName.Trim(), lastOpenedAt))
                .OrderByDescending(entry => entry.LastOpenedAt)
                .ToList();
            SaveUnsafe(projects);
        }
    }

    public void Remove(string fullPath)
    {
        var normalizedPath = ProjectPathIdentity.Normalize(fullPath);
        lock (_sync)
        {
            var projects = LoadUnsafe()
                .Where(entry => !ProjectPathIdentity.Equals(entry.FullPath, normalizedPath))
                .ToList();
            SaveUnsafe(projects);
        }
    }

    private IReadOnlyList<RecentProjectEntry> LoadUnsafe()
    {
        try
        {
            if (!File.Exists(_storagePath))
            {
                return Array.Empty<RecentProjectEntry>();
            }

            var records = JsonSerializer.Deserialize<List<RecentProjectEntry>>(
                              File.ReadAllText(_storagePath),
                              JsonOptions)
                          ?? new List<RecentProjectEntry>();
            return records
                .Where(entry => !string.IsNullOrWhiteSpace(entry.FullPath)
                                && !string.IsNullOrWhiteSpace(entry.DisplayName))
                .Select(entry => entry with { FullPath = ProjectPathIdentity.Normalize(entry.FullPath) })
                .GroupBy(entry => entry.FullPath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(entry => entry.LastOpenedAt).First())
                .OrderByDescending(entry => entry.LastOpenedAt)
                .ToList();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            Trace.TraceError($"Could not load recent workflow projects from '{_storagePath}': {exception}");
            return Array.Empty<RecentProjectEntry>();
        }
    }

    private void SaveUnsafe(IReadOnlyList<RecentProjectEntry> projects)
        => AtomicFileWriter.WriteAllText(_storagePath, JsonSerializer.Serialize(projects, JsonOptions));
}
