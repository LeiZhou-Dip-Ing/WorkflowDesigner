using System.Text;
using WorkflowRuntime.ActionSdk;

namespace WorkflowRuntime.SampleActionPlugin;

public sealed class SampleActionPlugin : IWorkflowActionPlugin
{
    public string PluginId => "workflow.sample-actions";

    public string PluginVersion => "1.1.0";

    public void Register(IWorkflowActionPluginBuilder builder)
    {
        builder.AddAction<GreetingAction>();
        builder.AddAction<TextMetricsAction>();
        builder.AddAction<TextTransformAction>();
        builder.AddAction<JsonEnvelopeAction>();
        builder.AddAction<DelayAction>();
        builder.AddAction<RunCounterAction>();
        builder.AddAction<PingActionHandler>(new WorkflowActionDefinition
        {
            ActionId = "cf7ab95e-7bcf-477d-81a2-546822a020d0",
            ActionType = "sample.ping",
            DisplayName = "Ping",
            Category = "External plugins",
            Description = "A zero-parameter Action that implements the handler interface without inheriting the SDK Base.",
            DisplayTemplate = "Ping runtime",
            Aliases = Array.Empty<string>(),
            Fields = Array.Empty<WorkflowActionFieldDefinition>(),
            Icon = new WorkflowActionIcon
            {
                ContentType = "image/svg+xml",
                Content = Encoding.UTF8.GetBytes(
                    "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"32\" height=\"32\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"#62b915\" stroke-width=\"1.8\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><circle cx=\"12\" cy=\"12\" r=\"3\"/><path d=\"M5.5 18.5a9 9 0 0 1 0-13M18.5 5.5a9 9 0 0 1 0 13M8 16a5.5 5.5 0 0 1 0-8M16 8a5.5 5.5 0 0 1 0 8\"/></svg>")
            }
        });
    }
}
