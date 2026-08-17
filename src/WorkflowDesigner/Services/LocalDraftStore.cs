using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorkflowCore.WpfDemo.Services.Projects;

namespace WorkflowCore.WpfDemo.Services;

public sealed class LocalDraftStore : ILocalDraftStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _draftDirectory;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public LocalDraftStore()
        : this(GetDefaultDraftDirectory())
    {
    }

    internal LocalDraftStore(string draftDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftDirectory);
        _draftDirectory = Path.GetFullPath(draftDirectory);
    }

    public LocalDraftSnapshot? Load(string workflowId)
    {
        var path = GetDraftPath(workflowId);
        if (!File.Exists(path))
        {
            return null;
        }

        return LoadSnapshot(path, ValidateWorkflowId(workflowId));
    }

    public LocalDraftSnapshot? LoadMostRecent()
    {
        if (!Directory.Exists(_draftDirectory))
        {
            return null;
        }

        var candidates = Directory
            .EnumerateFiles(_draftDirectory, "*.draft.json", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToArray();
        InvalidDataException? lastFailure = null;
        foreach (var candidate in candidates)
        {
            try
            {
                return LoadSnapshot(candidate.FullName, expectedWorkflowId: null);
            }
            catch (InvalidDataException exception)
            {
                lastFailure = exception;
            }
        }

        if (lastFailure != null)
        {
            throw lastFailure;
        }

        return null;
    }

    public async Task SaveAsync(
        string workflowId,
        JsonNode workflow,
        JsonNode savedWorkflow,
        bool isDirty,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(savedWorkflow);
        var snapshot = new LocalDraftSnapshot
        {
            WorkflowId = ValidateWorkflowId(workflowId),
            IsDirty = isDirty,
            SavedAtUtc = DateTimeOffset.UtcNow,
            Workflow = workflow.DeepClone(),
            SavedWorkflow = savedWorkflow.DeepClone()
        };
        var destinationPath = GetDraftPath(workflowId);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await AtomicFileWriter.WriteAllTextAsync(
                destinationPath,
                JsonSerializer.Serialize(snapshot, JsonOptions),
                cancellationToken).ConfigureAwait(false);
            Trace.TraceInformation(
                "Saved local workflow draft '{0}' to '{1}'.",
                snapshot.WorkflowId,
                destinationPath);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private string GetDraftPath(string workflowId)
        => Path.Combine(_draftDirectory, ValidateWorkflowId(workflowId) + ".draft.json");

    private LocalDraftSnapshot LoadSnapshot(string path, string? expectedWorkflowId)
    {
        try
        {
            var snapshot = JsonSerializer.Deserialize<LocalDraftSnapshot>(
                               File.ReadAllText(path),
                               JsonOptions)
                           ?? throw new JsonException("The draft file contains no snapshot.");
            ValidateWorkflowId(snapshot.WorkflowId);
            if (!string.IsNullOrWhiteSpace(expectedWorkflowId)
                && !string.Equals(snapshot.WorkflowId, expectedWorkflowId, StringComparison.OrdinalIgnoreCase))
            {
                throw new JsonException(
                    $"Draft identity '{snapshot.WorkflowId}' does not match '{expectedWorkflowId}'.");
            }

            Trace.TraceInformation(
                "Loaded local workflow draft '{0}' from '{1}'.",
                snapshot.WorkflowId,
                path);
            return snapshot;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            var quarantinedPath = QuarantineCorruptDraft(path);
            Trace.TraceError(
                "Could not read local workflow draft '{0}'. It was quarantined as '{1}'. {2}",
                path,
                quarantinedPath,
                exception);
            throw new InvalidDataException(
                $"The local draft was unreadable and has been quarantined as '{quarantinedPath}'.",
                exception);
        }
    }

    private static string QuarantineCorruptDraft(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path)
            ?? throw new IOException($"Draft path '{path}' has no parent directory.");
        var quarantinedPath = Path.Combine(
            directory,
            $"{Path.GetFileName(path)}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}");
        File.Move(path, quarantinedPath);
        return quarantinedPath;
    }

    private static string ValidateWorkflowId(string workflowId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        if (workflowId.Any(character => !(char.IsLetterOrDigit(character) || character is '.' or '_' or '-')))
        {
            throw new ArgumentException("Workflow id may contain only letters, digits, '.', '_' and '-'.", nameof(workflowId));
        }

        return workflowId;
    }

    private static string GetDefaultDraftDirectory()
    {
        var configuredPath = Environment.GetEnvironmentVariable("WORKFLOW_DRAFT_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Gbo",
            "WorkflowCore",
            "Drafts");
    }
}
