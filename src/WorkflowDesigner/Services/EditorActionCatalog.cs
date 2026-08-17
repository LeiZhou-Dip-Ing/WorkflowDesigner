using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.Services;

/// <summary>Refreshes the editor's Action Catalog from Runtime and keeps an offline cache.</summary>
public sealed class EditorActionCatalog : IEditorActionCatalog
{
    private static readonly JsonSerializerOptions CatalogJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IRuntimeApiClient _runtimeApi;
    private readonly string _cachePath;
    private readonly string _assetCacheDirectory;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly ConcurrentDictionary<string, Lazy<ImageSource?>> _imageCache =
        new(StringComparer.OrdinalIgnoreCase);

    public EditorActionCatalog(IRuntimeApiClient runtimeApi)
        : this(runtimeApi, GetDefaultCachePath())
    {
    }

    internal EditorActionCatalog(IRuntimeApiClient runtimeApi, string cachePath)
    {
        _runtimeApi = runtimeApi ?? throw new ArgumentNullException(nameof(runtimeApi));
        ArgumentException.ThrowIfNullOrWhiteSpace(cachePath);
        _cachePath = Path.GetFullPath(cachePath);
        _assetCacheDirectory = Path.Combine(Path.GetDirectoryName(_cachePath)!, "action-assets");
        Current = LoadCachedCatalog() ?? CreateEmptyCatalog();
    }

    public ActionCatalogResponse Current { get; private set; }

    public string? GetCachedIconUri(ActionAssetReferenceDto? icon)
    {
        if (icon == null)
        {
            return null;
        }

        var path = GetAssetPath(icon);
        return File.Exists(path) && new FileInfo(path).Length > 0
            ? new Uri(path).AbsoluteUri
            : null;
    }

