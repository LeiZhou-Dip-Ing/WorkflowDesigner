using System.IO;

namespace WorkflowCore.WpfDemo.Services.Projects;

public static class ProjectPathIdentity
{
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
        var root = Path.GetPathRoot(fullPath);
        return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : fullPath.TrimEnd(Path.DirectorySeparatorChar);
    }

    public static bool Equals(string left, string right)
        => StringComparer.OrdinalIgnoreCase.Equals(Normalize(left), Normalize(right));
}
