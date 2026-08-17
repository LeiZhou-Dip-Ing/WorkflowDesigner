using System.Text.Json;
using System.Text.Json.Nodes;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.Editor;

public sealed class WorkflowEditorJsonSerializer
{
    private const int CurrentEditorSchemaVersion = 2;
    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

    public string Serialize(WorkflowProject project)
        => SerializeToNode(project).ToJsonString(IndentedOptions);

    public string SerializeDocument(WorkflowEditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.Kind switch
        {
            WorkflowEditorDocumentKind.Method when document.Method != null => new JsonObject
            {
                ["editorSchemaVersion"] = CurrentEditorSchemaVersion,
                ["documentType"] = "method",
                ["method"] = SerializeMethod(document.Method)
            },
            WorkflowEditorDocumentKind.CSharpScript when document.Script != null => new JsonObject
            {
                ["editorSchemaVersion"] = CurrentEditorSchemaVersion,
                ["documentType"] = "csharpScript",
                ["script"] = SerializeScript(document.Script)
            },
            _ => throw new InvalidOperationException("The workflow editor document has no exportable content.")
        };

        return root.ToJsonString(IndentedOptions);
    }

    public JsonObject SerializeToNode(WorkflowProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var root = Clone(project.ExtensionData);
        root["editorSchemaVersion"] = CurrentEditorSchemaVersion;
        root["projectId"] = project.ProjectId.ToString("D");
        root["name"] = project.Name;
        root["version"] = project.Version;
        root["methods"] = new JsonArray(project.Methods.Select(SerializeMethod).ToArray<JsonNode?>());
        root["scripts"] = new JsonArray(project.Scripts.Select(SerializeScript).ToArray<JsonNode?>());
        root["scriptLibraries"] = new JsonArray(project.ScriptLibraries.Select(reference => new JsonObject
        {
            ["libraryId"] = reference.LibraryId,
            ["version"] = reference.Version
        }).ToArray<JsonNode?>());
        return root;
    }

    public WorkflowProject Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var root = JsonNode.Parse(json) as JsonObject ?? throw new JsonException("Workflow JSON root must be an object.");
        if (root.ContainsKey("documentType"))
        {
            throw new JsonException("The selected file is a single workflow document, not a workflow project.");
        }

