using System.Net;
using System.Text.Json.Nodes;
using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Models;
using WorkflowCore.WpfDemo.Services.Ui;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.Services.Runtime;

/// <summary>Deploys saved workflows and reconciles local state with the runtime copy.</summary>
public sealed class RuntimeDeployment
{
    private readonly IRuntimeApiClient _runtimeApi;
    private readonly IEditorDocumentPersistence _persistence;
    private readonly IEditorDialogs _dialogs;
    private readonly EditorSession _session;
    private readonly RuntimeWorkspaceSync _workspaceSync;

    public RuntimeDeployment(
        IRuntimeApiClient runtimeApi,
        IEditorDocumentPersistence persistence,
        IEditorDialogs dialogs,
        EditorSession session,
        RuntimeWorkspaceSync workspaceSync)
    {
        _runtimeApi = runtimeApi ?? throw new ArgumentNullException(nameof(runtimeApi));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _workspaceSync = workspaceSync ?? throw new ArgumentNullException(nameof(workspaceSync));
    }

    public WorkflowDeploymentState GetState(bool hasUnsavedLocalChanges)
    {
        var hasUndeployedSavedChanges = _session.RuntimeProjectJson != null
                                        && !JsonDocumentsAreEquivalent(
                                            _session.SavedProjectJson,
                                            _session.RuntimeProjectJson);
        return new WorkflowDeploymentState(hasUnsavedLocalChanges, hasUndeployedSavedChanges);
    }

    public async Task<WorkflowDeployResult> DeployAsync(
        IReadOnlyList<string> unsavedDocumentNames,
        CancellationToken cancellationToken = default)
    {
        if (unsavedDocumentNames.Count > 0)
        {
            _dialogs.ShowWarning(
                "Local changes are not saved",
                "Save all local documents before deploying:\n\n"
                + string.Join(Environment.NewLine, unsavedDocumentNames.Select(name => $"• {name}")));
            return new WorkflowDeployResult(false, false, null, "Deploy blocked because the local Project has unsaved documents.");
        }

        if (string.IsNullOrWhiteSpace(_session.SavedProjectJson))
        {
            return new WorkflowDeployResult(false, false, null, "Save the local Project before deploying.");
        }

        var savedProject = _persistence.Deserialize(_session.SavedProjectJson);
        var activeProject = await RefreshActiveProjectIdentityAsync(cancellationToken).ConfigureAwait(false);

        if (activeProject?.ProjectId == savedProject.ProjectId
            && _session.RuntimeProjectJson != null
            && JsonDocumentsAreEquivalent(_session.SavedProjectJson, _session.RuntimeProjectJson))
        {
            var savedWorkflow = JsonNode.Parse(_session.SavedProjectJson)
                ?? throw new InvalidOperationException("The saved local Project is empty.");
            var verifiedDocument = await VerifyRuntimeProjectAsync(
                    savedProject,
                    savedWorkflow,
                    activeProject.Revision,
                    activeProject.ContentHash,
                    "Project synchronization",
                    cancellationToken)
                .ConfigureAwait(false);
            var message = $"Local Project is already synchronized with Runtime revision {verifiedDocument.Revision}.";
            _dialogs.ShowInformation("Project already synchronized", message);
            return new WorkflowDeployResult(
                false,
                false,
                verifiedDocument,
                message,
                RuntimeMatchesSavedProject: true);
        }

        var replacesDifferentProject = activeProject != null
                                       && activeProject.ProjectId != savedProject.ProjectId;
        var confirmationMessage = replacesDifferentProject
            ? "Workflow Runtime currently contains another Project.\n\n"
              + $"Local Project: {savedProject.Name} ({savedProject.ProjectId:D})\n"
              + $"Runtime Project ID: {activeProject!.ProjectId:D}\n\n"
              + "Continuing will atomically replace the complete Runtime Project. Methods and CSharp scripts "
              + "that belong only to the current Runtime Project will no longer be active."
            : $"Deploy the complete saved local Project to Workflow Runtime?\n\n"
              + $"Project ID: {savedProject.ProjectId:D}\n"
              + $"Current Runtime revision: {(activeProject is { Revision: > 0 } ? activeProject.Revision.ToString() : "none")}\n"
              + "Only saved local changes will be deployed. The Runtime Project will be replaced atomically.";
        var confirmed = _dialogs.Confirm("Deploy complete Project", confirmationMessage);
        if (!confirmed)
        {
            return new WorkflowDeployResult(false, false, null, "Deployment cancelled.");
        }

        var workflow = JsonNode.Parse(_session.SavedProjectJson)
            ?? throw new InvalidOperationException("The saved local Project is empty.");
        await EnsureWorkflowCanPublishAsync(workflow, cancellationToken).ConfigureAwait(false);
        // The publication coordinator validates against candidate registrations for every script
        // in this project. The current Runtime catalog cannot validate a new script action yet.
        WorkflowPublishResponse published;
        try
        {
            published = await _runtimeApi.PublishWorkflowAsync(
                    WorkflowRuntimeDefaults.DefaultWorkflowId,
                    savedProject.ProjectId,
                    ProjectDeploymentScope.CompleteProject,
                    workflow,
                    activeProject?.Revision ?? 0,
                    cancellationToken);
        }
        catch (RuntimeRevisionConflictException conflict)
        {
            _session.RuntimeRevision = conflict.CurrentRevision;
            _session.RuntimeContentHash = conflict.CurrentContentHash;
            var message =
                $"Deploy stopped: Runtime revision {conflict.CurrentRevision} was changed by another client. "
                + "Your local Project was kept unchanged. Use Compare or Download before deploying again.";
            _dialogs.ShowWarning("Runtime revision conflict", message);
            return new WorkflowDeployResult(false, true, null, message);
        }
        var runtimeDocument = await VerifyRuntimeProjectAsync(
                savedProject,
                workflow,
                published.Revision,
                published.ContentHash,
                "Complete Project deployment",
                cancellationToken)
            .ConfigureAwait(false);
        var successMessage = $"Deployed and verified complete Project '{savedProject.Name}' as Runtime revision {published.Revision}.";
        _dialogs.ShowInformation("Project deployment succeeded", successMessage);
        return new WorkflowDeployResult(
            true,
            false,
            runtimeDocument,
            successMessage,
            RuntimeMatchesSavedProject: true);
    }