    public ImageSource? GetCachedIconImage(ActionAssetReferenceDto? icon)
    {
        if (icon == null)
        {
            return null;
        }

        return _imageCache.GetOrAdd(
            icon.ContentHash,
            _ => new Lazy<ImageSource?>(
                () => LoadFrozenImage(icon),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    public async Task<ActionCatalogResponse> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var catalog = string.IsNullOrWhiteSpace(Current.CatalogVersion)
                ? await _runtimeApi.GetActionCatalogAsync(cancellationToken).ConfigureAwait(false)
                : await _runtimeApi.GetActionCatalogIfChangedAsync(Current.CatalogVersion, cancellationToken)
                    .ConfigureAwait(false);
            if (catalog == null)
            {
                await CacheAssetsAsync(Current, cancellationToken).ConfigureAwait(false);
                return Current;
            }

            await CacheAssetsAsync(catalog, cancellationToken).ConfigureAwait(false);
            Current = catalog;
            PruneImageCache(catalog);
            PruneAssetCache(catalog);
            await SaveCatalogAsync(catalog, cancellationToken).ConfigureAwait(false);
            return catalog;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task<bool> ApplyChangeAsync(
        ActionCatalogChangedDto change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (string.Equals(Current.CatalogVersion, change.CatalogVersion, StringComparison.Ordinal))
            {
                return true;
            }

            if (!string.Equals(Current.CatalogVersion, change.PreviousCatalogVersion, StringComparison.Ordinal))
            {
                return false;
            }

            var actions = Current.Actions.ToDictionary(GetDescriptorKey, StringComparer.OrdinalIgnoreCase);
            foreach (var actionId in change.RemovedActionIds)
            {
                foreach (var key in actions
                             .Where(pair => string.Equals(pair.Value.ActionId, actionId, StringComparison.OrdinalIgnoreCase))
                             .Select(pair => pair.Key)
                             .ToArray())
                {
                    actions.Remove(key);
                }
            }

            foreach (var descriptor in change.Added.Concat(change.Updated))
            {
                actions[GetDescriptorKey(descriptor)] = descriptor;
            }

            var patched = new ActionCatalogResponse
            {
                SchemaVersion = Current.SchemaVersion,
                CatalogVersion = change.CatalogVersion,
                Actions = actions.Values
                    .OrderBy(action => action.Category, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(action => action.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
            await CacheAssetsAsync(
                new ActionCatalogResponse
                {
                    SchemaVersion = patched.SchemaVersion,
                    CatalogVersion = patched.CatalogVersion,
                    Actions = change.Added.Concat(change.Updated).ToArray()
                },
                cancellationToken).ConfigureAwait(false);
            Current = patched;
            PruneImageCache(patched);
            PruneAssetCache(patched);
            await SaveCatalogAsync(patched, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task CacheAssetsAsync(ActionCatalogResponse catalog, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_assetCacheDirectory);
        foreach (var icon in catalog.Actions
                     .Select(action => action.Icon)
                     .Where(icon => icon != null)
                     .Cast<ActionAssetReferenceDto>()
                     .DistinctBy(icon => icon.ContentHash, StringComparer.OrdinalIgnoreCase))
        {
            var path = GetAssetPath(icon);
            if (File.Exists(path) && new FileInfo(path).Length > 0)
            {
                continue;
            }

            try
            {
                var content = await _runtimeApi.GetActionAssetAsync(icon, cancellationToken).ConfigureAwait(false);
                if (!IsValidImage(icon.ContentType, content))
                {
                    continue;
                }

                var temporaryPath = path + ".tmp";
                await File.WriteAllBytesAsync(temporaryPath, content, cancellationToken).ConfigureAwait(false);
                File.Move(temporaryPath, path, overwrite: true);
                _imageCache.TryRemove(icon.ContentHash, out _);
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException)
            {
                // A missing icon must not prevent catalog or offline workflow editing.
            }
        }
    }

    private ImageSource? LoadFrozenImage(ActionAssetReferenceDto icon)
    {
        var path = GetAssetPath(icon);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            ImageSource image;
            if (string.Equals(icon.ContentType, "image/svg+xml", StringComparison.OrdinalIgnoreCase))
            {
                var reader = new FileSvgReader(new WpfDrawingSettings(), isEmbedded: true);
                var drawing = reader.Read(path);
                if (drawing == null)
                {
                    return null;
                }

                if (drawing.CanFreeze)
                {
                    drawing.Freeze();
                }

                image = new DrawingImage(drawing);
            }
            else
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                image = bitmap;
            }

            if (image.CanFreeze)
            {
                image.Freeze();
            }

            return image;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    private void PruneImageCache(ActionCatalogResponse catalog)
    {
        var activeHashes = catalog.Actions
            .Select(action => action.Icon?.ContentHash)
            .Where(hash => !string.IsNullOrWhiteSpace(hash))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var hash in _imageCache.Keys.Where(hash => !activeHashes.Contains(hash)))
        {
            _imageCache.TryRemove(hash, out _);
        }
    }

    private void PruneAssetCache(ActionCatalogResponse catalog)
    {
        if (!Directory.Exists(_assetCacheDirectory))
        {
            return;
        }

        var activeHashes = catalog.Actions
            .Select(action => action.Icon?.ContentHash)
            .Where(hash => !string.IsNullOrWhiteSpace(hash))
            .Select(hash => new string(hash!.Where(char.IsLetterOrDigit).ToArray()))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(_assetCacheDirectory))
        {
            if (activeHashes.Contains(Path.GetFileNameWithoutExtension(path)))
            {
                continue;
            }

            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A stale cache file in use can be removed on a later reconciliation.
            }
            catch (UnauthorizedAccessException)
            {
                // Read-only cache files do not affect the active in-memory catalog.
            }
        }
    }

    private async Task SaveCatalogAsync(ActionCatalogResponse catalog, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_cachePath)!;
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            _cachePath,
            JsonSerializer.Serialize(catalog, CatalogJsonOptions),
            cancellationToken).ConfigureAwait(false);
    }

    private static string GetDescriptorKey(WorkflowActionDescriptorDto descriptor)
        => $"{descriptor.ActionId}\u001f{descriptor.ActionType}";

    private string GetAssetPath(ActionAssetReferenceDto icon)
    {
        var extension = icon.ContentType.ToLowerInvariant() switch
        {
            "image/svg+xml" => ".svg",
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            _ => ".img"
        };
        var safeHash = new string(icon.ContentHash.Where(char.IsLetterOrDigit).ToArray());
        return Path.Combine(_assetCacheDirectory, safeHash + extension);
    }

    private static bool IsValidImage(string contentType, byte[] content)
    {
        if (content.Length == 0)
        {
            return false;
        }

        return !string.Equals(contentType, "image/svg+xml", StringComparison.OrdinalIgnoreCase)
            || Encoding.UTF8.GetString(content).Contains("<svg", StringComparison.OrdinalIgnoreCase);
    }

    private ActionCatalogResponse? LoadCachedCatalog()
    {
        try
        {
            if (!File.Exists(_cachePath))
            {
                return null;
            }

            var catalog = JsonSerializer.Deserialize<ActionCatalogResponse>(
                File.ReadAllText(_cachePath),
                CatalogJsonOptions);
            return catalog;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static ActionCatalogResponse CreateEmptyCatalog()
        => new() { CatalogVersion = string.Empty, Actions = Array.Empty<WorkflowActionDescriptorDto>() };

    private static string GetDefaultCachePath()
    {
        var configuredPath = Environment.GetEnvironmentVariable("WORKFLOW_ACTION_CATALOG_CACHE_PATH");
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        var projectDirectory = FindProjectDirectory(Environment.CurrentDirectory)
            ?? FindProjectDirectory(AppContext.BaseDirectory);
        if (projectDirectory != null)
        {
            return Path.Combine(projectDirectory, "LocalData", "action-catalog.json");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WorkflowCore.WpfDemo",
            "action-catalog.json");
    }

    private static string? FindProjectDirectory(string startDirectory)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(startDirectory));
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WorkflowCore.WpfDemo.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
