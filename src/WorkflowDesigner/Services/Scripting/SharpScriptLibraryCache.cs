using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.Services.Scripting;

/// <summary>Keeps Runtime-authoritative compile assets in a hash-addressed local editor cache.</summary>
public sealed class SharpScriptLibraryCache : ISharpScriptLibraryCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IRuntimeApiClient _runtimeApi;
    private readonly string _rootDirectory;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private SharpScriptLibraryCatalogResponse? _catalog;

    public SharpScriptLibraryCache(IRuntimeApiClient runtimeApi)
        : this(runtimeApi, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Gbo",
            "WorkflowCore",
            "ScriptLibraries"))
    {
    }

    internal SharpScriptLibraryCache(IRuntimeApiClient runtimeApi, string rootDirectory)
    {
        _runtimeApi = runtimeApi ?? throw new ArgumentNullException(nameof(runtimeApi));
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(_rootDirectory);
    }

    public async Task<SharpScriptLibraryCatalogResponse> RefreshCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        var catalog = await _runtimeApi.GetScriptLibrariesAsync(cancellationToken).ConfigureAwait(false);
        _catalog = catalog;
        await _syncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteAtomicallyAsync(
                Path.Combine(_rootDirectory, "catalog.json"),
                JsonSerializer.Serialize(catalog, JsonOptions),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _syncLock.Release();
        }

        return catalog;
    }

    public async Task<IReadOnlyList<string>> ResolveCompilationReferencesAsync(
        IReadOnlyList<SharpScriptLibraryReferenceDto> references,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(references);
        if (references.Count == 0) return Array.Empty<string>();

        var catalog = _catalog;
        if (catalog == null)
        {
            try
            {
                catalog = await RefreshCatalogAsync(cancellationToken).ConfigureAwait(false);
            }
            catch when (!cancellationToken.IsCancellationRequested)
            {
                catalog = await ReadCachedCatalogAsync(cancellationToken).ConfigureAwait(false);
                _catalog = catalog;
            }
        }

        var paths = new List<string>();
        foreach (var reference in references.DistinctBy(
                     item => $"{item.LibraryId}|{item.Version}",
                     StringComparer.OrdinalIgnoreCase))
        {
            var library = catalog.Libraries.SingleOrDefault(item =>
                string.Equals(item.LibraryId, reference.LibraryId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Version, reference.Version, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"Script Library '{reference.LibraryId}' {reference.Version} is not installed in the Runtime Catalog.");
            if (!string.Equals(library.Availability, "Available", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Script Library '{library.LibraryId}' {library.Version} is unavailable: {library.AvailabilityMessage}");
            }

            foreach (var assembly in library.CompilationAssemblies)
            {
                paths.Add(await EnsureAssetAsync(library, assembly, cancellationToken).ConfigureAwait(false));
            }
        }

        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public bool IsLocallyAvailable(SharpScriptLibraryDescriptorDto library)
    {
        ArgumentNullException.ThrowIfNull(library);
        return library.CompilationAssemblies.Count > 0
               && library.CompilationAssemblies.All(assembly =>
               {
                   var path = GetAssetPath(library, assembly);
                   return File.Exists(path)
                          && string.Equals(ComputeHash(path), assembly.Sha256, StringComparison.OrdinalIgnoreCase);
               });
    }

    private async Task<string> EnsureAssetAsync(
        SharpScriptLibraryDescriptorDto library,
        SharpScriptLibraryAssemblyDto assembly,
        CancellationToken cancellationToken)
    {
        var path = GetAssetPath(library, assembly);
        if (File.Exists(path)
            && string.Equals(ComputeHash(path), assembly.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        var bytes = await _runtimeApi.GetScriptLibraryAssetAsync(assembly, cancellationToken).ConfigureAwait(false);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(hash, assembly.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Downloaded Script Library assembly '{assembly.Name}' failed SHA-256 verification.");
        }

        await _syncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken).ConfigureAwait(false);
                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
        finally
        {
            _syncLock.Release();
        }

        return path;
    }

    private string GetAssetPath(
        SharpScriptLibraryDescriptorDto library,
        SharpScriptLibraryAssemblyDto assembly)
    {
        var id = ValidateSegment(library.LibraryId);
        var version = ValidateSegment(library.Version);
        var hash = ValidateHash(assembly.Sha256);
        var fileName = Path.GetFileName(assembly.FileName);
        if (!string.Equals(fileName, assembly.FileName, StringComparison.Ordinal)
            || !fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Invalid Script Library compile asset name '{assembly.FileName}'.");
        }

        var path = Path.GetFullPath(Path.Combine(_rootDirectory, id, version, hash, fileName));
        var prefix = _rootDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Script Library cache path escapes the configured cache root.");
        }

        return path;
    }

    private async Task<SharpScriptLibraryCatalogResponse> ReadCachedCatalogAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(_rootDirectory, "catalog.json");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                "Workflow Runtime is offline and no Script Library Catalog has been cached locally.");
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<SharpScriptLibraryCatalogResponse>(json, JsonOptions)
               ?? throw new InvalidDataException("The cached Script Library Catalog is empty.");
    }

    private static string ValidateSegment(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Any(character => !char.IsLetterOrDigit(character) && character is not ('.' or '-' or '_')))
        {
            throw new InvalidDataException($"Invalid Script Library cache identity '{value}'.");
        }

        return value;
    }

    private static string ValidateHash(string value)
    {
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException("Script Library assembly has an invalid SHA-256 value.");
        }

        return value.ToLowerInvariant();
    }

    private static string ComputeHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static async Task WriteAtomicallyAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
