using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.AspNetCore.Http.Features;
using System.Security.Cryptography;
using WorkflowCore.Actions;
using WorkflowCore.Design;
using WorkflowCore.Serialization;
using WorkflowRuntime.Application.Catalog;
using WorkflowRuntime.Application.Documents;
using WorkflowRuntime.Application.Plugins;
using WorkflowRuntime.Application.Runtime;
using WorkflowRuntime.Application.Resources;
using WorkflowRuntime.Application.Security;
using WorkflowRuntime.Application.Storage;
using WorkflowRuntime.Application.SharpScripts;
using WorkflowRuntime.Application.SharpScripts.Libraries;
using WorkflowRuntime.ScriptCompiler;
using WorkflowRuntime.RestService.Extensions;
using WorkflowRuntime.ResourceSdk;

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

        var actionRegistry = new ActionRegistry();
        var metadataRegistry = new ActionMetadataRegistry();
        var assetRegistry = new ActionAssetRegistry();
        new BuiltInActionModule().RegisterActions(actionRegistry);
        new BuiltInActionMetadataModule().RegisterMetadata(metadataRegistry, assetRegistry);

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
        builder.Services.AddSingleton<WorkflowResourceRuntimeRegistry>();
        builder.Services.AddSingleton<IWorkflowResourceRuntime>(provider =>
            provider.GetRequiredService<WorkflowResourceRuntimeRegistry>());
        builder.Services.AddSingleton<IWorkflowResourcePreviewProvider>(provider =>
            provider.GetRequiredService<WorkflowResourceRuntimeRegistry>());
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
        builder.Services.AddSingleton(_ => CreateDocumentProtector(
            options,
            builder.Environment.IsDevelopment()));
        builder.Services.AddSingleton(provider => new PublishedWorkflowStore(
            options.StorageDirectory,
            provider.GetRequiredService<WorkflowDocumentProtector>()));
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
                "/api/workflow-runtime/resources/previews/{runId:guid}/{lineNumber:int}",
                (Guid runId, int lineNumber, string methodName, IWorkflowResourcePreviewProvider previews) =>
                {
                    if (!previews.TryGetLatestPreview(runId, methodName, lineNumber, out var frame)
                        || frame?.Content is not { Length: > 0 } content)
                    {
                        return Results.NotFound();
                    }

                    return Results.File(content, NormalizeContentType(frame.ContentType));
                })
            .WithName("GetWorkflowResourcePreview");

        app.MapGet(
                "/api/workflow-runtime/resources/previews/latest/{lineNumber:int}",
                (int lineNumber, string methodName, IWorkflowResourcePreviewProvider previews) =>
                {
                    if (!previews.TryGetLatestPreview(methodName, lineNumber, out var frame)
                        || frame?.Content is not { Length: > 0 } content)
                    {
                        return Results.NotFound();
                    }

                    return Results.File(content, NormalizeContentType(frame.ContentType));
                })
            .WithName("GetLatestWorkflowResourcePreview");

        app.Run();
    }

    private static string ResolvePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    }

    private static string NormalizeContentType(string? contentType)
        => string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim();

    private static WorkflowDocumentProtector CreateDocumentProtector(
        WorkflowRuntimeOptions options,
        bool isDevelopment)
    {
        if (!string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(options.WorkflowEncryptionKeyEnvironmentVariable)))
        {
            return WorkflowDocumentProtector.FromEnvironment(
                options.WorkflowEncryptionKeyId,
                options.WorkflowEncryptionKeyEnvironmentVariable);
        }

        if (!isDevelopment)
        {
            return WorkflowDocumentProtector.FromEnvironment(
                options.WorkflowEncryptionKeyId,
                options.WorkflowEncryptionKeyEnvironmentVariable);
        }

        Directory.CreateDirectory(options.StorageDirectory);
        var keyPath = Path.Combine(options.StorageDirectory, ".development-encryption-key");
        byte[] key;
        if (File.Exists(keyPath))
        {
            key = File.ReadAllBytes(keyPath);
        }
        else
        {
            key = RandomNumberGenerator.GetBytes(32);
            try
            {
                using var stream = new FileStream(keyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                stream.Write(key);
            }
            catch (IOException) when (File.Exists(keyPath))
            {
                CryptographicOperations.ZeroMemory(key);
                key = File.ReadAllBytes(keyPath);
            }
        }

        try
        {
            return WorkflowDocumentProtector.Create(options.WorkflowEncryptionKeyId, key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }
}
