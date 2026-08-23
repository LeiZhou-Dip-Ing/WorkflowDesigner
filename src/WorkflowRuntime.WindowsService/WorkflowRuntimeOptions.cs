namespace WorkflowRuntime.WindowsService;

using WorkflowRuntime.Contracts;
using WorkflowRuntime.Application.Runtime;

public sealed class WorkflowRuntimeOptions
{
    public string Url { get; set; } = "http://localhost:5197";

    public bool AllowRemoteAccess { get; set; }

    public string StorageDirectory { get; set; } = "data/workflows";

    public string WorkflowEncryptionKeyId { get; set; } = "primary";

    public string WorkflowEncryptionKeyEnvironmentVariable { get; set; } = "WORKFLOW_RUNTIME_ENCRYPTION_KEY";

    public string PluginDirectory { get; set; } = "plugins";

    public string SharpScriptDirectory { get; set; } = "data/sharp-scripts";

    public string SharpScriptLibraryDirectory { get; set; } = "data/script-libraries";

    public List<string> AllowedNuGetSources { get; set; } = new();

    public long MaximumScriptLibraryBytes { get; set; } = 134_217_728;

    public int SharpScriptExecutionTimeoutSeconds { get; set; } = 300;

    public string DefaultWorkflowId { get; set; } = WorkflowRuntimeDefaults.DefaultWorkflowId;

    public int CompletedRunRetentionMinutes { get; set; } = 30;

    public int MaxRetainedRuns { get; set; } = 1_000;

    public int RunCleanupIntervalSeconds { get; set; } = 60;

    public int RuntimeEventQueueCapacity { get; set; } = RuntimeEventQueue.DefaultCapacity;

    public List<WorkflowAutoStartOptions> AutoStart { get; set; } = new();
}

public sealed class WorkflowAutoStartOptions
{
    public string WorkflowId { get; set; } = string.Empty;

    public string MethodName { get; set; } = "Main";
}
