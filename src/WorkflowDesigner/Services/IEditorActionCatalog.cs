using WorkflowRuntime.Contracts;
using System.Windows.Media;

namespace WorkflowCore.WpfDemo.Services;

/// <summary>Provides the Runtime Action definitions and locally cached icons used by the editor.</summary>
public interface IEditorActionCatalog
{
    ActionCatalogResponse Current { get; }

    string? GetCachedIconUri(ActionAssetReferenceDto? icon);

    ImageSource? GetCachedIconImage(ActionAssetReferenceDto? icon) => null;

    Task<ActionCatalogResponse> RefreshAsync(CancellationToken cancellationToken = default);

    Task<bool> ApplyChangeAsync(
        ActionCatalogChangedDto change,
        CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}
