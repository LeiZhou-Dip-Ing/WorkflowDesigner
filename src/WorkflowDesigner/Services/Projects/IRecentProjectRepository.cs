using WorkflowCore.WpfDemo.Models;

namespace WorkflowCore.WpfDemo.Services.Projects;

public interface IRecentProjectRepository
{
    IReadOnlyList<RecentProjectEntry> Load();

    void AddOrUpdate(string fullPath, string displayName, DateTimeOffset lastOpenedAt);

    void Remove(string fullPath);
}