    /// <summary>
    /// Deploys one saved method by UID while preserving every other document in the Runtime Project.
    /// </summary>
    public async Task<WorkflowDocumentDeployResult> DeployDocumentAsync(
        WorkflowEditorDocument localDocument,
        bool hasUnsavedChanges,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(localDocument);
        if (localDocument.Kind == WorkflowEditorDocumentKind.CSharpScript && localDocument.Script != null)
        {
            return await DeployScriptAsync(localDocument.Script, hasUnsavedChanges, cancellationToken)
                .ConfigureAwait(false);
        }

        if (localDocument.Kind != WorkflowEditorDocumentKind.Method || localDocument.Method == null)
        {
            return new WorkflowDocumentDeployResult(
                false,
                false,
                null,
                "Only method documents can currently be deployed individually.");
        }

        if (hasUnsavedChanges)
        {
            _dialogs.ShowWarning(
                "Method is not saved",
                $"Save method '{localDocument.Name}' locally before deploying it.");
            return new WorkflowDocumentDeployResult(
                false,
                false,
                null,
                $"Deploy blocked because method '{localDocument.Name}' has unsaved changes.");
        }

        if (string.IsNullOrWhiteSpace(_session.SavedProjectJson))
        {
            return new WorkflowDocumentDeployResult(false, false, null, "Save the method locally before deploying it.");
        }

        var savedProject = _persistence.Deserialize(_session.SavedProjectJson);
        var savedMethod = savedProject.Methods.FirstOrDefault(method => method.Uid == localDocument.Method.Uid);
        if (savedMethod == null)
        {
            return new WorkflowDocumentDeployResult(
                false,
                false,
                null,
                $"Saved method UID '{localDocument.Method.Uid}' was not found. Save the method before deploying it.");
        }

        var activeProject = await RequireMatchingActiveProjectAsync(
                savedProject,
                ProjectDeploymentScope.CurrentMethod,
                cancellationToken)
            .ConfigureAwait(false);
        if (activeProject == null)
        {
            var message = CreateProjectMismatchMessage(savedProject, ProjectDeploymentScope.CurrentMethod);
            _dialogs.ShowWarning("Complete Project deployment required", message);
            return new WorkflowDocumentDeployResult(false, false, null, message);
        }

        var runtimeDocument = await _runtimeApi.GetWorkflowAsync(
                WorkflowRuntimeDefaults.DefaultWorkflowId,
                cancellationToken)
            .ConfigureAwait(false);
        EnsureRuntimeDocumentMatches(savedProject, runtimeDocument, ProjectDeploymentScope.CurrentMethod);

        _workspaceSync.ApplyRuntimeSnapshot(runtimeDocument);
        var runtimeProject = _persistence.Deserialize(runtimeDocument.Workflow.ToJsonString());
        var runtimeMethodIndex = runtimeProject.Methods.FindIndex(method => method.Uid == savedMethod.Uid);
        var sameNameMethod = runtimeProject.Methods.FirstOrDefault(method =>
            method.Uid != savedMethod.Uid
            && string.Equals(method.Name, savedMethod.Name, StringComparison.OrdinalIgnoreCase));
        if (sameNameMethod != null)
        {
            var message =
                $"Runtime already contains method '{savedMethod.Name}' with a different UID. "
                + "Individual deploy was stopped to avoid replacing the wrong method. Use Compare or deploy the complete Project.";
            _dialogs.ShowWarning("Method identity conflict", message);
            return new WorkflowDocumentDeployResult(false, false, runtimeDocument, message);
        }

        var confirmed = _dialogs.Confirm(
            "Deploy current method",
            $"Deploy method '{savedMethod.Name}' to Workflow Runtime?\n\n"
            + $"Method UID: {savedMethod.Uid}\n"
            + $"Current Runtime revision: {runtimeDocument.Revision}\n"
            + "Only this method will be replaced or added. Other Runtime methods will remain unchanged.");
        if (!confirmed)
        {
            return new WorkflowDocumentDeployResult(false, false, runtimeDocument, "Method deployment cancelled.");
        }

        if (runtimeMethodIndex >= 0)
        {
            runtimeProject.Methods[runtimeMethodIndex] = savedMethod;
        }
        else
        {
            runtimeProject.Methods.Add(savedMethod);
        }

        var mergedWorkflow = JsonNode.Parse(_persistence.Serialize(runtimeProject))
            ?? throw new InvalidOperationException("The merged Runtime Project is empty.");
        await EnsureWorkflowCanPublishAsync(mergedWorkflow, cancellationToken);
        WorkflowPublishResponse published;
        try
        {
            published = await _runtimeApi.PublishWorkflowAsync(
                    WorkflowRuntimeDefaults.DefaultWorkflowId,
                    savedProject.ProjectId,
                    ProjectDeploymentScope.CurrentMethod,
                    mergedWorkflow,
                    runtimeDocument.Revision,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (RuntimeRevisionConflictException conflict)
        {
            _session.RuntimeRevision = conflict.CurrentRevision;
            _session.RuntimeContentHash = conflict.CurrentContentHash;
            var message =
                $"Method deploy stopped: Runtime revision {conflict.CurrentRevision} was changed by another client. "
                + "Use Compare or Download before deploying again.";
            _dialogs.ShowWarning("Runtime revision conflict", message);
            return new WorkflowDocumentDeployResult(false, true, runtimeDocument, message);
        }
        catch (RuntimeProjectIdentityConflictException conflict)
        {
            await RefreshActiveProjectIdentityAsync(cancellationToken).ConfigureAwait(false);
            var message = conflict.ConflictMessage;
            _dialogs.ShowWarning("Complete Project deployment required", message);
            return new WorkflowDocumentDeployResult(false, false, runtimeDocument, message);
        }

        var publishedDocument = await VerifyRuntimeProjectAsync(
                runtimeProject,
                mergedWorkflow,
                published.Revision,
                published.ContentHash,
                $"Method deployment for '{savedMethod.Name}'",
                cancellationToken)
            .ConfigureAwait(false);
        var successMessage = $"Deployed and verified method '{savedMethod.Name}' as Runtime revision {published.Revision}; other Runtime documents were preserved.";
        _dialogs.ShowInformation("Method deployment succeeded", successMessage);
        return new WorkflowDocumentDeployResult(
            true,
            false,
            publishedDocument,
            successMessage);
    }

    /// <summary>Downloads the complete runtime Project for an explicit Project-level synchronization.</summary>
    public async Task<WorkflowProjectDownloadResult> DownloadProjectAsync(
        WorkflowProject currentProject,
        bool hasUnsavedDocuments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentProject);
        var response = await _runtimeApi.GetWorkflowAsync(
                WorkflowRuntimeDefaults.DefaultWorkflowId,
                cancellationToken);
        _workspaceSync.ApplyRuntimeSnapshot(response);
        var currentProjectJson = _persistence.Serialize(currentProject);
        if (JsonDocumentsAreEquivalent(currentProjectJson, _session.RuntimeProjectJson))
        {
            return new WorkflowProjectDownloadResult(
                WorkflowDownloadChoice.AlreadyCurrent,
                response,
                null,
                null,
                $"Local working copy already matches Runtime revision {_session.RuntimeRevision}.");
        }

        var isSameProject = response.ProjectId == currentProject.ProjectId;

        var choice = _dialogs.AskYesNoCancel(
            "Download from Workflow Runtime",
            isSameProject
                ? $"Runtime revision {_session.RuntimeRevision} differs from the local working copy.\n\n"
                  + "Yes: replace the complete local Project with the Runtime version\n"
                  + "No: open Compare without replacing anything\n"
                  + "Cancel: keep the local Project unchanged"
                : "Workflow Runtime contains another Project.\n\n"
                  + $"Local Project ID: {currentProject.ProjectId:D}\n"
                  + $"Runtime Project ID: {response.ProjectId:D}\n\n"
                  + "Yes: replace the complete local Project with the Runtime Project\n"
                  + "No or Cancel: keep the local Project unchanged\n\n"
                  + "Compare is unavailable because these are different Projects.");
        if (choice == EditorDialogChoice.No && isSameProject)
        {
            var comparison = Compare(currentProjectJson, response, hasUnsavedDocuments);
            return new WorkflowProjectDownloadResult(
                WorkflowDownloadChoice.Compare,
                response,
                null,
                comparison,
                comparison.Summary);
        }

        if (choice != EditorDialogChoice.Yes)
        {
            return new WorkflowProjectDownloadResult(
                WorkflowDownloadChoice.Cancelled,
                response,
                null,
                null,
                "Runtime download cancelled; the local Project was not changed.");
        }

        var downloadedProject = _persistence.Deserialize(response.Workflow.ToJsonString());
        return new WorkflowProjectDownloadResult(
            WorkflowDownloadChoice.Synchronize,
            response,
            downloadedProject,
            null,
            $"Downloaded Runtime revision {_session.RuntimeRevision} and replaced the complete local Project.");
    }

    /// <summary>Compares the complete local Project with the active runtime revision.</summary>
    public async Task<WorkflowComparisonResult> CompareProjectAsync(
        WorkflowProject localProject,
        bool hasUnsavedDocuments,
        CancellationToken cancellationToken = default)
    {
        await RequireMatchingActiveProjectOrThrowAsync(
                localProject,
                ProjectDeploymentScope.CompleteProject,
                "Compare Project",
                cancellationToken)
            .ConfigureAwait(false);
        var response = await _runtimeApi.GetWorkflowAsync(
                WorkflowRuntimeDefaults.DefaultWorkflowId,
                cancellationToken)
            .ConfigureAwait(false);
        EnsureRuntimeDocumentMatches(localProject, response, ProjectDeploymentScope.CompleteProject);
        _workspaceSync.ApplyRuntimeSnapshot(response);
        return Compare(_persistence.Serialize(localProject), response, hasUnsavedDocuments);
    }

    /// <summary>Downloads only the method or script currently open in the editor.</summary>
    public async Task<WorkflowDocumentDownloadResult> DownloadDocumentAsync(
        WorkflowEditorDocument localDocument,
        bool hasUnsavedChanges,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(localDocument);
        var response = await _runtimeApi.GetWorkflowAsync(
            WorkflowRuntimeDefaults.DefaultWorkflowId,
            cancellationToken);
        EnsureRuntimeDocumentMatches(ReadSavedProject(), response, localDocument.Kind == WorkflowEditorDocumentKind.Method
            ? ProjectDeploymentScope.CurrentMethod
            : ProjectDeploymentScope.CurrentScript);
        _workspaceSync.ApplyRuntimeSnapshot(response);

        var runtimeProject = _persistence.Deserialize(response.Workflow.ToJsonString());
        var runtimeDocument = FindMatchingDocument(runtimeProject, localDocument);
        var comparison = CompareDocument(localDocument, runtimeDocument, response, hasUnsavedChanges);
        if (runtimeDocument == null)
        {
            return new WorkflowDocumentDownloadResult(
                WorkflowDownloadChoice.Compare,
                response,
                null,
                comparison,
                $"'{localDocument.Name}' does not exist in Runtime revision {response.Revision}; nothing was overwritten.");
        }

        if (comparison.Differences.Count == 0)
        {
            return new WorkflowDocumentDownloadResult(
                WorkflowDownloadChoice.AlreadyCurrent,
                response,
                null,
                comparison,
                $"'{localDocument.Name}' already matches Runtime revision {response.Revision}.");
        }

        var documentType = localDocument.Kind == WorkflowEditorDocumentKind.Method ? "method" : "script";
        var choice = _dialogs.AskYesNoCancel(
            $"Download Runtime {documentType}",
            $"Runtime revision {response.Revision} differs from the local {documentType} '{localDocument.Name}'.\n\n"
            + $"Yes: overwrite only this {documentType} with the Runtime version\n"
            + "No: open Compare without overwriting anything\n"
            + "Cancel: keep the local document unchanged"
            + (hasUnsavedChanges ? "\n\nThis document contains unsaved local changes." : string.Empty));
        if (choice == EditorDialogChoice.No)
        {
            return new WorkflowDocumentDownloadResult(
                WorkflowDownloadChoice.Compare,
                response,
                null,
                comparison,
                comparison.Summary);
        }

        if (choice != EditorDialogChoice.Yes)
        {
            return new WorkflowDocumentDownloadResult(
                WorkflowDownloadChoice.Cancelled,
                response,
                null,
                null,
                $"Runtime download cancelled; '{localDocument.Name}' was not changed.");
        }

        return new WorkflowDocumentDownloadResult(
            WorkflowDownloadChoice.Synchronize,
            response,
            runtimeDocument,
            null,
            $"Downloaded '{localDocument.Name}' from Runtime revision {response.Revision}.");
    }

    /// <summary>Compares only the method or script currently open in the editor.</summary>
    public async Task<WorkflowComparisonResult> CompareDocumentAsync(
        WorkflowEditorDocument localDocument,
        bool hasUnsavedChanges,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(localDocument);
        var localProject = ReadSavedProject();
        var deploymentScope = localDocument.Kind == WorkflowEditorDocumentKind.Method
            ? ProjectDeploymentScope.CurrentMethod
            : ProjectDeploymentScope.CurrentScript;
        await RequireMatchingActiveProjectOrThrowAsync(
                localProject,
                deploymentScope,
                $"Compare {localDocument.Kind}",
                cancellationToken)
            .ConfigureAwait(false);
        if (localDocument.Kind == WorkflowEditorDocumentKind.CSharpScript && localDocument.Script != null)
        {
            return await CompareScriptDocumentAsync(localDocument, hasUnsavedChanges, cancellationToken)
                .ConfigureAwait(false);
        }

        var response = await _runtimeApi.GetWorkflowAsync(
            WorkflowRuntimeDefaults.DefaultWorkflowId,
            cancellationToken);
        EnsureRuntimeDocumentMatches(localProject, response, deploymentScope);
        _workspaceSync.ApplyRuntimeSnapshot(response);
        var runtimeProject = _persistence.Deserialize(response.Workflow.ToJsonString());
        return CompareDocument(
            localDocument,
            FindMatchingDocument(runtimeProject, localDocument),
            response,
            hasUnsavedChanges);
    }

    private async Task<WorkflowDocumentDeployResult> DeployScriptAsync(
        WorkflowScript localScript,
        bool hasUnsavedChanges,
        CancellationToken cancellationToken)
    {
        if (hasUnsavedChanges)
        {
            _dialogs.ShowWarning(
                "CSharp script is not saved",
                $"Save CSharp script '{localScript.Name}' locally before deploying it.");
            return new WorkflowDocumentDeployResult(
                false,
                false,
                null,
                $"Deploy blocked because CSharp script '{localScript.Name}' has unsaved changes.");
        }

        if (string.IsNullOrWhiteSpace(_session.SavedProjectJson))
        {
            return new WorkflowDocumentDeployResult(false, false, null, "Save the CSharp script locally before deploying it.");
        }

        var savedProject = _persistence.Deserialize(_session.SavedProjectJson);
        var savedScript = savedProject.Scripts.FirstOrDefault(script => script.Uid == localScript.Uid);
        if (savedScript == null)
        {
            return new WorkflowDocumentDeployResult(
                false,
                false,
                null,
                $"Saved CSharp script UID '{localScript.Uid}' was not found. Save the script before deploying it.");
        }

        var activeProject = await RequireMatchingActiveProjectAsync(
                savedProject,
                ProjectDeploymentScope.CurrentScript,
                cancellationToken)
            .ConfigureAwait(false);
        if (activeProject == null)
        {
            var message = CreateProjectMismatchMessage(savedProject, ProjectDeploymentScope.CurrentScript);
            _dialogs.ShowWarning("Complete Project deployment required", message);
            return new WorkflowDocumentDeployResult(false, false, null, message);
        }

        var runtimeDocument = await _runtimeApi.GetWorkflowAsync(
                WorkflowRuntimeDefaults.DefaultWorkflowId,
                cancellationToken)
            .ConfigureAwait(false);
        EnsureRuntimeDocumentMatches(savedProject, runtimeDocument, ProjectDeploymentScope.CurrentScript);
        _workspaceSync.ApplyRuntimeSnapshot(runtimeDocument);
        var expectedRuntimeProject = _persistence.Deserialize(runtimeDocument.Workflow.ToJsonString());
        var runtimeScriptIndex = expectedRuntimeProject.Scripts.FindIndex(script => script.Uid == savedScript.Uid);
        if (runtimeScriptIndex >= 0)
        {
            expectedRuntimeProject.Scripts[runtimeScriptIndex] = savedScript;
        }
        else
        {
            expectedRuntimeProject.Scripts.Add(savedScript);
        }
        expectedRuntimeProject.ScriptLibraries = savedProject.ScriptLibraries;
        var expectedRuntimeWorkflow = JsonNode.Parse(_persistence.Serialize(expectedRuntimeProject))
            ?? throw new InvalidOperationException("The expected Runtime Project is empty.");

        var confirmed = _dialogs.Confirm(
            "Deploy current CSharp script",
            $"Deploy CSharp script '{savedScript.Name}' to Workflow Runtime?\n\n"
            + $"Script UID: {savedScript.Uid}\n"
            + $"Current Runtime revision: {runtimeDocument.Revision}\n"
            + "Only this script will be merged. Runtime methods and other scripts remain unchanged.");
        if (!confirmed)
        {
            return new WorkflowDocumentDeployResult(false, false, null, "CSharp script deployment cancelled.");
        }

        SharpScriptPublishResponse publication;
        try
        {
            publication = await _runtimeApi.PublishSharpScriptAsync(
                WorkflowRuntimeDefaults.DefaultWorkflowId,
                savedProject.ProjectId,
                new SharpScriptDocumentDto
                {
                    Uid = savedScript.Uid,
                    Name = savedScript.Name,
                    Language = savedScript.Language,
                    Content = savedScript.Content
                },
                runtimeDocument.Revision,
                savedProject.ScriptLibraries.Select(item => new SharpScriptLibraryReferenceDto
                {
                    LibraryId = item.LibraryId,
                    Version = item.Version
                }).ToArray(),
                cancellationToken).ConfigureAwait(false);
        }
        catch (RuntimeRevisionConflictException conflict)
        {
            _session.RuntimeRevision = conflict.CurrentRevision;
            _session.RuntimeContentHash = conflict.CurrentContentHash;
            var message =
                $"CSharp script deploy stopped: Runtime revision {conflict.CurrentRevision} was changed by another client. "
                + "Use Compare or Download before deploying again.";
            _dialogs.ShowWarning("Runtime revision conflict", message);
            return new WorkflowDocumentDeployResult(false, true, null, message);
        }
        catch (RuntimeProjectIdentityConflictException conflict)
        {
            await RefreshActiveProjectIdentityAsync(cancellationToken).ConfigureAwait(false);
            var message = conflict.ConflictMessage;
            _dialogs.ShowWarning("Complete Project deployment required", message);
            return new WorkflowDocumentDeployResult(false, false, runtimeDocument, message);
        }

        if (!publication.Succeeded)
        {
            var errors = string.Join(
                "; ",
                publication.Diagnostics.Where(item => string.Equals(item.Severity, "Error", StringComparison.OrdinalIgnoreCase))
                    .Take(8)
                    .Select(item => $"{item.Code} ({item.Line},{item.Column}): {item.Message}"));
            var failureMessage = string.IsNullOrWhiteSpace(errors) ? "CSharp script compilation failed." : errors;
            _dialogs.ShowError("CSharp script deployment failed", failureMessage);
            return new WorkflowDocumentDeployResult(
                false,
                false,
                null,
                failureMessage,
                publication);
        }

        var publishedRuntimeDocument = await VerifyRuntimeProjectAsync(
                expectedRuntimeProject,
                expectedRuntimeWorkflow,
                publication.WorkflowRevision,
                expectedContentHash: null,
                $"CSharp script deployment for '{savedScript.Name}'",
                cancellationToken)
            .ConfigureAwait(false);
        await VerifyPublishedScriptAsync(savedScript, publication, cancellationToken).ConfigureAwait(false);
        var successMessage = $"Deployed and verified CSharp script '{savedScript.Name}' as script revision {publication.ScriptRevision}; Runtime methods and other scripts were preserved.";
        _dialogs.ShowInformation("CSharp script deployment succeeded", successMessage);
        return new WorkflowDocumentDeployResult(
            true,
            false,
            publishedRuntimeDocument,
            successMessage,
            publication);
    }

    private async Task<ActiveProjectIdentityResponse?> RefreshActiveProjectIdentityAsync(
        CancellationToken cancellationToken)
    {
        var activeProject = await _runtimeApi.GetActiveProjectIdentityAsync(
                WorkflowRuntimeDefaults.DefaultWorkflowId,
                cancellationToken)
            .ConfigureAwait(false);
        _workspaceSync.ApplyRuntimeIdentity(activeProject);
        return activeProject;
    }

    private async Task<ActiveProjectIdentityResponse?> RequireMatchingActiveProjectAsync(
        WorkflowProject localProject,
        ProjectDeploymentScope deploymentScope,
        CancellationToken cancellationToken)
    {
        var activeProject = await RefreshActiveProjectIdentityAsync(cancellationToken).ConfigureAwait(false);
        return activeProject?.ProjectId == localProject.ProjectId
            ? activeProject
            : null;
    }

    private async Task RequireMatchingActiveProjectOrThrowAsync(
        WorkflowProject localProject,
        ProjectDeploymentScope deploymentScope,
        string operationName,
        CancellationToken cancellationToken)
    {
        var activeProject = await RequireMatchingActiveProjectAsync(
                localProject,
                deploymentScope,
                cancellationToken)
            .ConfigureAwait(false);
        if (activeProject == null)
        {
            throw new InvalidOperationException(
                $"{operationName} is unavailable. {CreateProjectMismatchMessage(localProject, deploymentScope)}");
        }
    }

    private static string CreateProjectMismatchMessage(
        WorkflowProject localProject,
        ProjectDeploymentScope deploymentScope)
        => $"Local Project '{localProject.Name}' ({localProject.ProjectId:D}) is not the Runtime active Project. "
           + $"{deploymentScope} requires the same Project identity. Deploy the complete Project first.";

    private WorkflowProject ReadSavedProject()
    {
        if (string.IsNullOrWhiteSpace(_session.SavedProjectJson))
        {
            throw new InvalidOperationException("Save the local Project before using Runtime document operations.");
        }

        return _persistence.Deserialize(_session.SavedProjectJson);
    }

    private static void EnsureRuntimeDocumentMatches(
        WorkflowProject localProject,
        WorkflowDocumentResponse runtimeDocument,
        ProjectDeploymentScope deploymentScope)
    {
        if (runtimeDocument.ProjectId != localProject.ProjectId)
        {
            throw new InvalidOperationException(
                CreateProjectMismatchMessage(localProject, deploymentScope));
        }
    }

    private async Task<WorkflowComparisonResult> CompareScriptDocumentAsync(
        WorkflowEditorDocument localDocument,
        bool hasUnsavedChanges,
        CancellationToken cancellationToken)
    {
        SharpScriptDocumentResponse? publishedScript = null;
        try
        {
            publishedScript = await _runtimeApi.GetSharpScriptAsync(
                WorkflowRuntimeDefaults.DefaultWorkflowId,
                localDocument.Script!.Uid,
                cancellationToken).ConfigureAwait(false);
        }
        catch (RuntimeApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
        }

        var runtimeProject = await _runtimeApi.GetWorkflowAsync(
            WorkflowRuntimeDefaults.DefaultWorkflowId,
            cancellationToken).ConfigureAwait(false);
        EnsureRuntimeDocumentMatches(ReadSavedProject(), runtimeProject, ProjectDeploymentScope.CurrentScript);
        _workspaceSync.ApplyRuntimeSnapshot(runtimeProject);
        var runtimeDocument = publishedScript == null
            ? null
            : WorkflowEditorDocument.FromScript(new WorkflowScript
            {
                Uid = publishedScript.Script.Uid,
                Name = publishedScript.Script.Name,
                Language = publishedScript.Script.Language,
                Content = publishedScript.Script.Content
            });
        return CompareDocument(localDocument, runtimeDocument, runtimeProject, hasUnsavedChanges);
    }

    public static bool JsonDocumentsAreEquivalent(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
        {
            return false;
        }

        return WorkflowJsonComparer.AreEquivalent(JsonNode.Parse(first), JsonNode.Parse(second));
    }

    private static WorkflowComparisonResult Compare(
        string localProjectJson,
        WorkflowDocumentResponse runtimeDocument,
        bool hasUnsavedDocuments)
    {
        var differences = WorkflowJsonComparer.Compare(JsonNode.Parse(localProjectJson), runtimeDocument.Workflow);
        var summary = differences.Count == 0
            ? $"Local working copy matches Runtime revision {runtimeDocument.Revision}."
            : $"{differences.Count} difference(s) - Local working copy vs Runtime revision {runtimeDocument.Revision}"
              + (hasUnsavedDocuments ? " - Local working copy also contains unsaved changes" : string.Empty);
        return new WorkflowComparisonResult(runtimeDocument, differences, summary);
    }

    private WorkflowComparisonResult CompareDocument(
        WorkflowEditorDocument localDocument,
        WorkflowEditorDocument? runtimeDocument,
        WorkflowDocumentResponse runtimeProject,
        bool hasUnsavedChanges)
    {
        var localJson = JsonNode.Parse(_persistence.SerializeDocument(localDocument));
        var runtimeJson = runtimeDocument == null
            ? null
            : JsonNode.Parse(_persistence.SerializeDocument(runtimeDocument));
        var differences = WorkflowJsonComparer.Compare(localJson, runtimeJson);
        var summary = differences.Count == 0
            ? $"'{localDocument.Name}' matches Runtime revision {runtimeProject.Revision}."
            : $"{differences.Count} difference(s) in '{localDocument.Name}' compared with Runtime revision {runtimeProject.Revision}"
              + (hasUnsavedChanges ? " - This document also contains unsaved changes" : string.Empty);
        return new WorkflowComparisonResult(runtimeProject, differences, summary);
    }

    private static WorkflowEditorDocument? FindMatchingDocument(
        WorkflowProject runtimeProject,
        WorkflowEditorDocument localDocument)
        => localDocument.Kind switch
        {
            WorkflowEditorDocumentKind.Method when localDocument.Method != null
                => runtimeProject.Methods.FirstOrDefault(method => method.Uid == localDocument.Method.Uid) is { } method
                    ? WorkflowEditorDocument.FromMethod(method)
                    : null,
            WorkflowEditorDocumentKind.CSharpScript when localDocument.Script != null
                => runtimeProject.Scripts.FirstOrDefault(script => script.Uid == localDocument.Script.Uid) is { } script
                    ? WorkflowEditorDocument.FromScript(script)
                    : null,
            _ => null
        };

    private async Task<WorkflowDocumentResponse> VerifyRuntimeProjectAsync(
        WorkflowProject expectedProject,
        JsonNode expectedWorkflow,
        long expectedRevision,
        string? expectedContentHash,
        string operationName,
        CancellationToken cancellationToken)
    {
        try
        {
            var runtimeDocument = await _runtimeApi.GetWorkflowAsync(
                    WorkflowRuntimeDefaults.DefaultWorkflowId,
                    cancellationToken)
                .ConfigureAwait(false);
            var failures = new List<string>();
            if (runtimeDocument.ProjectId != expectedProject.ProjectId)
            {
                failures.Add(
                    $"Project ID is '{runtimeDocument.ProjectId:D}' instead of '{expectedProject.ProjectId:D}'");
            }

            if (runtimeDocument.Revision != expectedRevision)
            {
                failures.Add(
                    $"Runtime revision is {runtimeDocument.Revision} instead of the published revision {expectedRevision}");
            }

            if (!string.IsNullOrWhiteSpace(expectedContentHash)
                && !string.Equals(
                    runtimeDocument.ContentHash,
                    expectedContentHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                failures.Add("Runtime content hash does not match the publication response");
            }

            if (!WorkflowJsonComparer.AreEquivalent(expectedWorkflow, runtimeDocument.Workflow))
            {
                failures.Add("the Project read back from Runtime does not match the content that was published");
            }

            var actionCatalog = await _runtimeApi.GetActionCatalogAsync(cancellationToken).ConfigureAwait(false);
            var missingScriptActions = expectedProject.Scripts
                .Where(script => !actionCatalog.Actions.Any(action => string.Equals(
                    action.ActionId,
                    $"csharp-script:{script.Uid:D}",
                    StringComparison.OrdinalIgnoreCase)))
                .Select(script => $"{script.Name} ({script.Uid:D})")
                .ToArray();
            if (missingScriptActions.Length > 0)
            {
                failures.Add(
                    "these CSharp script Actions are missing from the Runtime Action Catalog: "
                    + string.Join(", ", missingScriptActions));
            }

            if (failures.Count > 0)
            {
                throw new RuntimeDeploymentVerificationException(
                    operationName,
                    expectedRevision,
                    string.Join("; ", failures));
            }

            _workspaceSync.ApplyRuntimeSnapshot(runtimeDocument);
            return runtimeDocument;
        }
        catch (RuntimeDeploymentVerificationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new RuntimeDeploymentVerificationException(
                operationName,
                expectedRevision,
                $"Runtime state could not be read back: {exception.Message}",
                exception);
        }
    }

    private async Task VerifyPublishedScriptAsync(
        WorkflowScript expectedScript,
        SharpScriptPublishResponse publication,
        CancellationToken cancellationToken)
    {
        try
        {
            var publishedScript = await _runtimeApi.GetSharpScriptAsync(
                    WorkflowRuntimeDefaults.DefaultWorkflowId,
                    expectedScript.Uid,
                    cancellationToken)
                .ConfigureAwait(false);
            var failures = new List<string>();
            if (publishedScript.WorkflowRevision != publication.WorkflowRevision)
            {
                failures.Add(
                    $"script workflow revision is {publishedScript.WorkflowRevision} instead of {publication.WorkflowRevision}");
            }

            if (publishedScript.Script.Uid != expectedScript.Uid
                || !string.Equals(publishedScript.Script.Name, expectedScript.Name, StringComparison.Ordinal)
                || !string.Equals(publishedScript.Script.Language, expectedScript.Language, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(publishedScript.Script.Content, expectedScript.Content, StringComparison.Ordinal))
            {
                failures.Add("the script source read back from Runtime does not match the saved local script");
            }

            if (!string.Equals(publishedScript.ActionId, publication.ActionId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(publishedScript.ActionType, publication.ActionType, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add("the registered script Action identity does not match the publication response");
            }

            if (failures.Count > 0)
            {
                throw new RuntimeDeploymentVerificationException(
                    $"CSharp script deployment for '{expectedScript.Name}'",
                    publication.WorkflowRevision,
                    string.Join("; ", failures));
            }
        }
        catch (RuntimeDeploymentVerificationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new RuntimeDeploymentVerificationException(
                $"CSharp script deployment for '{expectedScript.Name}'",
                publication.WorkflowRevision,
                $"the published script contract could not be read back: {exception.Message}",
                exception);
        }
    }

    private async Task EnsureWorkflowCanPublishAsync(
        JsonNode workflow,
        CancellationToken cancellationToken)
    {
        var validation = await _runtimeApi.ValidateAsync(workflow, cancellationToken).ConfigureAwait(false);
        if (validation.IsValid)
        {
            return;
        }

        var details = string.Join(
            "; ",
            validation.Messages
                .Where(message => !string.Equals(message.Severity, "Info", StringComparison.OrdinalIgnoreCase))
                .Take(8)
                .Select(message => message.MethodName == null
                    ? message.Message
                    : $"{message.MethodName}: {message.Message}"));
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(details) ? "Runtime validation failed." : details);
    }
}

public sealed record WorkflowDeploymentState(bool HasUnsavedLocalChanges, bool HasUndeployedSavedChanges);

public sealed class RuntimeDeploymentVerificationException : InvalidOperationException
{
    public RuntimeDeploymentVerificationException(
        string operationName,
        long expectedRevision,
        string verificationFailure,
        Exception? innerException = null)
        : base(
            $"{operationName} was accepted by Runtime, but deployment verification did not complete: "
            + $"{verificationFailure}. Expected Runtime revision: {expectedRevision}. Refresh or Compare before deploying again.",
            innerException)
    {
        OperationName = operationName;
        ExpectedRevision = expectedRevision;
    }

    public string OperationName { get; }

    public long ExpectedRevision { get; }
}

public sealed record WorkflowDeployResult(
    bool Deployed,
    bool IsRevisionConflict,
    WorkflowDocumentResponse? RuntimeDocument,
    string Message,
    bool RuntimeMatchesSavedProject = false);

public sealed record WorkflowDocumentDeployResult(
    bool Deployed,
    bool IsRevisionConflict,
    WorkflowDocumentResponse? RuntimeDocument,
    string Message,
    SharpScriptPublishResponse? ScriptPublication = null);

public enum WorkflowDownloadChoice
{
    AlreadyCurrent,
    Compare,
    Cancelled,
    Synchronize
}

public sealed record WorkflowProjectDownloadResult(
    WorkflowDownloadChoice Choice,
    WorkflowDocumentResponse RuntimeDocument,
    WorkflowProject? DownloadedProject,
    WorkflowComparisonResult? Comparison,
    string Message);

public sealed record WorkflowDocumentDownloadResult(
    WorkflowDownloadChoice Choice,
    WorkflowDocumentResponse RuntimeProject,
    WorkflowEditorDocument? DownloadedDocument,
    WorkflowComparisonResult? Comparison,
    string Message);

public sealed record WorkflowComparisonResult(
    WorkflowDocumentResponse RuntimeDocument,
    IReadOnlyList<WorkflowDifferenceItem> Differences,
    string Summary);
