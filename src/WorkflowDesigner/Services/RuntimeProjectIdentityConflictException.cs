using System.Net;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.Services;

public sealed class RuntimeProjectIdentityConflictException : RuntimeApiException
{
    public RuntimeProjectIdentityConflictException(
        string workflowId,
        Guid requestedProjectId,
        Guid activeProjectId,
        ProjectDeploymentScope deploymentScope,
        string message,
        string? responseBody = null)
        : base(HttpStatusCode.Conflict, responseBody ?? string.Empty, message)
    {
        WorkflowId = workflowId;
        RequestedProjectId = requestedProjectId;
        ActiveProjectId = activeProjectId;
        DeploymentScope = deploymentScope;
        ConflictMessage = message;
    }

    public string WorkflowId { get; }

    public Guid RequestedProjectId { get; }

    public Guid ActiveProjectId { get; }

    public ProjectDeploymentScope DeploymentScope { get; }

    public string ConflictMessage { get; }
}
