using System.Text.Json.Nodes;
using WorkflowCore.WpfDemo.Models;
using WorkflowCore.WpfDemo.Services;
using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class WorkflowJsonComparerTests
{
    [Fact]
    public void Compare_ReportsModifiedAndRuntimeOnlyWorkflowValuesByDocumentIdentity()
    {
        var local = JsonNode.Parse("""
            { "methods": [
              { "uid": "1", "name": "Main", "methodLines": [ { "uid": "10", "waitTime": 2 } ] }
            ] }
            """);
        var runtime = JsonNode.Parse("""
            { "methods": [
              { "uid": "1", "name": "Main", "methodLines": [ { "uid": "10", "waitTime": 1 } ] },
              { "uid": "2", "name": "RuntimeOnly", "methodLines": [] }
            ] }
            """);

        var differences = WorkflowJsonComparer.Compare(local, runtime);

        Assert.Contains(differences, item =>
            item.Kind == WorkflowDifferenceKind.Modified
            && item.Path.Contains("Main", StringComparison.Ordinal)
            && item.Path.EndsWith("waitTime", StringComparison.Ordinal)
            && item.LocalValue == "2"
            && item.RuntimeValue == "1");
        Assert.Contains(differences, item =>
            item.Kind == WorkflowDifferenceKind.RuntimeOnly
            && item.Path.Contains("RuntimeOnly", StringComparison.Ordinal));
    }

    [Fact]
    public void AreEquivalent_IgnoresJsonFormatting()
    {
        Assert.True(WorkflowJsonComparer.AreEquivalent(
            JsonNode.Parse("{\"value\":1}"),
            JsonNode.Parse("""
                {
                  "value": 1
                }
                """)));
    }

    [Fact]
    public void AreEquivalent_IgnoresObjectPropertyOrder()
    {
        Assert.True(WorkflowJsonComparer.AreEquivalent(
            JsonNode.Parse("{\"name\":\"Main\",\"version\":1}"),
            JsonNode.Parse("{\"version\":1,\"name\":\"Main\"}")));
    }
}
