using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.AspNetCore.Http.Features;
using WorkflowCore.Actions;
using WorkflowCore.Design;
using WorkflowCore.Serialization;
using WorkflowRuntime.Application.Catalog;
using WorkflowRuntime.Application.Documents;
using WorkflowRuntime.Application.Plugins;
using WorkflowRuntime.Application.Runtime;
using WorkflowRuntime.Application.Security;
using WorkflowRuntime.Application.Storage;
using WorkflowRuntime.Application.SharpScripts;
using WorkflowRuntime.Application.SharpScripts.Libraries;
using WorkflowRuntime.ScriptCompiler;
using WorkflowRuntime.RestService.Extensions;
using WorkflowRuntime.Vision.OpenCvSharp;
using WorkflowRuntime.Vision.OpenCvSharp.Runtime;
using WorkflowRuntime.VisionSdk;

namespace WorkflowRuntime.WindowsService;

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var options = builder.Configuration.GetSection("WorkflowRuntime").Get<WorkflowRuntimeOptions>()
            ?? new WorkflowRuntimeOptions();
        options.StorageDirectory = ResolvePath(options.StorageDirectory);
        options.PluginDirectory = ResolvePath(options.PluginDirectory);
        options.SharpScriptDirectory = ResolvePath(options.SharpScriptDirectory);
        options.SharpScriptLibraryDirectory = ResolvePath(options.SharpScriptLibraryDirectory);
        options.VisionPreviewDirectory = ResolvePath(options.VisionPreviewDirectory);

        builder.Host.UseWindowsService(serviceOptions =>
        {
            serviceOptions.ServiceName = "WorkflowRuntime";
        });
        builder.WebHost.UseUrls(options.Url);

        _ = OpenCvBuiltInActionModule.OpenCvSharpAssembly;

        var actionRegistry = new ActionRegistry();
        var metadataRegistry = new ActionMetadataRegistry();
        var assetRegistry = new ActionAssetRegistry();
        new BuiltInActionModule().RegisterActions(actionRegistry);
        new BuiltInActionMetadataModule().RegisterMetadata(metadataRegistry, assetRegistry);
        new OpenCvBuiltInActionModule().RegisterActions(actionRegistry);
        new OpenCvBuiltInActionMetadataModule().RegisterMetadata(metadataRegistry, assetRegistry);

        builder.Services.AddSingleton(options);
        builder.Services.Configure<FormOptions>(formOptions =>
            formOptions.MultipartBodyLengthLimit = options.MaximumScriptLibraryBytes);
        var runRetention = new RunRetentionOptions
        {
            CompletedRunRetention = TimeSpan.FromMinutes(Math.Max(0, options.CompletedRunRetentionMinutes)),
            MaxRetainedRuns = Math.Max(0, options.MaxRetainedRuns),
            CleanupInterval = TimeSpan.FromSeconds(Math.Max(1, options.RunCleanupIntervalSeconds))
        };
        builder.Services.AddSingleton(runRetention);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IRunRegistry, RunRegistry>();
        builder.Services.AddSingleton<IRuntimeEventQueue>(
            _ => new RuntimeEventQueue(Math.Max(1, options.RuntimeEventQueueCapacity)));
        builder.Services.AddSingleton(actionRegistry);
        builder.Services.AddSingleton(metadataRegistry);
        builder.Services.AddSingleton(assetRegistry);
        builder.Services.AddSingleton(new OpenCvVisionRuntimeOptions
        {
            PreviewDirectory = options.VisionPreviewDirectory,
            PreviewMaxWidth = options.VisionPreviewMaxWidth,
            PreviewMaxHeight = options.VisionPreviewMaxHeight,
            ResourceRetentionMinutes = options.VisionResourceRetentionMinutes,
            MaximumRetainedImages = options.VisionMaximumRetainedImages
        });
        builder.Services.AddSingleton<OpenCvVisionRuntime>();
        builder.Services.AddSingleton<IWorkflowVisionRuntime>(provider => provider.GetRequiredService<OpenCvVisionRuntime>());
        builder.Services.AddSingleton(provider => new WorkflowJsonSerializer(provider.GetRequiredService<ActionRegistry>()));
        builder.Services.AddSingleton<WorkflowValidator>();
        builder.Services.AddSingleton<RuntimeActionCatalog>();
        builder.Services.AddSingleton(_ => new SharpScriptLibraryCatalog(options.SharpScriptLibraryDirectory));
        builder.Services.AddSingleton(provider => new SharpScriptManagedDllInstaller(
            provider.GetRequiredService<SharpScriptLibraryCatalog>(),
            options.MaximumScriptLibraryBytes));
        builder.Services.AddSingleton(provider => new SharpScriptNuGetInstaller(
            provider.GetRequiredService<SharpScriptLibraryCatalog>(),
            options.AllowedNuGetSources,
            options.MaximumScriptLibraryBytes));
        builder.Services.AddSingleton<ActionPluginLoader>();
        builder.Services.AddSingleton<SharpScriptReferenceProvider>();
        builder.Services.AddSingleton<ISharpScriptCompiler, SharpScriptCompiler>();
        builder.Services.AddSingleton(_ => new SharpScriptArtifactStore(options.SharpScriptDirectory));
        builder.Services.AddSingleton(provider => new SharpScriptRuntimeRegistry(
            provider.GetRequiredService<ActionRegistry>(),
            provider.GetRequiredService<ActionMetadataRegistry>(),
            provider.GetRequiredService<ActionAssetRegistry>(),
            provider.GetRequiredService<TimeProvider>(),
            options.SharpScriptExecutionTimeoutSeconds > 0
                ? TimeSpan.FromSeconds(options.SharpScriptExecutionTimeoutSeconds)
                : null,
            provider.GetRequiredService<SharpScriptLibraryCatalog>()));
        builder.Services.AddSingleton<SharpScriptPublicationService>();
        builder.Services.AddSingleton<SharpScriptLibraryUsageGuard>();
        builder.Services.AddSingleton<WorkflowPublicationCoordinator>();
        builder.Services.AddSingleton<RuntimeWorkflowValidator>();
        builder.Services.AddSingleton(_ => new PublishedWorkflowStore(
            options.StorageDirectory,
            CreateDocumentProtector(builder.Environment),
            TimeProvider.System));
        builder.Services.AddSingleton<WorkflowRunLauncher>();
        builder.Services.AddWorkflowRuntimeRestServices(options.AllowRemoteAccess);
        builder.Services.AddSingleton<ActionPluginStartup>();
        builder.Services.AddSingleton<SharpScriptStartup>();
        builder.Services.AddHostedService<PublishedWorkflowStartup>();
        builder.Services.AddHostedService<ExpiredRunCleanup>();
        builder.Services.AddHostedService<VisionResourceCleanup>();

        var app = builder.Build();
        if (options.AllowRemoteAccess)
        {
            app.Logger.LogWarning(
                "Workflow Runtime remote access is enabled. Configure HTTPS and authenticated network access before exposing this service.");
        }
        // Plugin registration must finish before hosted services deserialize stored workflows.
        app.Services.GetRequiredService<ActionPluginStartup>().Load();
        app.Services.GetRequiredService<SharpScriptStartup>().LoadAsync().GetAwaiter().GetResult();
        app.UseWorkflowRuntimeAccessPolicy();
        app.UseWorkflowRuntimeApiDocumentation();
        app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
        app.MapWorkflowRuntimeEndpoints();

        app.MapGet(
                "/api/workflow-runtime/vision/previews/{runId:guid}/{lineNumber:int}",
                (Guid runId, int lineNumber, string methodName, OpenCvVisionRuntime visionRuntime) =>
                {
                    if (!visionRuntime.TryGetLatestPreview(runId, methodName, lineNumber, out var frame)
                        || frame?.EncodedImage is not { Length: > 0 } encodedImage)
                    {
                        return Results.NotFound();
                    }

                    return Results.File(encodedImage, "image/png");
                })
            .WithName("GetWorkflowVisionPreview")
            .ExcludeFromDescription();

        app.MapGet(
                "/api/workflow-runtime/vision/previews/latest/{lineNumber:int}",
                (int lineNumber, string methodName, OpenCvVisionRuntime visionRuntime) =>
                {
                    if (!visionRuntime.TryGetLatestPreview(methodName, lineNumber, out var frame)
                        || frame?.EncodedImage is not { Length: > 0 } encodedImage)
                    {
                        return Results.NotFound();
                    }

                    return Results.File(encodedImage, "image/png");
                })
            .WithName("GetLatestWorkflowVisionPreview")
            .ExcludeFromDescription();

        app.Run();
    }

    private static string ResolvePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    }

    private static WorkflowDocumentProtector CreateDocumentProtector(IHostEnvironment environment)
    {
        const string keyId = "primary";
        const string environmentVariableName = "WORKFLOW_RUNTIME_ENCRYPTION_KEY";

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(environmentVariableName)))
        {
            return WorkflowDocumentProtector.FromEnvironment(keyId, environmentVariableName);
        }

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"Required workflow encryption key environment variable '{environmentVariableName}' is not set.");
        }

        var developmentKey = SHA256.HashData(
            Encoding.UTF8.GetBytes("WorkflowRuntime.WindowsService development workflow encryption key"));
        return WorkflowDocumentProtector.Create("development", developmentKey);
    }
}
