using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.Services.Scripting;

public interface ISharpScriptLibraryCache
{
    Task<SharpScriptLibraryCatalogResponse> RefreshCatalogAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ResolveCompilationReferencesAsync(
        IReadOnlyList<SharpScriptLibraryReferenceDto> references,
        CancellationToken cancellationToken = default);

    bool IsLocallyAvailable(SharpScriptLibraryDescriptorDto library);
}