        return Deserialize(root);
    }

    public WorkflowEditorDocument DeserializeDocument(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var root = JsonNode.Parse(json) as JsonObject
                   ?? throw new JsonException("Workflow document JSON root must be an object.");
        var documentType = GetString(root, "documentType");

        if (string.Equals(documentType, "method", StringComparison.OrdinalIgnoreCase)
            && root["method"] is JsonObject method)
        {
            return WorkflowEditorDocument.FromMethod(DeserializeMethod(method));
        }

        if (string.Equals(documentType, "csharpScript", StringComparison.OrdinalIgnoreCase)
            && root["script"] is JsonObject script)
        {
            return WorkflowEditorDocument.FromScript(DeserializeScript(script));
        }

        throw new JsonException("The selected file must contain exactly one current workflow method or C# script document.");
    }

    public WorkflowProject Deserialize(JsonObject root)
    {
        if (root.ContainsKey("documentType"))
        {
            throw new JsonException("A single workflow document cannot replace a workflow project.");
        }

        var projectId = GetGuid(root, "projectId");
        var project = new WorkflowProject
        {
            ProjectId = projectId ?? Guid.NewGuid(),
            ProjectIdWasGenerated = !projectId.HasValue,
            Name = GetString(root, "name") ?? "Workflow Project",
            Version = GetString(root, "version") ?? "1.0",
            ExtensionData = CaptureExtension(root, "editorSchemaVersion", "projectId", "name", "version", "methods", "scripts", "scriptLibraries")
        };
        if (root["methods"] is JsonArray methods)
        {
            project.Methods.AddRange(methods.OfType<JsonObject>().Select(DeserializeMethod));
        }

        if (root["scripts"] is JsonArray scripts)
        {
            project.Scripts.AddRange(scripts.OfType<JsonObject>().Select(DeserializeScript));
        }

        if (root["scriptLibraries"] is JsonArray scriptLibraries)
        {
            project.ScriptLibraries.AddRange(scriptLibraries.OfType<JsonObject>().Select(reference =>
                new SharpScriptLibraryReferenceDto
                {
                    LibraryId = GetString(reference, "libraryId") ?? string.Empty,
                    Version = GetString(reference, "version") ?? string.Empty
                }).Where(reference => !string.IsNullOrWhiteSpace(reference.LibraryId)
                                      && !string.IsNullOrWhiteSpace(reference.Version)));
        }

        return project;
    }

    private static WorkflowScript DeserializeScript(JsonObject document)
        => new()
        {
            Uid = GetGuid(document, "uid") ?? Guid.NewGuid(),
            Name = GetString(document, "name") ?? string.Empty,
            Language = GetString(document, "language") ?? "CSharp",
            Content = GetString(document, "content") ?? string.Empty,
            ExtensionData = CaptureExtension(document, "uid", "name", "language", "content")
        };

    private static WorkflowMethod DeserializeMethod(JsonObject document)
    {
        if (document["inputs"] is not JsonArray || document["outputs"] is not JsonArray)
        {
            throw new JsonException(
                $"Method '{GetString(document, "name") ?? "<unnamed>"}' does not use the current explicit input/output contract.");
        }

        var method = new WorkflowMethod
        {
            Uid = GetGuid(document, "uid") ?? Guid.NewGuid(),
            Name = GetString(document, "name") ?? string.Empty,
            MethodType = (WorkflowMethodType)(GetInt(document, "methodType") ?? 0),
            InitAtStart = GetBool(document, "initAtStart") ?? false,
            InitMethodName = GetString(document, "initMethodName"),
            LastExecution = GetDateTime(document, "lastExecution"),
            ExtensionData = CaptureExtension(document, "uid", "name", "methodType", "initAtStart", "initMethodName", "lastExecution", "methodLines", "methodVariables", "inputs", "outputs")
        };

        if (document["methodLines"] is JsonArray lines)
        {
            method.MethodLines.AddRange(lines.OfType<JsonObject>().Select(DeserializeLine));
        }

        if (document["methodVariables"] is JsonArray variables)
        {
            method.MethodVariables.AddRange(variables.OfType<JsonObject>().Select(DeserializeVariable));
        }

        if (document["inputs"] is JsonArray inputs)
        {
            method.Inputs.AddRange(inputs.OfType<JsonObject>().Select(DeserializeMethodParameter));
        }

        if (document["outputs"] is JsonArray outputs)
        {
            method.Outputs.AddRange(outputs.OfType<JsonObject>().Select(DeserializeMethodParameter));
        }

        return method;
    }

    private static MethodLine DeserializeLine(JsonObject document)
    {
        var action = document["action"] is JsonObject actionDocument
            ? new WorkflowAction(Clone(actionDocument))
            : null;
        if (action != null && string.IsNullOrWhiteSpace(action.ActionId))
        {
            throw new JsonException(
                $"Action '{action.ActionType}' does not use the current stable actionId identity.");
        }

        return new MethodLine
        {
            Uid = GetGuid(document, "uid") ?? Guid.NewGuid(),
            LineNo = GetInt(document, "lineNo") ?? 0,
            SequenceNo = GetInt(document, "sequenceNo") ?? 0,
            NestingLevel = GetInt(document, "nestingLevel") ?? 0,
            IsActive = GetBool(document, "isActive") ?? true,
            Comment = GetString(document, "comment"),
            Action = action,
            ExtensionData = CaptureExtension(document, "uid", "lineNo", "sequenceNo", "nestingLevel", "isActive", "comment", "action")
        };
    }

    private static WorkflowVariable DeserializeVariable(JsonObject document)
    {
        var legacyField = document
            .Select(property => property.Key)
            .FirstOrDefault(name =>
                name.Equals("scopeKind", StringComparison.OrdinalIgnoreCase)
                || name.Equals("isInput", StringComparison.OrdinalIgnoreCase)
                || name.Equals("isReturn", StringComparison.OrdinalIgnoreCase)
                || name.Equals("isInternal", StringComparison.OrdinalIgnoreCase));
        if (legacyField != null)
        {
            throw new JsonException(
                $"Legacy variable field '{legacyField}' is no longer supported. Variable semantics are defined by variableName.");
        }

        var variable = new WorkflowVariable
        {
            Uid = GetGuid(document, "uid") ?? Guid.NewGuid(),
            VariableName = GetString(document, "variableName") ?? string.Empty,
            Value = ConvertNode(document["value"]),
            DataType = GetString(document, "dataType") ?? "object",
            IsActive = GetBool(document, "isActive") ?? true,
            Description = GetString(document, "description"),
            RequestText = GetString(document, "requestText"),
            OrderIndex = GetInt(document, "orderIndex") ?? 0,
            DefaultValue = ConvertNode(document["defaultValue"]),
            MinCheck = GetBool(document, "minCheck") ?? false,
            MinValue = GetDouble(document, "minValue") ?? 0,
            MaxCheck = GetBool(document, "maxCheck") ?? false,
            MaxValue = GetDouble(document, "maxValue") ?? 0,
            PickList = GetString(document, "pickList"),
            DataIsArray = GetBool(document, "dataIsArray") ?? false,
            ArrayLengthRefToOrder = GetInt(document, "arrayLengthRefToOrder") ?? 0,
            ExtensionData = CaptureExtension(document, "uid", "variableName", "value", "dataType", "isActive", "description", "requestText", "orderIndex", "defaultValue", "minCheck", "minValue", "maxCheck", "maxValue", "pickList", "dataIsArray", "arrayLengthRefToOrder")
        };
        if (!WorkflowVariableNaming.IsVariable(variable.VariableName))
        {
            throw new JsonException(
                $"Variable '{variable.VariableName}' does not follow the current variable naming model.");
        }

        return variable;
    }

    private static JsonObject SerializeMethod(WorkflowMethod method)
    {
        var result = Clone(method.ExtensionData);
        result["uid"] = method.Uid.ToString();
        result["name"] = method.Name;
        result["methodType"] = (int)method.MethodType;
        result["initAtStart"] = method.InitAtStart;
        result["initMethodName"] = method.InitMethodName;
        result["lastExecution"] = method.LastExecution == null ? null : JsonValue.Create(method.LastExecution.Value);
        result["methodLines"] = new JsonArray(method.MethodLines.Select(SerializeLine).ToArray<JsonNode?>());
        result["methodVariables"] = new JsonArray(method.MethodVariables.Select(SerializeVariable).ToArray<JsonNode?>());
        result["inputs"] = new JsonArray(method.Inputs.OrderBy(parameter => parameter.Order).Select(SerializeMethodParameter).ToArray<JsonNode?>());
        result["outputs"] = new JsonArray(method.Outputs.OrderBy(parameter => parameter.Order).Select(SerializeMethodParameter).ToArray<JsonNode?>());
        return result;
    }

    private static WorkflowMethodParameter DeserializeMethodParameter(JsonObject document)
        => new()
        {
            Uid = GetGuid(document, "uid") ?? Guid.NewGuid(),
            Name = GetString(document, "name") ?? string.Empty,
            VariableName = GetString(document, "variableName") ?? string.Empty,
            DisplayName = GetString(document, "displayName") ?? GetString(document, "name") ?? string.Empty,
            Description = GetString(document, "description") ?? string.Empty,
            Order = GetInt(document, "order") ?? 0,
            ValueType = GetString(document, "valueType") ?? "object",
            Required = GetBool(document, "required") ?? false,
            DefaultValue = ConvertNode(document["defaultValue"]),
            Editor = GetString(document, "editor") ?? "text"
        };

    private static JsonObject SerializeMethodParameter(WorkflowMethodParameter parameter)
        => new()
        {
            ["uid"] = parameter.Uid.ToString(),
            ["name"] = parameter.Name,
            ["variableName"] = parameter.VariableName,
            ["displayName"] = parameter.DisplayName,
            ["description"] = parameter.Description,
            ["order"] = parameter.Order,
            ["valueType"] = parameter.ValueType,
            ["required"] = parameter.Required,
            ["defaultValue"] = SerializeValue(parameter.DefaultValue),
            ["editor"] = parameter.Editor
        };

    private static JsonObject SerializeScript(WorkflowScript script)
    {
        var result = Clone(script.ExtensionData);
        result["uid"] = script.Uid.ToString();
        result["name"] = script.Name;
        result["language"] = script.Language;
        result["content"] = script.Content;
        return result;
    }

    private static JsonObject SerializeLine(MethodLine line)
    {
        var result = Clone(line.ExtensionData);
        result["uid"] = line.Uid.ToString();
        result["lineNo"] = line.LineNo;
        result["sequenceNo"] = line.SequenceNo;
        result["nestingLevel"] = line.NestingLevel;
        result["isActive"] = line.IsActive;
        result["comment"] = line.Comment;
        result["action"] = line.Action?.ToJsonObject();
        return result;
    }

    private static JsonObject SerializeVariable(WorkflowVariable variable)
    {
        if (!WorkflowVariableNaming.IsVariable(variable.VariableName))
        {
            throw new JsonException(
                $"Variable '{variable.VariableName}' does not follow the current variable naming model.");
        }

        var result = Clone(variable.ExtensionData);
        result["uid"] = variable.Uid.ToString();
        result["variableName"] = variable.VariableName;
        result["value"] = SerializeValue(variable.Value);
        result["dataType"] = variable.DataType;
        result["isActive"] = variable.IsActive;
        result["description"] = variable.Description;
        result["requestText"] = variable.RequestText;
        result["orderIndex"] = variable.OrderIndex;
        result["defaultValue"] = SerializeValue(variable.DefaultValue);
        result["minCheck"] = variable.MinCheck;
        result["minValue"] = variable.MinValue;
        result["maxCheck"] = variable.MaxCheck;
        result["maxValue"] = variable.MaxValue;
        result["pickList"] = variable.PickList;
        result["dataIsArray"] = variable.DataIsArray;
        result["arrayLengthRefToOrder"] = variable.ArrayLengthRefToOrder;
        return result;
    }

    private static JsonNode? SerializeValue(object? value)
        => value switch
        {
            null => null,
            JsonNode node => node.DeepClone(),
            JsonElement element => JsonNode.Parse(element.GetRawText()),
            _ => JsonSerializer.SerializeToNode(value)
        };

    private static object? ConvertNode(JsonNode? node)
    {
        if (node == null) return null;
        if (node is JsonValue value)
        {
            if (value.TryGetValue<bool>(out var boolean)) return boolean;
            if (value.TryGetValue<long>(out var integer)) return integer;
            if (value.TryGetValue<double>(out var number)) return number;
            if (value.TryGetValue<string>(out var text)) return text;
        }

        return node.DeepClone();
    }

    private static JsonObject CaptureExtension(JsonObject source, params string[] knownNames)
    {
        var known = new HashSet<string>(knownNames, StringComparer.OrdinalIgnoreCase);
        var result = new JsonObject();
        foreach (var pair in source.Where(pair => !known.Contains(pair.Key)))
        {
            result[pair.Key] = pair.Value?.DeepClone();
        }

        return result;
    }

    private static JsonObject Clone(JsonObject source) => (JsonObject)source.DeepClone();
    private static string? GetString(JsonObject source, string name) => source[name]?.GetValue<string>();
    private static int? GetInt(JsonObject source, string name) => source[name]?.GetValue<int>();
    private static double? GetDouble(JsonObject source, string name) => source[name]?.GetValue<double>();
    private static bool? GetBool(JsonObject source, string name) => source[name]?.GetValue<bool>();
    private static Guid? GetGuid(JsonObject source, string name) => Guid.TryParse(GetString(source, name), out var value) ? value : null;
    private static DateTime? GetDateTime(JsonObject source, string name) => DateTime.TryParse(GetString(source, name), out var value) ? value : null;
}
