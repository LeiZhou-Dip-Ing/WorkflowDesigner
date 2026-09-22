using WorkflowCore.Actions.Communication;
using WorkflowCore.Serialization;
using WorkflowCore.WpfDemo.Editor;
using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class EmailDemoTests
{
    [Fact]
    public void DemoProject_ContainsConfiguredEmailAction()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "SampleProjects", "EmailDemo.json");
        var project = new WorkflowJsonSerializer().DeserializeProject(File.ReadAllText(file));
        var method = Assert.Single(project.Methods);
        Assert.Equal("SendDemoEmail", method.Name);
        var action = Assert.IsType<SendEmailAction>(Assert.Single(method.MethodLines).Action);
        Assert.Equal("dip_ing_leizhou@outlook.com", action.Recipients);

        var editorProject = new WorkflowEditorJsonSerializer().Deserialize(File.ReadAllText(file));
        Assert.Equal("Email Demo", editorProject.Name);
        Assert.Equal("sendEmail", Assert.Single(Assert.Single(editorProject.Methods).MethodLines).Action?.ActionType);
    }
}
