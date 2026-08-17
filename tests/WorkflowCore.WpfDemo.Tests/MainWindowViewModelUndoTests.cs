using System.Text.Json.Nodes;
using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Models;
using WorkflowCore.WpfDemo.Services;
using WorkflowCore.WpfDemo.Services.Projects;
using WorkflowCore.WpfDemo.Services.Scripting;
using WorkflowCore.WpfDemo.Services.Ui;
using WorkflowCore.WpfDemo.ViewModels;
using WorkflowRuntime.Contracts;
using WorkflowRuntime.ScriptCompiler;
using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class MainWindowViewModelUndoTests
{
    [Fact]
    public void Navigation_ExposesMethodsAndScriptsAsIndependentPeerItems()
    {
        using var viewModel = CreateViewModel();

        Assert.Collection(
            viewModel.HamburgerMenuItems,
            methods =>
            {
                Assert.Equal("Methods", methods.Key);
                Assert.Equal(DocumentIconKeys.Method, methods.IconKey);
            },
            scripts =>
            {
                Assert.Equal("CSharpScripts", scripts.Key);
                Assert.Equal(DocumentIconKeys.CSharpScript, scripts.IconKey);
            });

        viewModel.SelectHamburgerMenuCommand.Execute(viewModel.HamburgerMenuItems[0]);
        Assert.True(viewModel.IsSubmenuOpen);
        Assert.True(viewModel.IsMethodsSubmenuOpen);
        Assert.False(viewModel.IsScriptsSubmenuOpen);

        viewModel.SelectHamburgerMenuCommand.Execute(viewModel.HamburgerMenuItems[1]);
        Assert.True(viewModel.IsSubmenuOpen);
        Assert.False(viewModel.IsMethodsSubmenuOpen);
        Assert.True(viewModel.IsScriptsSubmenuOpen);

        viewModel.OpenMethodCommand.Execute(viewModel.Methods[0]);
        Assert.False(viewModel.IsSubmenuOpen);
    }

    [Fact]
    public void RuntimeDownload_PersistsAtomicallyToTheOpenedProjectFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "workflow-runtime-download-tests", Guid.NewGuid().ToString("N"));
        var filePath = Path.Combine(directory, "Downloaded.workflow.json");
        try
        {
            var persistence = new JsonEditorDocumentPersistence(new WorkflowEditorJsonSerializer());
            var fileService = new WorkflowProjectFileService(persistence);
            var localProject = EditorTestProjectFactory.Create();
            localProject.Name = "Local";
            fileService.Save(filePath, localProject);
            using var viewModel = CreateViewModel(
                draftStore: new EmptyDraftStore(),
                openedProject: localProject,
                projectFileService: fileService,
                projectFilePath: filePath);
            var downloadedProject = persistence.Deserialize(persistence.Serialize(localProject));
            downloadedProject.Name = "Runtime download";

            var saved = viewModel.PersistRuntimeDownloadToProjectFile(downloadedProject);

            Assert.True(saved);
            Assert.Equal("Runtime download", fileService.Open(filePath).Project.Name);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void CreateCSharpScript_UsesProjectCollectionTemplateAndSingleDockDocument()
    {
        using var viewModel = CreateViewModel();
        var originalMethodCount = viewModel.Project.Methods.Count;

        viewModel.SelectCreateItemCommand.Execute("CSharpScript");
        viewModel.NewMethodName = "ExtractDigits";
        viewModel.ConfirmCreateMethodCommand.Execute(null);

        var script = Assert.Single(viewModel.Project.Scripts, item => item.Name == "ExtractDigits");
        Assert.Contains(script, viewModel.Scripts);
        Assert.Equal(originalMethodCount, viewModel.Project.Methods.Count);
        Assert.Contains("IWorkflowSharpScript", script.Content, StringComparison.Ordinal);
        var pane = Assert.Single(viewModel.OpenedEditors, item => item.ContentId == $"script:{script.Uid:N}");
        Assert.Equal("ExtractDigits.csx *", pane.Title);
        Assert.Equal(DocumentIconKeys.CSharpScript, pane.IconKey);

        viewModel.SelectHamburgerMenuCommand.Execute(viewModel.HamburgerMenuItems[1]);
        viewModel.OpenScriptCommand.Execute(script);

        Assert.False(viewModel.IsSubmenuOpen);
        Assert.Single(viewModel.OpenedEditors, item => item.ContentId == $"script:{script.Uid:N}");
    }

    [Fact]
    public void CSharpScriptUndo_RestoresThePreviousEditorContent()
    {
        using var viewModel = CreateViewModel();
        viewModel.SelectCreateItemCommand.Execute("CSharpScript");
        viewModel.NewMethodName = "UndoScript";
        viewModel.ConfirmCreateMethodCommand.Execute(null);
        var editor = Assert.IsType<CSharpScriptEditorViewModel>(viewModel.SelectedDockPane!.Content);
        var originalContent = editor.Content;

        editor.Content += Environment.NewLine + "// unsaved change";

        Assert.True(viewModel.UndoCommand.CanExecute(editor));
        viewModel.UndoCommand.Execute(editor);
        Assert.Equal(originalContent, editor.Content);
    }

    [Fact]
    public async Task PublishedScriptAction_RefreshesToolboxAndCanBeInsertedIntoMethod()
    {
        var scriptUid = Guid.NewGuid();
        var actionId = $"csharp-script:{scriptUid:D}";
        var actionType = $"csharp-script:{scriptUid:N}";
        var actionCatalog = new PublishedScriptActionCatalog(new WorkflowActionDescriptorDto
        {
            ActionId = actionId,
            ActionType = actionType,
            DisplayName = "Extract Digits",
            Category = "CSharp Scripts"
        });
        using var viewModel = CreateViewModel(actionCatalog: actionCatalog);
        viewModel.Project.Scripts.Add(new WorkflowScript
        {
            Uid = scriptUid,
            Name = "Extract Digits"
        });
        var publication = new SharpScriptPublishResponse
        {
            WorkflowId = WorkflowRuntimeDefaults.DefaultWorkflowId,
            WorkflowRevision = 2,
            ScriptUid = scriptUid,
            ScriptRevision = 1,
            ActionId = actionId,
            ActionType = actionType,
            Succeeded = true
        };

        await viewModel.RefreshActionToolboxAfterScriptPublicationAsync(publication);

        var category = Assert.Single(viewModel.ActionToolbox, item => item.DisplayName == "CSharp Scripts");
        var template = Assert.Single(category.Children);
        Assert.Equal(actionType, template.ActionType);

        var method = viewModel.Project.Methods.First();
        viewModel.SelectedMethod = method;
        viewModel.AddActionFromToolbox(actionType);

        var inserted = Assert.Single(method.MethodLines, line => line.Action?.ActionType == actionType);
        Assert.Equal(actionId, inserted.Action!.ActionId);
    }

    [Fact]
    public async Task PublishedProject_RefreshesEveryScriptActionInToolbox()
    {
        var first = new WorkflowScript { Uid = Guid.NewGuid(), Name = "Statistics" };
        var second = new WorkflowScript { Uid = Guid.NewGuid(), Name = "Cubic Solver" };
        var actionCatalog = new PublishedScriptActionCatalog(
            CreatePublishedScriptDescriptor(first),
            CreatePublishedScriptDescriptor(second));
        using var viewModel = CreateViewModel(actionCatalog: actionCatalog);
        viewModel.Project.Scripts.Add(first);
        viewModel.Project.Scripts.Add(second);

        await viewModel.RefreshActionToolboxAfterProjectPublicationAsync([first, second]);

        var category = Assert.Single(viewModel.ActionToolbox, item => item.DisplayName == "CSharp Scripts");
        Assert.Equal(2, category.Children.Count);
        Assert.Contains(category.Children, item => item.ActionType == $"csharp-script:{first.Uid:N}");
        Assert.Contains(category.Children, item => item.ActionType == $"csharp-script:{second.Uid:N}");
    }

    private static WorkflowActionDescriptorDto CreatePublishedScriptDescriptor(WorkflowScript script)
        => new()
        {
            ActionId = $"csharp-script:{script.Uid:D}",
            ActionType = $"csharp-script:{script.Uid:N}",
            DisplayName = script.Name,
            Category = "CSharp Scripts",
            SourceKind = "CSharpScript",
            SourceId = script.Uid.ToString("D")
        };

    [Fact]
    public void PropertyPanelDeactivate_ThenUndo_PreservesEveryMethodLine()
    {
        var viewModel = CreateViewModel();
        var method = viewModel.Project.Methods.First(candidate => candidate.MethodLines.Count >= 2);
        viewModel.OpenMethod(method);
        viewModel.SelectedMethodLine = method.MethodLines[0];
        var originalLineIds = method.MethodLines.Select(line => line.Uid).ToArray();
        var deactivate = viewModel.SelectedActionProperties.Single(property => property.Name == "Deactivate");

        deactivate.BooleanValue = true;
        Assert.False(method.MethodLines[0].IsActive);
        Assert.True(viewModel.UndoCommand.CanExecute(null));

        viewModel.UndoCommand.Execute(null);

        Assert.Equal(originalLineIds, method.MethodLines.Select(line => line.Uid));
        Assert.True(method.MethodLines[0].IsActive);
        Assert.True(method.MethodLines[0].Action!.IsActive);
    }

    [Fact]
    public void PropertyVariableClearAndCreate_AreTwoAtomicUndoSteps()
    {
        var viewModel = CreateViewModel(actionCatalog: new MethodActionCatalog());
        var method = viewModel.Project.Methods.Single(candidate => candidate.Name == "Main");
        viewModel.OpenMethod(method);
        viewModel.SelectedMethodLine = method.MethodLines.Single(line => line.LineNo == 10);
        var action = viewModel.SelectedMethodLine.Action!;
        var property = viewModel.SelectedActionProperties.Single(item => item.Name == "VariableName");
        var originalVariableName = property.ValueText;
        var originalVariableNames = method.MethodVariables.Select(item => item.VariableName).ToArray();

        viewModel.ClearPropertyValueCommand.Execute(property);
        Assert.Null(action.GetProperty("VariableName"));

        viewModel.CreatePropertyValueCommand.Execute(property);
        var createdVariableName = action.GetProperty("VariableName")?.GetValue<string>();
        Assert.False(string.IsNullOrWhiteSpace(createdVariableName));
        Assert.NotEqual(originalVariableName, createdVariableName);
        Assert.Contains(method.MethodVariables, item => item.VariableName == createdVariableName);

        viewModel.UndoCommand.Execute(null);

        Assert.Null(method.MethodLines.Single(line => line.LineNo == 10).Action!.GetProperty("VariableName"));
        Assert.Equal(originalVariableNames, method.MethodVariables.Select(item => item.VariableName));

        viewModel.UndoCommand.Execute(null);

        Assert.Equal(
            originalVariableName,
            method.MethodLines.Single(line => line.LineNo == 10).Action!.GetProperty("VariableName")?.GetValue<string>());
        Assert.Equal(originalVariableNames, method.MethodVariables.Select(item => item.VariableName));
    }

    [Fact]
    public void ContextMenuDeactivate_ThenUndo_PreservesEveryMethodLine()
    {
        var viewModel = CreateViewModel();
        var method = viewModel.Project.Methods.First(candidate => candidate.MethodLines.Count >= 2);
        viewModel.OpenMethod(method);
        viewModel.SelectedMethodLine = method.MethodLines[0];
        var originalLineIds = method.MethodLines.Select(line => line.Uid).ToArray();

        viewModel.DeactivateLineCommand.Execute(null);
        Assert.False(method.MethodLines[0].IsActive);
        Assert.True(viewModel.UndoCommand.CanExecute(null));

        viewModel.UndoCommand.Execute(null);

        Assert.Equal(originalLineIds, method.MethodLines.Select(line => line.Uid));
        Assert.True(method.MethodLines[0].IsActive);
        Assert.True(method.MethodLines[0].Action!.IsActive);
    }

    [Fact]
    public void VisibleEditorUndo_DoesNotUseAStaleAvalonDockSelection()
    {
        var viewModel = CreateViewModel();
        var method = viewModel.Project.Methods.First(candidate => candidate.MethodLines.Count >= 2);
        viewModel.OpenMethod(method);
        var visibleEditor = Assert.IsType<MethodEditorViewModel>(viewModel.SelectedDockPane!.Content);
        viewModel.SelectedMethodLine = method.MethodLines[0];
        var originalLineIds = method.MethodLines.Select(line => line.Uid).ToArray();
        var deactivate = viewModel.SelectedActionProperties.Single(property => property.Name == "Deactivate");
        deactivate.BooleanValue = true;

        var otherMethod = viewModel.Project.Methods.First(candidate => candidate.Uid != method.Uid);
        viewModel.OpenMethod(otherMethod);
        Assert.NotSame(visibleEditor, viewModel.SelectedDockPane!.Content);

        Assert.True(viewModel.UndoCommand.CanExecute(visibleEditor));
        viewModel.UndoCommand.Execute(visibleEditor);

        Assert.Equal(originalLineIds, method.MethodLines.Select(line => line.Uid));
        Assert.True(method.MethodLines[0].IsActive);
        Assert.True(method.MethodLines[0].Action!.IsActive);
    }

    [Fact]
    public void DeactivateUndo_DoesNotResetTheMethodsListOrLoseTheCurrentMethod()
    {
        var viewModel = CreateViewModel();
        var method = viewModel.Project.Methods.First(candidate => candidate.MethodLines.Count >= 2);
        viewModel.OpenMethod(method);
        var editor = Assert.IsType<MethodEditorViewModel>(viewModel.SelectedDockPane!.Content);
        viewModel.SelectedMethodLine = method.MethodLines[0];
        var methodsListWasReset = false;
        viewModel.Methods.CollectionChanged += (_, args) =>
        {
            if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                methodsListWasReset = true;
                viewModel.SelectedMethod = null;
            }
        };

        viewModel.SelectedActionProperties
            .Single(property => property.Name == "Deactivate")
            .BooleanValue = true;
        viewModel.UndoCommand.Execute(editor);

        Assert.False(methodsListWasReset);
        Assert.Same(method, viewModel.SelectedMethod);
        Assert.Equal(method.MethodLines.Count, viewModel.VisibleMethodLineItems.Count);
    }

    [Fact]
    public void CloseEditor_IsBlockedWhileAnyDocumentIsUnsaved()
    {
        var viewModel = CreateViewModel();
        var method = viewModel.Project.Methods.First(candidate => candidate.MethodLines.Count >= 2);
        viewModel.OpenMethod(method);
        viewModel.SelectedMethodLine = method.MethodLines[0];
        viewModel.DeactivateLineCommand.Execute(null);

        Assert.False(viewModel.CanCloseEditor());

        viewModel.UndoCommand.Execute(null);
        Assert.True(viewModel.CanCloseEditor());
    }

    [Fact]
    public void DeletedMethod_IsCommittedBySaveProjectAndNoLongerBlocksClosing()
    {
        var dialogs = new RecordingDialogs(confirmResult: true);
        var viewModel = CreateViewModel(dialogs: dialogs);
        var deletedMethod = viewModel.Project.Methods.Single(method => method.Name == "Worker");

        viewModel.DeleteMethodCommand.Execute(deletedMethod);

        Assert.DoesNotContain(viewModel.Project.Methods, method => method.Uid == deletedMethod.Uid);
        Assert.True(viewModel.SaveAllWorkflowCommand.CanExecute(null));
        Assert.False(viewModel.CanCloseEditor());
        Assert.Contains("Save project", dialogs.LastWarningMessage);
        Assert.Contains("Worker (deleted)", dialogs.LastWarningMessage);

        viewModel.SaveAllWorkflowCommand.Execute(null);

        Assert.True(viewModel.CanCloseEditor());
    }

    [Fact]
    public void StartupWithoutLocalDraft_CreatesAnEmptyLocalWorkflow()
    {
        var draftStore = new EmptyDraftStore();
        var viewModel = CreateViewModel(draftStore);

        Assert.Equal("Untitled Project", viewModel.Project.Name);
        Assert.Empty(viewModel.Project.Methods);
        Assert.Empty(viewModel.OpenedEditors);
        Assert.Null(viewModel.SelectedMethod);
        Assert.Equal(1, draftStore.SaveCount);
    }

    [Fact]
    public void StartupWithLocalDraft_OpensTheLocalWorkflow()
    {
        var serializer = new WorkflowEditorJsonSerializer();
        var localProject = new WorkflowProject
        {
            Name = "My Local Project",
            Version = "2.0",
            Methods = [new WorkflowMethod { Name = "Local Main" }]
        };

        var viewModel = CreateViewModel(new MemoryDraftStore(serializer.SerializeToNode(localProject)));

        Assert.Equal("My Local Project", viewModel.Project.Name);
        Assert.Equal("2.0", viewModel.Project.Version);
        Assert.Equal("Local Main", Assert.Single(viewModel.Project.Methods).Name);
    }

    [Fact]
    public void OpenedProject_PreservesMethodsAndScriptsAndOpensItsFirstMethod()
    {
        var method = new WorkflowMethod { Name = "Loaded Method" };
        var script = new WorkflowScript { Name = "Loaded Script", Content = "// loaded" };
        var project = new WorkflowProject
        {
            Name = "Loaded Project",
            Version = "1.0",
            Methods = [method],
            Scripts = [script]
        };

        using var viewModel = CreateViewModel(openedProject: project);

        Assert.Same(project, viewModel.Project);
        Assert.Same(method, Assert.Single(viewModel.Methods));
        Assert.Same(script, Assert.Single(viewModel.Scripts));
        var document = Assert.Single(viewModel.OpenedEditors);
        Assert.Equal($"method:{method.Uid:N}", document.ContentId);
        Assert.Same(method, viewModel.SelectedMethod);
    }

    [Fact]
    public void OpenedScriptOnlyProject_OpensItsFirstScript()
    {
        var script = new WorkflowScript { Name = "Only Script", Content = "// loaded" };
        var project = new WorkflowProject
        {
            Name = "Script Project",
            Version = "1.0",
            Scripts = [script]
        };

        using var viewModel = CreateViewModel(openedProject: project);

        Assert.Empty(viewModel.Methods);
        Assert.Same(script, Assert.Single(viewModel.Scripts));
        var document = Assert.Single(viewModel.OpenedEditors);
        Assert.Equal($"script:{script.Uid:N}", document.ContentId);
        Assert.Null(viewModel.SelectedMethod);
    }

    [Fact]
    public void OpenedProject_ResolvesItsScriptActionsWithoutUsingAnotherRuntimeProjectScripts()
    {
        var localScript = new WorkflowScript { Uid = Guid.NewGuid(), Name = "MathTest" };
        var remoteScript = new WorkflowScript { Uid = Guid.NewGuid(), Name = "Other Project Script" };
        var localAction = WorkflowAction.Create(
            $"csharp-script:{localScript.Uid:D}",
            $"csharp-script:{localScript.Uid:N}");
        var method = new WorkflowMethod
        {
            Name = "DemoTest",
            MethodLines = [MethodLine.Create(10, 0, localAction)]
        };
        var project = new WorkflowProject
        {
            Name = "DemoTest",
            Methods = [method],
            Scripts = [localScript]
        };
        var runtimeCatalog = new StaticScriptActionCatalog(CreatePublishedScriptDescriptor(remoteScript));

        using var viewModel = CreateViewModel(actionCatalog: runtimeCatalog, openedProject: project);

        var line = Assert.Single(viewModel.VisibleMethodLineItems);
        Assert.Equal("MathTest", line.DisplayName);
        Assert.True(line.Line.IsActionAvailable);
        var scriptsCategory = Assert.Single(
            viewModel.ActionToolbox,
            category => category.DisplayName == "CSharp Scripts");
        var scriptTemplate = Assert.Single(scriptsCategory.Children);
        Assert.Equal(localScript.Name, scriptTemplate.DisplayName);
        Assert.DoesNotContain(
            scriptsCategory.Children,
            template => template.DisplayName == remoteScript.Name);
    }

    [Fact]
    public void ConfigureMethodVariables_OpensTheExplicitMethodContractEditor()
    {
        var viewModel = CreateViewModel();
        var method = viewModel.Project.Methods.Single(item => item.Name == "Worker");
        viewModel.OpenMethod(method);

        viewModel.ConfigureMethodVariablesCommand.Execute(null);

        Assert.True(viewModel.IsMethodVariablesDialogOpen);
        Assert.Contains(viewModel.SelectedMethodInputs, input =>
            input.Name == "input" && input.VariableName == "_$input");
        Assert.Contains(viewModel.SelectedMethodOutputs, output =>
            output.Name == "workerResult" && output.VariableName == "_$0workerResult");

        viewModel.CloseMethodVariablesCommand.Execute(null);
        Assert.False(viewModel.IsMethodVariablesDialogOpen);
    }

    [Fact]
    public void MethodContractEditor_CanDeclareInputsAndOutputsOnAnEmptySignature()
    {
        var viewModel = CreateViewModel();
        var method = viewModel.Project.Methods.Single(item => item.Name == "Main");
        viewModel.OpenMethod(method);

        viewModel.AddMethodInputCommand.Execute(null);
        viewModel.AddMethodOutputCommand.Execute(null);

        var input = Assert.Single(method.Inputs);
        var output = Assert.Single(method.Outputs);
        Assert.Equal("input", input.Name);
        Assert.Equal("_$input", input.VariableName);
        Assert.Equal("result", output.Name);
        Assert.Equal("_$0result", output.VariableName);
        Assert.Contains(method.MethodVariables, variable => variable.VariableName == input.VariableName);
        Assert.Contains(method.MethodVariables, variable => variable.VariableName == output.VariableName);
    }

    [Fact]
    public void RunMethodSelection_RebuildsInputAndReturnMappingsFromTheTargetSignature()
    {
        var viewModel = CreateViewModel(actionCatalog: new MethodActionCatalog());
        var caller = viewModel.Project.Methods.Single(method => method.Name == "Main");
        var action = WorkflowAction.Create("runMethod");
        action.SetProperty("MethodName", JsonValue.Create(string.Empty));
        var line = MethodLine.Create(30, 0, action);
        caller.MethodLines.Add(line);
        viewModel.OpenMethod(caller);
        viewModel.SelectedMethodLine = line;

        var methodProperty = viewModel.SelectedActionProperties.Single(property => property.Name == "MethodName");
        Assert.Equal(["Background", "Main", "Worker"], methodProperty.Suggestions);

        methodProperty.ValueText = "Worker";

        Assert.DoesNotContain(viewModel.SelectedActionProperties, property => property.Name == "Parameters");
        Assert.DoesNotContain(viewModel.SelectedActionProperties, property => property.Name == "ReturnVarNames");
        Assert.Contains(viewModel.SelectedActionProperties, property => property.Name == "Parameters.input");
        Assert.Contains(viewModel.SelectedActionProperties, property => property.Name == "ReturnVarNames.workerResult");
    }

    [Fact]
    public void MethodContractChanges_UpdateExistingCallMappingsAtomically()
    {
        var viewModel = CreateViewModel(actionCatalog: new MethodActionCatalog());
        var caller = viewModel.Project.Methods.Single(method => method.Name == "Main");
        var worker = viewModel.Project.Methods.Single(method => method.Name == "Worker");
        var action = WorkflowAction.Create("runMethod");
        action.SetProperty("MethodName", JsonValue.Create("Worker"));
        action.SetProperty("Parameters", new JsonObject { ["input"] = "_$0x" });
        action.SetProperty("ReturnVarNames", JsonValue.Create("_$0workerResult"));
        caller.MethodLines.Add(MethodLine.Create(30, 0, action));
        viewModel.OpenMethod(worker);

        var input = Assert.Single(viewModel.SelectedMethodInputs);
        input.Name = "value";

        Assert.Equal("_$value", input.VariableName);
        Assert.Contains(worker.MethodVariables, variable => variable.VariableName == "_$value");
        Assert.DoesNotContain(worker.MethodVariables, variable => variable.VariableName == "_$input");
        Assert.Equal(
            "_$value * 2",
            worker.MethodLines.Single(line => line.Action?.ActionType == "setVariable")
                .Action!.GetProperty("ValueExpression")?.GetValue<string>());
        var parameters = Assert.IsType<JsonObject>(action.GetProperty("Parameters"));
        Assert.True(parameters.ContainsKey("value"));
        Assert.False(parameters.ContainsKey("input"));

        var output = Assert.Single(viewModel.SelectedMethodOutputs);
        output.Name = "answer";
        Assert.Equal("_$0answer", output.VariableName);
        Assert.Contains(worker.MethodVariables, variable => variable.VariableName == "_$0answer");
        Assert.Equal(
            "_$0answer",
            worker.MethodLines.Single(line => line.Action?.ActionType == "return")
                .Action!.GetProperty("ReturnValues")?.GetValue<string>());

        viewModel.SelectedMethodOutput = output;
        viewModel.DeleteMethodOutputCommand.Execute(null);
        Assert.Equal(string.Empty, action.GetProperty("ReturnVarNames")?.GetValue<string>());
        Assert.Equal(
            string.Empty,
            worker.MethodLines.Single(line => line.Action?.ActionType == "return")
                .Action!.GetProperty("ReturnValues")?.GetValue<string>());

        viewModel.SelectedMethodInput = input;
        viewModel.DeleteMethodInputCommand.Execute(null);
        Assert.Empty(Assert.IsType<JsonObject>(action.GetProperty("Parameters")));
    }

    [Fact]
    public void MethodContractDefaultValue_UsesTheSelectedDataType()
    {
        var parameter = new WorkflowMethodParameter { ValueType = "number" };

        parameter.DefaultValueText = "10.5";

        Assert.Equal(10.5d, Assert.IsType<double>(parameter.DefaultValue));

        parameter.ValueType = "integer";
        parameter.DefaultValueText = "3";
        Assert.Equal(3L, Assert.IsType<long>(parameter.DefaultValue));

        parameter.ValueType = "boolean";
        parameter.DefaultValueText = "true";
        Assert.True(Assert.IsType<bool>(parameter.DefaultValue));

        parameter.ValueType = "object";
        parameter.DefaultValueText = "12";
        Assert.Equal(12L, Assert.IsType<long>(parameter.DefaultValue));

        parameter.ValueType = "string";
        parameter.DefaultValueText = "12";
        Assert.Equal("12", Assert.IsType<string>(parameter.DefaultValue));
    }

    private static MainWindowViewModel CreateViewModel(
        ILocalDraftStore? draftStore = null,
        IEditorActionCatalog? actionCatalog = null,
        IEditorDialogs? dialogs = null,
        WorkflowProject? openedProject = null,
        IWorkflowProjectFileService? projectFileService = null,
        string? projectFilePath = null)
    {
        var serializer = new WorkflowEditorJsonSerializer();
        var projectNode = serializer.SerializeToNode(EditorTestProjectFactory.Create());
        return new MainWindowViewModel(
            new MethodEditorFactory(),
            new ScriptEditorFactory(),
            new JsonEditorDocumentPersistence(serializer),
            new OfflineRuntimeClient(),
            actionCatalog ?? new EmptyActionCatalog(),
            draftStore ?? new MemoryDraftStore(projectNode),
            dialogs ?? new NonInteractiveDialogs(),
            new NoFileDialogs(),
            new DirectUiDispatcher(),
            new StoppedUiTimerFactory(),
            editorSession: openedProject == null ? null : new EditorSession(openedProject),
            projectFileService: projectFileService,
            projectFilePath: openedProject == null
                ? null
                : projectFilePath ?? @"C:\Projects\Loaded.workflow.json");
    }

    private sealed class EmptyDraftStore : ILocalDraftStore
    {
        public int SaveCount { get; private set; }

        public LocalDraftSnapshot? Load(string workflowId) => null;

        public LocalDraftSnapshot? LoadMostRecent() => null;

        public Task SaveAsync(
            string workflowId,
            JsonNode workflow,
            JsonNode savedWorkflow,
            bool isDirty,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class MethodEditorFactory : IMethodEditorViewModelFactory
    {
        public MethodEditorViewModel Create(WorkflowMethod method, MainWindowViewModel owner) => new(method, owner);

        public void Release(MethodEditorViewModel viewModel) => viewModel.Dispose();
    }

    private sealed class ScriptEditorFactory : ICSharpScriptEditorViewModelFactory
    {
        private readonly SharpScriptCompiler _compiler = new();
        private readonly SharpScriptLocalRunner _runner;

        public ScriptEditorFactory() => _runner = new SharpScriptLocalRunner(_compiler);

        public CSharpScriptEditorViewModel Create(WorkflowScript script, MainWindowViewModel owner)
            => new(script, owner, _compiler, _runner, new EmptyLibraryCache(), new DirectUiDispatcher());

        public void Release(CSharpScriptEditorViewModel viewModel)
        {
        }

        private sealed class EmptyLibraryCache : ISharpScriptLibraryCache
        {
            public Task<SharpScriptLibraryCatalogResponse> RefreshCatalogAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(new SharpScriptLibraryCatalogResponse());

            public Task<IReadOnlyList<string>> ResolveCompilationReferencesAsync(
                IReadOnlyList<SharpScriptLibraryReferenceDto> references,
                CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

            public bool IsLocallyAvailable(SharpScriptLibraryDescriptorDto library) => false;
        }
    }

    private sealed class MemoryDraftStore : ILocalDraftStore
    {
        private readonly JsonNode _project;

        public MemoryDraftStore(JsonNode project) => _project = project.DeepClone();

        public LocalDraftSnapshot? Load(string workflowId) => null;

        public LocalDraftSnapshot LoadMostRecent() => new()
        {
            WorkflowId = _project["projectId"]?.GetValue<string>() ?? Guid.NewGuid().ToString("D"),
            Workflow = _project.DeepClone(),
            SavedWorkflow = _project.DeepClone(),
            IsDirty = false,
            SavedAtUtc = DateTimeOffset.UtcNow
        };

        public Task SaveAsync(
            string workflowId,
            JsonNode workflow,
            JsonNode savedWorkflow,
            bool isDirty,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class EmptyActionCatalog : IEditorActionCatalog
    {
        public ActionCatalogResponse Current { get; } = new();

        public string? GetCachedIconUri(ActionAssetReferenceDto? icon) => null;

        public Task<ActionCatalogResponse> RefreshAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Current);
    }

    private sealed class PublishedScriptActionCatalog(params WorkflowActionDescriptorDto[] publishedActions)
        : IEditorActionCatalog
    {
        public ActionCatalogResponse Current { get; private set; } = new();

        public string? GetCachedIconUri(ActionAssetReferenceDto? icon) => null;

        public Task<ActionCatalogResponse> RefreshAsync(CancellationToken cancellationToken = default)
        {
            Current = new ActionCatalogResponse
            {
                CatalogVersion = "published-script",
                Actions = publishedActions
            };
            return Task.FromResult(Current);
        }
    }

    private sealed class StaticScriptActionCatalog(params WorkflowActionDescriptorDto[] actions)
        : IEditorActionCatalog
    {
        public ActionCatalogResponse Current { get; } = new()
        {
            CatalogVersion = "another-project",
            Actions = actions
        };

        public string? GetCachedIconUri(ActionAssetReferenceDto? icon) => null;

        public Task<ActionCatalogResponse> RefreshAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Current);
    }

    private sealed class MethodActionCatalog : IEditorActionCatalog
    {
        public ActionCatalogResponse Current { get; } = new()
        {
            Actions =
            [
                new WorkflowActionDescriptorDto
                {
                    ActionId = "runMethod",
                    ActionType = "runMethod",
                    DisplayName = "Run Method",
                    Category = "Methods",
                    Inputs =
                    [
                        new WorkflowActionFieldDto
                        {
                            Name = "MethodName",
                            DisplayName = "Method",
                            Category = "Action",
                            ValueType = "string",
                            Direction = "input",
                            Required = true,
                            Editor = "method",
                            Order = 0,
                            EditorOptions = new WorkflowActionEditorOptionsDto
                            {
                                DataSource = "methods",
                                AllowCustomValue = false,
                                AllowClear = true
                            }
                        },
                        new WorkflowActionFieldDto
                        {
                            Name = "Parameters",
                            DisplayName = "Method parameters",
                            Category = "Action",
                            ValueType = "object",
                            Direction = "input",
                            Editor = "json",
                            Order = 1
                        },
                        new WorkflowActionFieldDto
                        {
                            Name = "ReturnVarNames",
                            DisplayName = "Store return values in",
                            Category = "Action",
                            ValueType = "string",
                            Direction = "input",
                            Editor = "text",
                            Order = 2
                        }
                    ]
                },
                new WorkflowActionDescriptorDto
                {
                    ActionId = "setVariable",
                    ActionType = "setVariable",
                    DisplayName = "Set Variable",
                    Category = "Variables",
                    Inputs =
                    [
                        new WorkflowActionFieldDto
                        {
                            Name = "VariableName",
                            DisplayName = "Variable name",
                            ValueType = "string",
                            Direction = "input",
                            Editor = "variable",
                            EditorOptions = new WorkflowActionEditorOptionsDto
                            {
                                DataSource = "methodVariables",
                                AllowCustomValue = true,
                                AllowCreate = true,
                                AllowClear = true,
                                CreateKind = "variable:number"
                            }
                        },
                        new WorkflowActionFieldDto
                        {
                            Name = "ValueExpression",
                            DisplayName = "Value expression",
                            ValueType = "string",
                            Direction = "input",
                            Editor = "expression"
                        }
                    ]
                },
                new WorkflowActionDescriptorDto
                {
                    ActionId = "return",
                    ActionType = "return",
                    DisplayName = "Return",
                    Category = "Methods",
                    Inputs =
                    [
                        new WorkflowActionFieldDto
                        {
                            Name = "ReturnValues",
                            DisplayName = "Return values",
                            ValueType = "string",
                            Direction = "input",
                            Editor = "expression"
                        }
                    ]
                }
            ]
        };

        public string? GetCachedIconUri(ActionAssetReferenceDto? icon) => null;

        public Task<ActionCatalogResponse> RefreshAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Current);
    }

    private sealed class OfflineRuntimeClient : IRuntimeApiClient
    {
        private readonly TaskCompletionSource _neverConnect = new();

        public event EventHandler<WorkflowRuntimeEventDto>? RuntimeEventReceived;

        public event EventHandler<ActionCatalogChangedDto>? ActionCatalogChanged;

        public event EventHandler<RuntimeConnectionChangedEventArgs>? ConnectionStateChanged;

        public Uri ResolveRuntimeUri(string relativeUri) => new("http://127.0.0.1/" + relativeUri.TrimStart('/'));

        public Task ConnectEventsAsync(CancellationToken cancellationToken = default) => _neverConnect.Task;

        public Task<ActionCatalogResponse> GetActionCatalogAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<byte[]> GetActionAssetAsync(ActionAssetReferenceDto asset, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<WorkflowDocumentResponse> GetWorkflowAsync(string workflowId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SharpScriptDocumentResponse> GetSharpScriptAsync(string workflowId, Guid scriptUid, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SharpScriptPublishResponse> PublishSharpScriptAsync(string workflowId, SharpScriptDocumentDto script, long expectedWorkflowRevision, IReadOnlyList<SharpScriptLibraryReferenceDto> libraries, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<WorkflowValidationResponse> ValidateAsync(JsonNode workflow, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<WorkflowPublishResponse> PublishWorkflowAsync(
            string workflowId,
            JsonNode workflow,
            long expectedRevision,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Guid> StartPreviewRunAsync(
            JsonNode workflow,
            Guid? methodUid,
            string? methodName,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Guid> StartPublishedRunAsync(
            string workflowId,
            Guid? methodUid,
            string? methodName,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<WorkflowRunStatusResponse> GetRunStatusAsync(Guid runId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task CancelRunAsync(Guid runId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void Dispose()
        {
            _neverConnect.TrySetCanceled();
            _ = RuntimeEventReceived;
            _ = ActionCatalogChanged;
            _ = ConnectionStateChanged;
        }
    }

    private sealed class NonInteractiveDialogs : IEditorDialogs
    {
        public void ShowInformation(string title, string message) { }
        public void ShowWarning(string title, string message) { }
        public void ShowError(string title, string message) { }
        public bool Confirm(string title, string message) => false;
        public EditorDialogChoice AskYesNoCancel(string title, string message) => EditorDialogChoice.Cancel;
        public DocumentImportConflictResolution ResolveDocumentImportConflict(string documentType, string documentName)
            => DocumentImportConflictResolution.Cancel;
    }

    private sealed class RecordingDialogs(bool confirmResult) : IEditorDialogs
    {
        public string LastWarningMessage { get; private set; } = string.Empty;

        public void ShowInformation(string title, string message) { }

        public void ShowWarning(string title, string message) => LastWarningMessage = message;

        public void ShowError(string title, string message) { }

        public bool Confirm(string title, string message) => confirmResult;

        public EditorDialogChoice AskYesNoCancel(string title, string message) => EditorDialogChoice.Cancel;

        public DocumentImportConflictResolution ResolveDocumentImportConflict(string documentType, string documentName)
            => DocumentImportConflictResolution.Cancel;
    }

    private sealed class NoFileDialogs : IEditorFileDialogs
    {
        public string? SelectDocumentImportFile() => null;
        public string? SelectProjectImportFile() => null;
        public string? SelectDocumentExportPath(string documentName, string suggestedFileName) => null;
        public string? SelectProjectExportPath() => null;
    }

    private sealed class DirectUiDispatcher : IUiDispatcher
    {
        public bool HasShutdownStarted => false;
        public bool CheckAccess() => true;
        public void Post(Action action, UiDispatchPriority priority = UiDispatchPriority.Normal) => action();
        public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    private sealed class StoppedUiTimerFactory : IUiTimerFactory
    {
        public IUiTimer Create(TimeSpan interval) => new StoppedUiTimer();

        private sealed class StoppedUiTimer : IUiTimer
        {
            public event EventHandler? Tick { add { } remove { } }
            public void Start() { }
            public void Stop() { }
            public void Dispose() { }
        }
    }
}
