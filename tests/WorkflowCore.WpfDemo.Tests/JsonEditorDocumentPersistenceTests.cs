using System.Text.Json.Nodes;
using System.Text.Json;
using System.Collections.Concurrent;
using WorkflowCore.Errors;
using WorkflowCore.Execution;
using WorkflowCore.Serialization;
using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Services;
using WorkflowCore.WpfDemo.ViewModels;
using WorkflowRuntime.Contracts;
using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class JsonEditorDocumentPersistenceTests
{
    [Fact]
    public void SerializeAndDeserialize_PreserveEditorDocumentStructure()
    {
        var service = CreateService();
        var project = EditorTestProjectFactory.Create();
        var originalMethod = project.Methods.Single(method => method.Name == "Main");
        var originalLine = originalMethod.MethodLines.Single(line => line.Action?.ActionType == "setVariable" && line.Action.GetProperty("variableName")?.GetValue<string>() == "_$0x");

        var json = service.Serialize(project);
        var roundTripped = service.Deserialize(json);

        Assert.Equal(project.Name, roundTripped.Name);
        Assert.Equal(project.Methods.Count, roundTripped.Methods.Count);
        var method = roundTripped.Methods.Single(item => item.Uid == originalMethod.Uid);
        var line = method.MethodLines.Single(item => item.Uid == originalLine.Uid);
        Assert.Equal(originalLine.LineNo, line.LineNo);
        Assert.Equal(originalLine.NestingLevel, line.NestingLevel);
        Assert.Equal("setVariable", line.Action?.ActionType);
        Assert.Equal("0", line.Action?.GetProperty("valueExpression")?.GetValue<string>());
    }

    [Fact]
    public void ExportAndImport_RoundTripThroughFile()
    {
        var service = CreateService();
        var project = EditorTestProjectFactory.Create();
        var filePath = Path.Combine(Path.GetTempPath(), $"workflow-{Guid.NewGuid():N}.json");

        try
        {
            service.Export(project, filePath);
            var imported = service.Import(filePath);
            Assert.Equal(service.Serialize(project), service.Serialize(imported));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void ActionId_RoundTripsIndependentlyFromTheUiName()
    {
        var service = CreateService();
        var actionId = "0f6a1dc4-43a3-4e1e-96bb-6bfcb0c377f3";
        var project = new WorkflowProject { Name = "Stable action identity" };
        var method = new WorkflowMethod { Name = "Main" };
        method.MethodLines.Add(MethodLine.Create(10, 0, WorkflowAction.Create(actionId, "Greeting")));
        project.Methods.Add(method);

        var json = service.Serialize(project);
        var restored = service.Deserialize(json);
        var action = Assert.Single(Assert.Single(restored.Methods).MethodLines).Action!;

        Assert.Equal(actionId, action.ActionId);
        Assert.Equal("Greeting", action.ActionType);
        Assert.Contains("\"actionId\": \"0f6a1dc4-43a3-4e1e-96bb-6bfcb0c377f3\"", json);
    }

    [Fact]
    public void MethodDocument_RoundTripsWithoutOtherProjectDocuments()
    {
        var service = CreateService();
        var project = EditorTestProjectFactory.Create();
        var worker = project.Methods.Single(method => method.Name == "Worker");

        var json = service.SerializeDocument(WorkflowEditorDocument.FromMethod(worker));
        var restored = service.DeserializeDocument(json);

        Assert.Equal(WorkflowEditorDocumentKind.Method, restored.Kind);
        Assert.NotNull(restored.Method);
        Assert.Null(restored.Script);
        Assert.Equal(worker.Uid, restored.Method!.Uid);
        Assert.Equal(worker.Name, restored.Method.Name);
        Assert.Equal(worker.MethodLines.Count, restored.Method.MethodLines.Count);
        Assert.DoesNotContain("\"methods\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"scripts\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScriptDocument_RoundTripsWithoutOtherProjectDocuments()
    {
        var service = CreateService();
        var script = new WorkflowScript
        {
            Name = "PrepareSample",
            Language = "CSharp",
            Content = "return 42;"
        };

        var json = service.SerializeDocument(WorkflowEditorDocument.FromScript(script));
        var restored = service.DeserializeDocument(json);

        Assert.Equal(WorkflowEditorDocumentKind.CSharpScript, restored.Kind);
        Assert.NotNull(restored.Script);
        Assert.Null(restored.Method);
        Assert.Equal(script.Uid, restored.Script!.Uid);
        Assert.Equal(script.Content, restored.Script.Content);
    }

    [Fact]
    public void ExportAndImportDocument_RoundTripThroughFile()
    {
        var service = CreateService();
        var method = EditorTestProjectFactory.Create().Methods.Single(item => item.Name == "Worker");
        var filePath = Path.Combine(Path.GetTempPath(), $"workflow-document-{Guid.NewGuid():N}.json");

        try
        {
            service.ExportDocument(WorkflowEditorDocument.FromMethod(method), filePath);
            var imported = service.ImportDocument(filePath);

            Assert.Equal(WorkflowEditorDocumentKind.Method, imported.Kind);
            Assert.Equal(method.Uid, imported.Method?.Uid);
            Assert.Equal(method.Name, imported.Method?.Name);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void ImportDocument_RejectsProjectContainingMultipleDocuments()
    {
        var service = CreateService();
        var projectJson = service.Serialize(EditorTestProjectFactory.Create());

        var exception = Assert.Throws<JsonException>(() => service.DeserializeDocument(projectJson));

        Assert.Contains("exactly one", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImportProject_RejectsStandaloneDocumentInsteadOfReturningEmptyProject()
    {
        var service = CreateService();
        var method = EditorTestProjectFactory.Create().Methods.Single(item => item.Name == "Worker");
        var documentJson = service.SerializeDocument(WorkflowEditorDocument.FromMethod(method));

        var exception = Assert.Throws<JsonException>(() => service.Deserialize(documentJson));

        Assert.Contains("single workflow document", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DuplicateDocumentName_UsesFirstAvailableNumericSuffix()
    {
        var result = MainWindowViewModel.CreateUniqueDocumentName(
            "Lei",
            ["lei", "Lei(1)", "LEI(2)"]);

        Assert.Equal("Lei(3)", result);
    }

    [Fact]
    public void EditorJson_IsAcceptedByWorkflowCoreSerializer()
    {
        var service = CreateService();
        var project = EditorTestProjectFactory.Create();

        var coreProject = new WorkflowJsonSerializer().DeserializeProject(service.Serialize(project));

        Assert.Equal(project.Name, coreProject.Name);
        Assert.Equal(project.Methods.Select(method => method.Uid), coreProject.Methods.Select(method => method.Uid));
    }

    [Fact]
    public void ImportProject_RejectsMethodsWithoutExplicitContracts()
    {
        var service = CreateService();

        var exception = Assert.Throws<JsonException>(() => service.Deserialize("""
            {
              "name": "Pre-contract project",
              "methods": [
                {
                  "name": "Worker",
                  "methodVariables": [],
                  "methodLines": []
                }
              ]
            }
            """));

        Assert.Contains("explicit input/output contract", exception.Message);
    }

    [Fact]
    public void ImportProject_DoesNotInferAContractWhenExplicitArraysAreEmpty()
    {
        var service = CreateService();

        var project = service.Deserialize("""
            {
              "editorSchemaVersion": 2,
              "name": "Explicit private state",
              "methods": [
                {
                  "name": "Worker",
                  "inputs": [],
                  "outputs": [],
                  "methodVariables": [
                    { "variableName": "_$privateValue", "dataType": "number" }
                  ],
                  "methodLines": []
                }
              ]
            }
            """);

        var method = Assert.Single(project.Methods);
        Assert.Empty(method.Inputs);
        Assert.Empty(method.Outputs);
    }

    [Fact]
    public void UnknownAction_RoundTripPreservesAllJsonProperties()
    {
        var service = CreateService();
        var project = EditorTestProjectFactory.Create();
        var action = new WorkflowAction(JsonNode.Parse("""
            {
              "actionId": "plugin.removed.v1",
              "actionType": "plugin.removed",
              "uid": "11111111-1111-1111-1111-111111111111",
              "name": "Unavailable action",
              "isActive": true,
              "customSetting": { "mode": "preserve", "value": 42 }
            }
            """)!.AsObject());
        project.Methods[0].MethodLines.Add(MethodLine.Create(999, 0, action));

        var roundTripped = service.Deserialize(service.Serialize(project));
        var unknown = roundTripped.Methods[0].MethodLines.Single(line => line.Action?.ActionType == "plugin.removed").Action!;

        Assert.Equal("preserve", unknown.GetProperty("customSetting")?["mode"]?.GetValue<string>());
        Assert.Equal(42, unknown.GetProperty("customSetting")?["value"]?.GetValue<int>());
    }

    [Fact]
    public void ImportProject_DoesNotCreateMissingThreadTaskVariables()
    {
        var service = CreateService();
        var project = service.Deserialize("""
            {
              "name": "Task variable repair",
              "methods": [
                {
                  "name": "Main",
                  "inputs": [],
                  "outputs": [],
                  "methodLines": [
                    { "lineNo": 10, "action": { "actionId": "threadStart", "actionType": "threadStart", "methodName": "Worker", "taskVarName": "_$0workerTask" } },
                    { "lineNo": 20, "action": { "actionId": "threadWait", "actionType": "threadWait", "taskVarName": "_$0workerTask" } }
                  ],
                  "methodVariables": []
                }
              ]
            }
            """);

        Assert.Empty(project.Methods.Single().MethodVariables);
        Assert.Empty(ThreadTaskVariables.GetDeclaredNames(project.Methods.Single()));
    }

    [Fact]
    public void EditorImport_RejectsTheRemovedVariableModel()
    {
        var service = CreateService();
        var legacyFields = """
            {
              "name": "Legacy fields",
              "methods": [
                {
                  "name": "Main",
                  "methodLines": [],
                  "methodVariables": [
                    { "variableName": "_$input", "scopeKind": 1, "isInput": true }
                  ]
                }
              ]
            }
            """;
        var plainName = """
            {
              "name": "Plain variable",
              "methods": [
                {
                  "name": "Main",
                  "methodLines": [],
                  "methodVariables": [
                    { "variableName": "input" }
                  ]
                }
              ]
            }
            """;

        Assert.Throws<JsonException>(() => service.Deserialize(legacyFields));
        Assert.Throws<JsonException>(() => service.Deserialize(plainName));
    }

    [Fact]
    public void CSharpScript_RoundTripsThroughEditorAndRuntimeProject()
    {
        var service = CreateService();
        var project = EditorTestProjectFactory.Create();
        project.Scripts.Add(new WorkflowScript
        {
            Name = "PrepareSample",
            Language = "CSharp",
            Content = string.Empty
        });

        var json = service.Serialize(project);
        var restoredEditorProject = service.Deserialize(json);
        var restoredScript = Assert.Single(restoredEditorProject.Scripts);
        Assert.Equal("PrepareSample", restoredScript.Name);
        Assert.Equal("CSharp", restoredScript.Language);
        Assert.Equal(string.Empty, restoredScript.Content);

        var runtimeProject = new WorkflowJsonSerializer().DeserializeProject(json);
        var runtimeScript = Assert.Single(runtimeProject.Scripts);
        Assert.Equal(restoredScript.Uid, runtimeScript.Uid);
        Assert.Equal("PrepareSample", runtimeScript.Name);
        Assert.Equal("CSharp", runtimeScript.Language);
    }

    [Fact]
    public void ScriptLibraries_RoundTripAsLogicalIdentityWithoutMachinePaths()
    {
        var service = CreateService();
        var project = new WorkflowProject
        {
            Name = "Logical Script Library references",
            ScriptLibraries =
            [
                new SharpScriptLibraryReferenceDto
                {
                    LibraryId = "number-algorithms",
                    Version = "1.0.0"
                }
            ]
        };

        var json = service.Serialize(project);
        var restored = service.Deserialize(json);
        var runtimeProject = new WorkflowJsonSerializer().DeserializeProject(json);

        var editorReference = Assert.Single(restored.ScriptLibraries);
        Assert.Equal("number-algorithms", editorReference.LibraryId);
        Assert.Equal("1.0.0", editorReference.Version);
        var runtimeReference = Assert.Single(runtimeProject.ScriptLibraries);
        Assert.Equal(editorReference.LibraryId, runtimeReference.LibraryId);
        Assert.Equal(editorReference.Version, runtimeReference.Version);
        Assert.DoesNotContain("LocalAppData", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("filePath", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("downloadUri", json, StringComparison.OrdinalIgnoreCase);
    }

    private static JsonEditorDocumentPersistence CreateService()
        => new(new WorkflowEditorJsonSerializer());

}
