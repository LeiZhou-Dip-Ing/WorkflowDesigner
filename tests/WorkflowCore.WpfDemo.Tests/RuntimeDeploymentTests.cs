using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Services;
using WorkflowCore.WpfDemo.Services.Editing;
using WorkflowCore.WpfDemo.Services.Runtime;
using WorkflowCore.WpfDemo.Services.Ui;
using WorkflowRuntime.Contracts;
using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class RuntimeDeploymentTests
{
    [Fact]
    public async Task SameProject_AllowsProjectComparison()
    {
        using var context = CreateContext(runtimeRevision: 3);

        var result = await context.Coordinator.CompareProjectAsync(context.Session.Project, false);

        Assert.Equal(context.Session.Project.ProjectId, result.RuntimeDocument.ProjectId);
    }

    [Fact]
    public async Task SameProject_AllowsMethodAndScriptComparison()
    {
        using var context = CreateContext(runtimeRevision: 3);
        var method = CreateMethod("Local method", "log");
        var script = new WorkflowScript { Name = "LocalScript", Content = "public class LocalScript {}" };
        context.Session.Project.Methods = [method];
        context.Session.Project.Scripts = [script];
        context.Session.SavedProjectJson = context.DocumentService.Serialize(context.Session.Project);

        var methodComparison = await context.Coordinator.CompareDocumentAsync(
            WorkflowEditorDocument.FromMethod(method),
            false);
        var scriptComparison = await context.Coordinator.CompareDocumentAsync(
            WorkflowEditorDocument.FromScript(script),
            false);

        Assert.NotNull(methodComparison);
        Assert.NotNull(scriptComparison);
    }

    [Fact]
    public async Task DifferentProject_BlocksProjectAndDocumentComparison()
    {
        using var context = CreateContext(runtimeRevision: 3);
        SetDifferentRuntimeProject(context, revision: 7);
        var method = CreateMethod("Local method", "log");
        context.Session.Project.Methods = [method];
        context.Session.SavedProjectJson = context.DocumentService.Serialize(context.Session.Project);

        var projectError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Coordinator.CompareProjectAsync(context.Session.Project, false));
        var methodError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Coordinator.CompareDocumentAsync(WorkflowEditorDocument.FromMethod(method), false));

        Assert.Contains("not the Runtime active Project", projectError.Message);
        Assert.Contains("not the Runtime active Project", methodError.Message);
    }

    [Fact]
    public async Task DifferentProject_BlocksIndividualMethodAndScriptDeployment()
    {
        using var context = CreateContext(runtimeRevision: 3);
        SetDifferentRuntimeProject(context, revision: 7);
        var method = CreateMethod("Local method", "log");
        var script = new WorkflowScript { Name = "LocalScript", Content = "public class LocalScript {}" };
        context.Session.Project.Methods = [method];
        context.Session.Project.Scripts = [script];
        context.Session.SavedProjectJson = context.DocumentService.Serialize(context.Session.Project);

        var methodResult = await context.Coordinator.DeployDocumentAsync(
            WorkflowEditorDocument.FromMethod(method),
            false);
        var scriptResult = await context.Coordinator.DeployDocumentAsync(
            WorkflowEditorDocument.FromScript(script),
            false);

        Assert.False(methodResult.Deployed);
        Assert.False(scriptResult.Deployed);
        Assert.Null(context.RuntimeClient.PublishedWorkflow);
        Assert.Null(context.RuntimeClient.PublishedScript);
    }

    [Fact]
    public async Task DifferentProject_AllowsOnlyCompleteReplacementDeployment()
    {
        using var context = CreateContext(runtimeRevision: 3);
        SetDifferentRuntimeProject(context, revision: 7);

        var result = await context.Coordinator.DeployAsync(Array.Empty<string>());

        Assert.True(result.Deployed);
        Assert.Equal(ProjectDeploymentScope.CompleteProject, context.RuntimeClient.PublishedDeploymentScope);
        Assert.Equal(context.Session.Project.ProjectId, context.RuntimeClient.PublishedProjectId);
        Assert.Contains("another Project", context.Dialog.LastConfirmationMessage);
        Assert.True(context.Session.IsCurrentProjectActive);
    }

    [Fact]
    public async Task SwitchingLocalProject_ReevaluatesRuntimeProjectIdentity()
    {
        using var context = CreateContext(runtimeRevision: 3);
        await context.Synchronization.SynchronizeAsync(context.Session.Project);
        Assert.True(context.Session.IsCurrentProjectActive);

        var otherLocalProject = new WorkflowProject { Name = "Other local Project" };
        context.Session.Project = otherLocalProject;
        context.Session.ClearRuntimeProjectState();
        await context.Synchronization.SynchronizeAsync(otherLocalProject);

        Assert.False(context.Session.IsCurrentProjectActive);
        Assert.Equal(context.RuntimeClient.WorkflowResponse.ProjectId, context.Session.RuntimeProjectId);
        Assert.Null(context.Session.RuntimeProjectJson);
    }

    [Fact]
    public async Task Deploy_PassesCurrentRuntimeRevision()
    {
        using var context = CreateContext(runtimeRevision: 42);

        await context.Coordinator.DeployAsync(Array.Empty<string>());

        Assert.Equal(42, context.RuntimeClient.PublishedExpectedRevision);
    }

    [Fact]
    public async Task DeploySuccess_UpdatesRuntimeSnapshotRevisionAndHash()
    {
        using var context = CreateContext(runtimeRevision: 4);
        context.RuntimeClient.PublishResponse = new WorkflowPublishResponse
        {
            WorkflowId = WorkflowRuntimeDefaults.DefaultWorkflowId,
            Revision = 5,
            ContentHash = "new-hash"
        };

        var result = await context.Coordinator.DeployAsync(Array.Empty<string>());

        Assert.True(result.Deployed);
        Assert.Equal(5, context.Session.RuntimeRevision);
        Assert.Equal("new-hash", context.Session.RuntimeContentHash);
        Assert.True(WorkflowJsonComparer.AreEquivalent(
            JsonNode.Parse(context.Session.SavedProjectJson),
            JsonNode.Parse(context.Session.RuntimeProjectJson!)));
        Assert.True(result.RuntimeMatchesSavedProject);
        Assert.Contains("Deployed and verified", context.Dialog.LastInformationMessage);
        Assert.Equal(1, context.RuntimeClient.WorkflowValidationCount);
    }

    [Fact]
    public async Task DeployReadBackMismatch_IsNotReportedAsSuccess()
    {
        using var context = CreateContext(runtimeRevision: 4);
        var previousRuntimeJson = context.Session.RuntimeProjectJson;
        context.RuntimeClient.ApplyPublishedWorkflowToRuntime = false;

        var exception = await Assert.ThrowsAsync<RuntimeDeploymentVerificationException>(() =>
            context.Coordinator.DeployAsync(Array.Empty<string>()));

        Assert.Contains("does not match the content that was published", exception.Message);
        Assert.Equal(string.Empty, context.Dialog.LastInformationMessage);
        Assert.Equal(previousRuntimeJson, context.Session.RuntimeProjectJson);
    }

    [Fact]
    public async Task DeployWithMissingScriptActionRegistration_IsNotReportedAsSuccess()
    {
        using var context = CreateContext(runtimeRevision: 4);
        context.Session.Project.Scripts =
        [
            new WorkflowScript
            {
                Name = "NewScript",
                Content = "public sealed class NewScript {}"
            }
        ];
        context.Session.SavedProjectJson = context.DocumentService.Serialize(context.Session.Project);
        context.RuntimeClient.HideScriptActionsFromCatalog = true;

        var exception = await Assert.ThrowsAsync<RuntimeDeploymentVerificationException>(() =>
            context.Coordinator.DeployAsync(Array.Empty<string>()));

        Assert.Contains("missing from the Runtime Action Catalog", exception.Message);
        Assert.Equal(string.Empty, context.Dialog.LastInformationMessage);
    }

    [Fact]
    public async Task AlreadySynchronizedProject_IsMarkedCurrentSoTheEditorCanRefreshItsCatalog()
    {
        using var context = CreateContext(runtimeRevision: 4);
        context.Session.RuntimeProjectJson = context.Session.SavedProjectJson;
        context.RuntimeClient.WorkflowResponse = CreateRuntimeDocument(context, 4, context.Session.Project);

        var result = await context.Coordinator.DeployAsync(Array.Empty<string>());

        Assert.False(result.Deployed);
        Assert.True(result.RuntimeMatchesSavedProject);
        Assert.Null(context.RuntimeClient.PublishedWorkflow);
    }

    [Fact]
    public async Task DeployConflict_DoesNotMarkLocalAsDeployed()
    {
        using var context = CreateContext(runtimeRevision: 4);
        var previousRuntimeJson = context.Session.RuntimeProjectJson;
        context.RuntimeClient.PublishException = new RuntimeRevisionConflictException(
            WorkflowRuntimeDefaults.DefaultWorkflowId,
            4,
            7,
            "runtime-7");

        var result = await context.Coordinator.DeployAsync(Array.Empty<string>());

        Assert.True(result.IsRevisionConflict);
        Assert.False(result.Deployed);
        Assert.Equal(previousRuntimeJson, context.Session.RuntimeProjectJson);
        Assert.True(context.Coordinator.GetState(false).HasUndeployedSavedChanges);
    }

    [Fact]
    public async Task DeployConflict_DoesNotDiscardLocalProject()
    {
        using var context = CreateContext(runtimeRevision: 4);
        var localProject = context.Session.Project;
        var localJson = context.DocumentService.Serialize(localProject);
        context.RuntimeClient.PublishException = new RuntimeRevisionConflictException(
            WorkflowRuntimeDefaults.DefaultWorkflowId,
            4,
            8,
            "runtime-8");

        await context.Coordinator.DeployAsync(Array.Empty<string>());

        Assert.Same(localProject, context.Session.Project);
        Assert.Equal(localJson, context.DocumentService.Serialize(context.Session.Project));
    }

    [Fact]
    public async Task DeployConflict_LeavesCompareAvailable()
    {
        using var context = CreateContext(runtimeRevision: 4);
        context.RuntimeClient.PublishException = new RuntimeRevisionConflictException(
            WorkflowRuntimeDefaults.DefaultWorkflowId,
            4,
            9,
            "runtime-9");
        context.RuntimeClient.WorkflowResponse = CreateRuntimeDocument(context, 9, "Runtime version");
        await context.Coordinator.DeployAsync(Array.Empty<string>());

        var comparison = await context.Coordinator.CompareProjectAsync(context.Session.Project, false);

        Assert.NotEmpty(comparison.Differences);
        Assert.Equal(9, comparison.RuntimeDocument.Revision);
        Assert.Equal(1, context.RuntimeClient.WorkflowDownloadCount);
    }

    [Fact]
    public async Task DownloadRuntimeWorkflow_ResetsExpectedRevision()
    {
        using var context = CreateContext(runtimeRevision: 3);
        context.Dialog.DownloadChoice = EditorDialogChoice.Yes;
        var runtimeProject = new WorkflowProject
        {
            Name = "Downloaded",
            Version = "12",
            Methods =
            [
                CreateMethod("Main", "log"),
                CreateMethod("Worker", "delay"),
                CreateMethod("Background", "threadWait")
            ]
        };
        context.RuntimeClient.WorkflowResponse = CreateRuntimeDocument(context, 12, runtimeProject);

        var download = await context.Coordinator.DownloadProjectAsync(context.Session.Project, false);

        Assert.Equal(WorkflowDownloadChoice.Synchronize, download.Choice);
        Assert.Equal(12, context.Session.RuntimeRevision);
        Assert.Equal("hash-12", context.Session.RuntimeContentHash);
        Assert.Equal(1, context.RuntimeClient.WorkflowDownloadCount);
        Assert.Equal("Downloaded", download.DownloadedProject?.Name);
        Assert.Equal(3, download.DownloadedProject?.Methods.Count);
        Assert.Equal(
            runtimeProject.Methods.Select(method => (method.Uid, method.Name)),
            download.DownloadedProject?.Methods.Select(method => (method.Uid, method.Name)));
        Assert.True(WorkflowJsonComparer.AreEquivalent(
            context.RuntimeClient.WorkflowResponse.Workflow,
            JsonNode.Parse(context.DocumentService.Serialize(download.DownloadedProject!))));
    }

    [Fact]
    public async Task DownloadRuntimeWorkflow_RejectsAProjectThatPredatesMethodContracts()
    {
        using var context = CreateContext(runtimeRevision: 3);
        context.Dialog.DownloadChoice = EditorDialogChoice.Yes;
        context.RuntimeClient.WorkflowResponse = new WorkflowDocumentResponse
        {
            WorkflowId = WorkflowRuntimeDefaults.DefaultWorkflowId,
            Revision = 12,
            ContentHash = "hash-12",
            Workflow = JsonNode.Parse("""
                {
                  "name": "Old Runtime project",
                  "methods": [
                    {
                      "name": "Worker",
                      "methodVariables": [
                        { "variableName": "_$input", "dataType": "number" },
                        { "variableName": "_$0result", "dataType": "number" }
                      ],
                      "methodLines": [
                        { "lineNo": 10, "action": { "actionType": "return", "returnValues": "_$0result" } }
                      ]
                    }
                  ]
                }
                """)!
        };

        var exception = await Assert.ThrowsAsync<JsonException>(() =>
            context.Coordinator.DownloadProjectAsync(context.Session.Project, false));

        Assert.Contains("explicit input/output contract", exception.Message);
    }

    [Fact]
    public async Task DownloadDocument_ReturnsOnlyTheMatchingRuntimeMethod()
    {
        using var context = CreateContext(runtimeRevision: 3);
        context.Dialog.DownloadChoice = EditorDialogChoice.Yes;
        var selectedMethod = CreateMethod("Selected", "greeting");
        var otherLocalMethod = CreateMethod("Other local method", "log");
        context.Session.Project.Methods = [selectedMethod, otherLocalMethod];
        var runtimeSelectedMethod = CreateMethod("Selected", "greeting");
        runtimeSelectedMethod.Uid = selectedMethod.Uid;
        runtimeSelectedMethod.MethodLines.Add(MethodLine.Create(20, 0, WorkflowAction.Create("delay")));
        context.RuntimeClient.WorkflowResponse = CreateRuntimeDocument(
            context,
            12,
            new WorkflowProject
            {
                Name = "Runtime",
                Methods = [runtimeSelectedMethod, CreateMethod("Other runtime method", "delay")]
            });

        var result = await context.Coordinator.DownloadDocumentAsync(
            WorkflowEditorDocument.FromMethod(selectedMethod),
            false);

        Assert.Equal(WorkflowDownloadChoice.Synchronize, result.Choice);
        Assert.Equal(selectedMethod.Uid, result.DownloadedDocument?.Method?.Uid);
        Assert.Equal(2, result.DownloadedDocument?.Method?.MethodLines.Count);
        Assert.Same(otherLocalMethod, context.Session.Project.Methods[1]);
        Assert.Equal(1, context.RuntimeClient.WorkflowDownloadCount);
    }

    [Fact]
    public async Task DeployDocument_ReplacesOnlyTheMatchingMethodUid()
    {
        using var context = CreateContext(runtimeRevision: 3);
        var localMethod = CreateMethod("Renamed local method", "log");
        var untouchedRuntimeMethod = CreateMethod("Runtime only", "delay");
        context.Session.Project.Methods = [localMethod];
        context.Session.SavedProjectJson = context.DocumentService.Serialize(context.Session.Project);
        var oldRuntimeMethod = CreateMethod("Old method name", "delay");
        oldRuntimeMethod.Uid = localMethod.Uid;
        context.RuntimeClient.WorkflowResponse = CreateRuntimeDocument(
            context,
            12,
            new WorkflowProject
            {
                Name = "Runtime",
                Methods = [oldRuntimeMethod, untouchedRuntimeMethod]
            });

        var result = await context.Coordinator.DeployDocumentAsync(
            WorkflowEditorDocument.FromMethod(localMethod),
            false);

        var published = context.DocumentService.Deserialize(context.RuntimeClient.PublishedWorkflow!.ToJsonString());
        Assert.True(result.Deployed);
        Assert.Equal(12, context.RuntimeClient.PublishedExpectedRevision);
        Assert.Equal(ProjectDeploymentScope.CurrentMethod, context.RuntimeClient.PublishedDeploymentScope);
        Assert.Equal(context.Session.Project.ProjectId, context.RuntimeClient.PublishedProjectId);
        Assert.Equal(2, published.Methods.Count);
        Assert.Contains(published.Methods, method => method.Uid == localMethod.Uid && method.Name == localMethod.Name);
        Assert.Contains(published.Methods, method => method.Uid == untouchedRuntimeMethod.Uid && method.Name == untouchedRuntimeMethod.Name);
    }

    [Fact]
    public async Task DeployDocument_AddsANewMethodUidWithoutReplacingRuntimeMethods()
    {
        using var context = CreateContext(runtimeRevision: 3);
        var newLocalMethod = CreateMethod("New local method", "log");
        var existingRuntimeMethod = CreateMethod("Existing runtime method", "delay");
        context.Session.Project.Methods = [newLocalMethod];
        context.Session.SavedProjectJson = context.DocumentService.Serialize(context.Session.Project);
        context.RuntimeClient.WorkflowResponse = CreateRuntimeDocument(
            context,
            15,
            new WorkflowProject { Name = "Runtime", Methods = [existingRuntimeMethod] });

        var result = await context.Coordinator.DeployDocumentAsync(
            WorkflowEditorDocument.FromMethod(newLocalMethod),
            false);

        var published = context.DocumentService.Deserialize(context.RuntimeClient.PublishedWorkflow!.ToJsonString());
        Assert.True(result.Deployed);
        Assert.Equal(2, published.Methods.Count);
        Assert.Contains(published.Methods, method => method.Uid == newLocalMethod.Uid);
        Assert.Contains(published.Methods, method => method.Uid == existingRuntimeMethod.Uid);
    }

    [Fact]
    public async Task DeployDocument_IsBlockedWhenRuntimeHasNoActiveProject()
    {
        using var context = CreateContext(runtimeRevision: 0);
        var newLocalMethod = CreateMethod("First method", "log");
        context.Session.Project.Name = "New local Project";
        context.Session.Project.Version = "7";
        context.Session.Project.Methods = [newLocalMethod];
        context.Session.SavedProjectJson = context.DocumentService.Serialize(context.Session.Project);
        context.RuntimeClient.HasActiveProject = false;

        var result = await context.Coordinator.DeployDocumentAsync(
            WorkflowEditorDocument.FromMethod(newLocalMethod),
            false);

        Assert.False(result.Deployed);
        Assert.Null(context.RuntimeClient.PublishedWorkflow);
        Assert.Contains("Deploy the complete Project first", result.Message);
    }

    [Fact]
    public async Task DeployDocument_DoesNotReplaceASameNameMethodWithADifferentUid()
    {
        using var context = CreateContext(runtimeRevision: 3);
        var localMethod = CreateMethod("Shared name", "log");
        var runtimeMethod = CreateMethod("Shared name", "delay");
        context.Session.Project.Methods = [localMethod];
        context.Session.SavedProjectJson = context.DocumentService.Serialize(context.Session.Project);
        context.RuntimeClient.WorkflowResponse = CreateRuntimeDocument(
            context,
            18,
            new WorkflowProject { Name = "Runtime", Methods = [runtimeMethod] });

        var result = await context.Coordinator.DeployDocumentAsync(
            WorkflowEditorDocument.FromMethod(localMethod),
            false);

        Assert.False(result.Deployed);
        Assert.Null(context.RuntimeClient.PublishedWorkflow);
        Assert.Contains("different UID", result.Message);
    }

    [Fact]
    public async Task DeployScript_UsesLatestRuntimeRevisionAndPreservesOtherRuntimeDocuments()
    {
        using var context = CreateContext(runtimeRevision: 7);
        var target = new WorkflowScript
        {
            Name = "ExtractDigits",
            Language = "CSharp",
            Content = "updated source"
        };
        var otherLocalScript = new WorkflowScript
        {
            Name = "LocalOnly",
            Language = "CSharp",
            Content = "local source"
        };
        context.Session.Project.Scripts = [target, otherLocalScript];
        context.Session.Project.Methods = [CreateMethod("Local method", "log")];
        context.Session.SavedProjectJson = context.DocumentService.Serialize(context.Session.Project);

        var preservedMethod = CreateMethod("Runtime method", "delay");
        var preservedScript = new WorkflowScript
        {
            Name = "RuntimeOther",
            Language = "CSharp",
            Content = "runtime source"
        };
        var runtimeProject = new WorkflowProject
        {
            Name = "Runtime",
            Methods = [preservedMethod],
            Scripts =
            [
                new WorkflowScript
                {
                    Uid = target.Uid,
                    Name = target.Name,
                    Language = target.Language,
                    Content = target.Content
                },
                preservedScript
            ]
        };
        context.RuntimeClient.ScriptPublishResponse = new SharpScriptPublishResponse
        {
            WorkflowId = WorkflowRuntimeDefaults.DefaultWorkflowId,
            WorkflowRevision = 8,
            ScriptUid = target.Uid,
            ScriptRevision = 3,
            ActionId = $"csharp-script:{target.Uid:D}",
            ActionType = $"csharp-script:{target.Uid:N}",
            Succeeded = true
        };
        context.RuntimeClient.WorkflowResponse = CreateRuntimeDocument(context, 8, runtimeProject);

        var result = await context.Coordinator.DeployDocumentAsync(
            WorkflowEditorDocument.FromScript(target),
            hasUnsavedChanges: false);

        Assert.True(result.Deployed);
        Assert.Null(context.RuntimeClient.PublishedWorkflow);
        Assert.Equal(8, context.RuntimeClient.PublishedScriptExpectedRevision);
        Assert.Equal(ProjectDeploymentScope.CurrentScript, context.RuntimeClient.PublishedDeploymentScope);
        Assert.Equal(context.Session.Project.ProjectId, context.RuntimeClient.PublishedProjectId);
        Assert.Equal(target.Uid, context.RuntimeClient.PublishedScript?.Uid);
        Assert.Equal("updated source", context.RuntimeClient.PublishedScript?.Content);
        Assert.Equal(8, context.Session.RuntimeRevision);
        var refreshedRuntime = context.DocumentService.Deserialize(context.Session.RuntimeProjectJson!);
        Assert.Contains(refreshedRuntime.Methods, item => item.Uid == preservedMethod.Uid);
        Assert.Contains(refreshedRuntime.Scripts, item => item.Uid == preservedScript.Uid);
        Assert.DoesNotContain(refreshedRuntime.Scripts, item => item.Uid == otherLocalScript.Uid);
    }

    [Fact]
    public void RuntimeSnapshot_DoesNotReplaceTheLocalEditorProject()
    {
        using var context = CreateContext(runtimeRevision: 3);
        var localProject = context.Session.Project;
        var localJson = context.DocumentService.Serialize(localProject);

        context.Synchronization.ApplyRuntimeSnapshot(CreateRuntimeDocument(context, 12, "Runtime project"));

        Assert.Same(localProject, context.Session.Project);
        Assert.Equal(localJson, context.DocumentService.Serialize(context.Session.Project));
        Assert.Equal(12, context.Session.RuntimeRevision);
    }

    [Fact]
    public async Task StartupSynchronization_DoesNotDownloadTheRuntimeProject()
    {
        using var context = CreateContext(runtimeRevision: 0);
        var localProject = context.Session.Project;
        var localJson = context.DocumentService.Serialize(localProject);

        var result = await context.Synchronization.SynchronizeAsync(localProject);

        Assert.NotNull(result);
        Assert.Equal(1, context.Catalog.RefreshCount);
        Assert.Equal(0, context.RuntimeClient.WorkflowDownloadCount);
        Assert.Equal(0, context.RuntimeClient.WorkflowValidationCount);
        Assert.Same(localProject, context.Session.Project);
        Assert.Equal(localJson, context.DocumentService.Serialize(context.Session.Project));
    }

    private static TestContext CreateContext(long runtimeRevision)
    {
        var serializer = new WorkflowEditorJsonSerializer();
        var documentPersistence = new JsonEditorDocumentPersistence(serializer);
        var project = new WorkflowProject { Name = "Local project", Version = "1" };
        var session = new EditorSession(project)
        {
            SavedProjectJson = documentPersistence.Serialize(project),
            RuntimeProjectJson = documentPersistence.Serialize(new WorkflowProject
            {
                ProjectId = project.ProjectId,
                Name = "Old Runtime",
                Version = "1"
            }),
            RuntimeProjectId = project.ProjectId,
            RuntimeRevision = runtimeRevision,
            RuntimeContentHash = $"hash-{runtimeRevision}"
        };
        var runtimeClient = new FakeRuntimeClient();
        runtimeClient.WorkflowResponse = CreateRuntimeDocument(
            documentPersistence,
            project.ProjectId,
            runtimeRevision,
            new WorkflowProject
            {
                ProjectId = project.ProjectId,
                Name = "Old Runtime",
                Version = "1"
            });
        runtimeClient.PublishResponse = new WorkflowPublishResponse
        {
            WorkflowId = WorkflowRuntimeDefaults.DefaultWorkflowId,
            ProjectId = project.ProjectId,
            Revision = runtimeRevision + 1,
            ContentHash = "published-hash"
        };
        var catalog = new FakeActionCatalogService();
        var propertyEditor = new ActionPropertyEditor(
            catalog,
            new VariableEditor());
        var synchronization = new RuntimeWorkspaceSync(
            runtimeClient,
            catalog,
            documentPersistence,
            propertyEditor,
            session);
        var dialog = new FakeDialogService();
        var deployment = new RuntimeDeployment(
            runtimeClient,
            documentPersistence,
            dialog,
            session,
            synchronization);
        return new TestContext(
            deployment,
            synchronization,
            runtimeClient,
            catalog,
            dialog,
            session,
            documentPersistence);
    }

    private static WorkflowDocumentResponse CreateRuntimeDocument(
        TestContext context,
        long revision,
        string name)
        => CreateRuntimeDocument(
            context,
            revision,
            new WorkflowProject { Name = name, Version = revision.ToString() });

    private static WorkflowDocumentResponse CreateRuntimeDocument(
        TestContext context,
        long revision,
        WorkflowProject project)
    {
        project.ProjectId = context.Session.Project.ProjectId;
        return CreateRuntimeDocument(
            context.DocumentService,
            project.ProjectId,
            revision,
            project);
    }

    private static WorkflowDocumentResponse CreateRuntimeDocument(
        JsonEditorDocumentPersistence documentService,
        Guid projectId,
        long revision,
        WorkflowProject project)
        => new()
        {
            WorkflowId = WorkflowRuntimeDefaults.DefaultWorkflowId,
            ProjectId = projectId,
            Workflow = JsonNode.Parse(documentService.Serialize(project))!,
            Revision = revision,
            ContentHash = $"hash-{revision}"
        };

    private static WorkflowMethod CreateMethod(string name, string actionType)
        => new()
        {
            Name = name,
            MethodLines = [MethodLine.Create(10, 0, WorkflowAction.Create(actionType))]
        };

    private static void SetDifferentRuntimeProject(TestContext context, long revision)
    {
        var runtimeProject = new WorkflowProject
        {
            ProjectId = Guid.NewGuid(),
            Name = "Different Runtime Project",
            Methods = [CreateMethod("Remote method", "delay")],
            Scripts = [new WorkflowScript { Name = "RemoteScript", Content = "public class RemoteScript {}" }]
        };
        context.RuntimeClient.WorkflowResponse = CreateRuntimeDocument(
            context.DocumentService,
            runtimeProject.ProjectId,
            revision,
            runtimeProject);
        context.RuntimeClient.PublishResponse = new WorkflowPublishResponse
        {
            WorkflowId = WorkflowRuntimeDefaults.DefaultWorkflowId,
            ProjectId = context.Session.Project.ProjectId,
            Revision = revision + 1,
            ContentHash = $"hash-{revision + 1}"
        };
    }

    private sealed record TestContext(
        RuntimeDeployment Coordinator,
        RuntimeWorkspaceSync Synchronization,
        FakeRuntimeClient RuntimeClient,
        FakeActionCatalogService Catalog,
        FakeDialogService Dialog,
        EditorSession Session,
        JsonEditorDocumentPersistence DocumentService) : IDisposable
    {
        public void Dispose() => Synchronization.Dispose();
    }

    private sealed class FakeDialogService : IEditorDialogs
    {
        public EditorDialogChoice DownloadChoice { get; set; } = EditorDialogChoice.Cancel;
        public string LastConfirmationMessage { get; private set; } = string.Empty;
        public string LastInformationMessage { get; private set; } = string.Empty;
        public void ShowInformation(string title, string message) => LastInformationMessage = message;
        public void ShowWarning(string title, string message) { }
        public void ShowError(string title, string message) { }
        public bool Confirm(string title, string message)
        {
            LastConfirmationMessage = message;
            return true;
        }
        public EditorDialogChoice AskYesNoCancel(string title, string message) => DownloadChoice;
        public DocumentImportConflictResolution ResolveDocumentImportConflict(string documentType, string documentName)
            => DocumentImportConflictResolution.Cancel;
    }

    private sealed class FakeActionCatalogService : IEditorActionCatalog
    {
        public ActionCatalogResponse Current { get; } = new();
        public int RefreshCount { get; private set; }
        public string? GetCachedIconUri(ActionAssetReferenceDto? icon) => null;
        public Task<ActionCatalogResponse> RefreshAsync(CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            return Task.FromResult(Current);
        }
    }

    private sealed class FakeRuntimeClient : IRuntimeApiClient
    {
        public event EventHandler<WorkflowRuntimeEventDto>? RuntimeEventReceived { add { } remove { } }
        public event EventHandler<ActionCatalogChangedDto>? ActionCatalogChanged { add { } remove { } }
        public event EventHandler<RuntimeConnectionChangedEventArgs>? ConnectionStateChanged { add { } remove { } }

        public long? PublishedExpectedRevision { get; private set; }
        public JsonNode? PublishedWorkflow { get; private set; }
        public SharpScriptDocumentDto? PublishedScript { get; private set; }
        public long? PublishedScriptExpectedRevision { get; private set; }
        public int WorkflowDownloadCount { get; private set; }
        public int WorkflowValidationCount { get; private set; }
        public bool HasActiveProject { get; set; } = true;
        public Guid? PublishedProjectId { get; private set; }
        public ProjectDeploymentScope? PublishedDeploymentScope { get; private set; }
        public Exception? PublishException { get; set; }
        public Exception? WorkflowDownloadException { get; set; }
        public bool ApplyPublishedWorkflowToRuntime { get; set; } = true;
        public bool HideScriptActionsFromCatalog { get; set; }
        public WorkflowPublishResponse PublishResponse { get; set; } = new()
        {
            WorkflowId = WorkflowRuntimeDefaults.DefaultWorkflowId,
            Revision = 2,
            ContentHash = "published-hash"
        };
        public WorkflowDocumentResponse WorkflowResponse { get; set; } = new()
        {
            WorkflowId = WorkflowRuntimeDefaults.DefaultWorkflowId,
            Workflow = new JsonObject { ["name"] = "Runtime" },
            Revision = 1,
            ContentHash = "hash-1"
        };
        public SharpScriptDocumentResponse? ScriptResponse { get; set; }
        public SharpScriptPublishResponse ScriptPublishResponse { get; set; } = new()
        {
            WorkflowId = WorkflowRuntimeDefaults.DefaultWorkflowId,
            WorkflowRevision = 2,
            ScriptUid = Guid.Empty,
            ScriptRevision = 1,
            Succeeded = true
        };

        public Uri ResolveRuntimeUri(string relativeUri) => new("http://localhost/" + relativeUri.TrimStart('/'));
        public Task ConnectEventsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ActionCatalogResponse> GetActionCatalogAsync(CancellationToken cancellationToken = default)
        {
            if (HideScriptActionsFromCatalog)
            {
                return Task.FromResult(new ActionCatalogResponse());
            }

            var project = new JsonEditorDocumentPersistence(new WorkflowEditorJsonSerializer())
                .Deserialize(WorkflowResponse.Workflow.ToJsonString());
            return Task.FromResult(new ActionCatalogResponse
            {
                Actions = project.Scripts.Select(script => new WorkflowActionDescriptorDto
                {
                    ActionId = $"csharp-script:{script.Uid:D}",
                    ActionType = $"csharp-script:{script.Uid:N}",
                    DisplayName = script.Name,
                    Category = "CSharp Scripts",
                    SourceKind = "CSharpScript"
                }).ToArray()
            });
        }
        public Task<byte[]> GetActionAssetAsync(ActionAssetReferenceDto asset, CancellationToken cancellationToken = default)
            => Task.FromResult(Array.Empty<byte>());
        public Task<WorkflowDocumentResponse> GetWorkflowAsync(string workflowId, CancellationToken cancellationToken = default)
        {
            WorkflowDownloadCount++;
            return WorkflowDownloadException == null
                ? Task.FromResult(WorkflowResponse)
                : Task.FromException<WorkflowDocumentResponse>(WorkflowDownloadException);
        }
        public Task<ActiveProjectIdentityResponse?> GetActiveProjectIdentityAsync(
            string workflowId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<ActiveProjectIdentityResponse?>(HasActiveProject
                ? new ActiveProjectIdentityResponse
                {
                    WorkflowId = workflowId,
                    ProjectId = WorkflowResponse.ProjectId,
                    Revision = WorkflowResponse.Revision,
                    ContentHash = WorkflowResponse.ContentHash
                }
                : null);
        public Task<SharpScriptDocumentResponse> GetSharpScriptAsync(
            string workflowId,
            Guid scriptUid,
            CancellationToken cancellationToken = default)
            => ScriptResponse != null
                ? Task.FromResult(ScriptResponse)
                : PublishedScript?.Uid == scriptUid
                    ? Task.FromResult(new SharpScriptDocumentResponse
                    {
                        WorkflowId = workflowId,
                        WorkflowRevision = ScriptPublishResponse.WorkflowRevision,
                        Script = PublishedScript,
                        ScriptRevision = ScriptPublishResponse.ScriptRevision,
                        SourceHash = ScriptPublishResponse.SourceHash,
                        ContractHash = ScriptPublishResponse.ContractHash,
                        AssemblyHash = ScriptPublishResponse.AssemblyHash,
                        ActionId = ScriptPublishResponse.ActionId,
                        ActionType = ScriptPublishResponse.ActionType
                    })
                    : Task.FromException<SharpScriptDocumentResponse>(new RuntimeApiException(
                        HttpStatusCode.NotFound,
                        string.Empty));
        public Task<SharpScriptPublishResponse> PublishSharpScriptAsync(
            string workflowId,
            SharpScriptDocumentDto script,
            long expectedWorkflowRevision,
            IReadOnlyList<SharpScriptLibraryReferenceDto> libraries,
            CancellationToken cancellationToken = default)
        {
            PublishedScript = script;
            PublishedScriptExpectedRevision = expectedWorkflowRevision;
            if (ScriptPublishResponse.Succeeded)
            {
                var persistence = new JsonEditorDocumentPersistence(new WorkflowEditorJsonSerializer());
                var project = persistence.Deserialize(WorkflowResponse.Workflow.ToJsonString());
                var scriptIndex = project.Scripts.FindIndex(item => item.Uid == script.Uid);
                var workflowScript = new WorkflowScript
                {
                    Uid = script.Uid,
                    Name = script.Name,
                    Language = script.Language,
                    Content = script.Content
                };
                if (scriptIndex >= 0)
                {
                    project.Scripts[scriptIndex] = workflowScript;
                }
                else
                {
                    project.Scripts.Add(workflowScript);
                }

                project.ScriptLibraries = libraries.Select(item => new SharpScriptLibraryReferenceDto
                {
                    LibraryId = item.LibraryId,
                    Version = item.Version
                }).ToList();
                WorkflowResponse = new WorkflowDocumentResponse
                {
                    WorkflowId = workflowId,
                    ProjectId = WorkflowResponse.ProjectId,
                    Workflow = JsonNode.Parse(persistence.Serialize(project))!,
                    Revision = ScriptPublishResponse.WorkflowRevision,
                    ContentHash = WorkflowResponse.ContentHash
                };
            }
            return Task.FromResult(ScriptPublishResponse);
        }
        public Task<SharpScriptPublishResponse> PublishSharpScriptAsync(
            string workflowId,
            Guid projectId,
            SharpScriptDocumentDto script,
            long expectedWorkflowRevision,
            IReadOnlyList<SharpScriptLibraryReferenceDto> libraries,
            CancellationToken cancellationToken = default)
        {
            PublishedProjectId = projectId;
            PublishedDeploymentScope = ProjectDeploymentScope.CurrentScript;
            return PublishSharpScriptAsync(
                workflowId,
                script,
                expectedWorkflowRevision,
                libraries,
                cancellationToken);
        }
        public Task<WorkflowValidationResponse> ValidateAsync(JsonNode workflow, CancellationToken cancellationToken = default)
        {
            WorkflowValidationCount++;
            return Task.FromResult(new WorkflowValidationResponse { IsValid = true });
        }
        public Task<WorkflowPublishResponse> PublishWorkflowAsync(
            string workflowId,
            JsonNode workflow,
            long expectedRevision,
            CancellationToken cancellationToken = default)
        {
            PublishedExpectedRevision = expectedRevision;
            PublishedWorkflow = workflow.DeepClone();
            if (PublishException != null)
            {
                return Task.FromException<WorkflowPublishResponse>(PublishException);
            }

            var publishedRevision = PublishResponse.Revision > expectedRevision
                ? PublishResponse.Revision
                : expectedRevision + 1;
            var publishedProjectId = PublishResponse.ProjectId != Guid.Empty
                ? PublishResponse.ProjectId
                : PublishedProjectId ?? WorkflowResponse.ProjectId;
            var response = new WorkflowPublishResponse
            {
                WorkflowId = PublishResponse.WorkflowId,
                ProjectId = publishedProjectId,
                Revision = publishedRevision,
                ContentHash = PublishResponse.ContentHash
            };
            if (ApplyPublishedWorkflowToRuntime)
            {
                WorkflowResponse = new WorkflowDocumentResponse
                {
                    WorkflowId = response.WorkflowId,
                    ProjectId = publishedProjectId,
                    Workflow = workflow.DeepClone(),
                    Revision = publishedRevision,
                    ContentHash = response.ContentHash
                };
            }
            return Task.FromResult(response);
        }
        public Task<WorkflowPublishResponse> PublishWorkflowAsync(
            string workflowId,
            Guid projectId,
            ProjectDeploymentScope deploymentScope,
            JsonNode workflow,
            long expectedRevision,
            CancellationToken cancellationToken = default)
        {
            PublishedProjectId = projectId;
            PublishedDeploymentScope = deploymentScope;
            return PublishWorkflowAsync(workflowId, workflow, expectedRevision, cancellationToken);
        }
        public Task<Guid> StartPreviewRunAsync(JsonNode workflow, Guid? methodUid, string? methodName, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<Guid> StartPublishedRunAsync(string workflowId, Guid? methodUid, string? methodName, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<WorkflowRunStatusResponse> GetRunStatusAsync(Guid runId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task CancelRunAsync(Guid runId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public void Dispose() { }
    }
}
