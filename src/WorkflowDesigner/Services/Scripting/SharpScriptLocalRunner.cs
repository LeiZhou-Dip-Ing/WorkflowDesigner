using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WorkflowRuntime.ScriptCompiler;
using WorkflowRuntime.ScriptSdk;

namespace WorkflowCore.WpfDemo.Services.Scripting;

/// <summary>Collectible, content-hash cached local execution used only by the editor Test Script panel.</summary>
public sealed class SharpScriptLocalRunner : ISharpScriptLocalRunner
{
    private readonly ISharpScriptCompiler _compiler;
    private readonly Dictionary<Guid, LocalSharpScriptRevision> _revisions = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _runLocks = new();
    private readonly object _syncRoot = new();
    private bool _disposed;

    public SharpScriptLocalRunner(ISharpScriptCompiler compiler)
    {
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
    }

    public async Task<SharpScriptLocalRunResult> RunAsync(
        Guid scriptUid,
        string source,
        string fileName,
        IReadOnlyDictionary<string, string> inputs,
        CancellationToken cancellationToken = default)
        => await RunAsync(
            scriptUid,
            source,
            fileName,
            inputs,
            Array.Empty<string>(),
            cancellationToken).ConfigureAwait(false);

    public async Task<SharpScriptLocalRunResult> RunAsync(
        Guid scriptUid,
        string source,
        string fileName,
        IReadOnlyDictionary<string, string> inputs,
        IReadOnlyList<string> referencePaths,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var runLock = _runLocks.GetOrAdd(scriptUid, static _ => new SemaphoreSlim(1, 1));
        await runLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sourceHash = CreateRevisionHash(source, referencePaths);
            LocalSharpScriptRevision? revision;
            lock (_syncRoot)
            {
                _revisions.TryGetValue(scriptUid, out revision);
            }

            if (revision == null || !string.Equals(revision.SourceHash, sourceHash, StringComparison.OrdinalIgnoreCase))
            {
                var compilation = await Task.Run(
                    () => _compiler.Compile(new SharpScriptCompilationRequest
                    {
                        Source = source,
                        FileName = fileName,
                        AssemblyName = $"LocalWorkflowSharpScript_{scriptUid:N}_{sourceHash[..12]}",
                        ReferencePaths = referencePaths
                    }, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                if (!compilation.Succeeded || compilation.Contract == null)
                {
                    return new SharpScriptLocalRunResult
                    {
                        Contract = compilation.Contract,
                        Diagnostics = compilation.Diagnostics
                    };
                }

                var loaded = LocalSharpScriptRevision.Load(compilation, referencePaths);
                LocalSharpScriptRevision? previous;
                lock (_syncRoot)
                {
                    _revisions.TryGetValue(scriptUid, out previous);
                    _revisions[scriptUid] = loaded;
                }

                previous?.Dispose();
                revision = loaded;
            }

            return await revision.ExecuteAsync(inputs, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            runLock.Release();
        }
    }

    public void Retire(Guid scriptUid)
    {
        LocalSharpScriptRevision? revision;
        lock (_syncRoot)
        {
            _revisions.Remove(scriptUid, out revision);
        }

        revision?.Dispose();
    }

    public void Dispose()
    {
        LocalSharpScriptRevision[] revisions;
        lock (_syncRoot)
        {
            if (_disposed) return;
            _disposed = true;
            revisions = _revisions.Values.ToArray();
            _revisions.Clear();
        }

        foreach (var revision in revisions) revision.Dispose();
        foreach (var runLock in _runLocks.Values) runLock.Dispose();
        _runLocks.Clear();
    }

    private static string CreateRevisionHash(string source, IReadOnlyList<string> referencePaths)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(source));
        foreach (var path in referencePaths.Order(StringComparer.OrdinalIgnoreCase))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetFullPath(path)));
            using var stream = File.OpenRead(path);
            var buffer = new byte[81920];
            while (true)
            {
                var count = stream.Read(buffer, 0, buffer.Length);
                if (count == 0) break;
                hash.AppendData(buffer, 0, count);
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private sealed class LocalSharpScriptRevision : IDisposable
    {
        private readonly LocalSharpScriptLoadContext _loadContext;
        private readonly Func<IWorkflowSharpScript> _factory;
        private readonly IReadOnlyDictionary<string, LocalInputBinding> _inputs;
        private readonly IReadOnlyDictionary<string, Func<IWorkflowSharpScript, object?>> _outputs;

        private LocalSharpScriptRevision(
            SharpScriptCompilation compilation,
            LocalSharpScriptLoadContext loadContext,
            Func<IWorkflowSharpScript> factory,
            IReadOnlyDictionary<string, LocalInputBinding> inputs,
            IReadOnlyDictionary<string, Func<IWorkflowSharpScript, object?>> outputs)
        {
            SourceHash = compilation.SourceHash;
            Contract = compilation.Contract!;
            _loadContext = loadContext;
            _factory = factory;
            _inputs = inputs;
            _outputs = outputs;
        }

        public string SourceHash { get; }

        public SharpScriptContract Contract { get; }

        public static LocalSharpScriptRevision Load(
            SharpScriptCompilation compilation,
            IReadOnlyList<string> referencePaths)
        {
            var loadContext = new LocalSharpScriptLoadContext(referencePaths);
            try
            {
                using var assemblyStream = new MemoryStream(compilation.AssemblyBytes, writable: false);
                using var pdbStream = new MemoryStream(compilation.PdbBytes, writable: false);
                var assembly = loadContext.LoadFromStream(assemblyStream, pdbStream);
                var entryType = assembly.GetType(compilation.Contract!.EntryTypeName, throwOnError: true)!;
                var constructor = entryType.GetConstructor(Type.EmptyTypes)
                    ?? throw new InvalidOperationException("Local script entry type has no public parameterless constructor.");
                var factory = Expression.Lambda<Func<IWorkflowSharpScript>>(
                    Expression.Convert(Expression.New(constructor), typeof(IWorkflowSharpScript)))
                    .Compile();
                var properties = entryType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .ToDictionary(property => property.Name, StringComparer.OrdinalIgnoreCase);
                var inputs = compilation.Contract.Inputs.ToDictionary(
                    field => field.Name,
                    field => CreateInputBinding(entryType, properties[field.Name]),
                    StringComparer.OrdinalIgnoreCase);
                var outputs = compilation.Contract.Outputs.ToDictionary(
                    field => field.Name,
                    field => CompileGetter(entryType, properties[field.Name]),
                    StringComparer.OrdinalIgnoreCase);
                return new LocalSharpScriptRevision(compilation, loadContext, factory, inputs, outputs);
            }
            catch
            {
                loadContext.Unload();
                throw;
            }
        }

        public async Task<SharpScriptLocalRunResult> ExecuteAsync(
            IReadOnlyDictionary<string, string> inputs,
            CancellationToken cancellationToken)
        {
            var messages = new List<string>();
            try
            {
                var instance = _factory();
                foreach (var input in inputs)
                {
                    if (_inputs.TryGetValue(input.Key, out var binding))
                    {
                        binding.Setter(instance, ConvertInput(input.Value, binding.ValueType));
                    }
                }

                var context = new LocalSharpScriptContext(messages);
                await instance.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
                return new SharpScriptLocalRunResult
                {
                    Succeeded = true,
                    Contract = Contract,
                    Outputs = _outputs.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value(instance),
                        StringComparer.OrdinalIgnoreCase),
                    Messages = messages
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new SharpScriptLocalRunResult
                {
                    Contract = Contract,
                    Messages = messages,
                    Diagnostics =
                    [
                        new SharpScriptDiagnostic
                        {
                            Severity = SharpScriptDiagnosticSeverity.Error,
                            Code = "WFSRUN",
                            Message = exception.ToString(),
                            FileName = "CurrentScript.cs"
                        }
                    ]
                };
            }
        }

        public void Dispose() => _loadContext.Unload();

        private static LocalInputBinding CreateInputBinding(Type entryType, PropertyInfo property)
        {
            var script = Expression.Parameter(typeof(IWorkflowSharpScript), "script");
            var value = Expression.Parameter(typeof(object), "value");
            var assign = Expression.Assign(
                Expression.Property(Expression.Convert(script, entryType), property),
                Expression.Convert(value, property.PropertyType));
            return new LocalInputBinding(
                property.PropertyType,
                Expression.Lambda<Action<IWorkflowSharpScript, object?>>(assign, script, value).Compile());
        }

        private static Func<IWorkflowSharpScript, object?> CompileGetter(Type entryType, PropertyInfo property)
        {
            var script = Expression.Parameter(typeof(IWorkflowSharpScript), "script");
            var read = Expression.Convert(
                Expression.Property(Expression.Convert(script, entryType), property),
                typeof(object));
            return Expression.Lambda<Func<IWorkflowSharpScript, object?>>(read, script).Compile();
        }

        private static object? ConvertInput(string text, Type targetType)
        {
            var underlyingType = Nullable.GetUnderlyingType(targetType);
            if (underlyingType != null && string.IsNullOrWhiteSpace(text)) return null;
            targetType = underlyingType ?? targetType;
            if (targetType == typeof(string)) return text;
            if (targetType == typeof(char)) return text.Single();
            if (targetType == typeof(bool)) return bool.Parse(text);
            if (targetType == typeof(Guid)) return Guid.Parse(text);
            if (targetType == typeof(DateTime)) return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if (targetType == typeof(DateTimeOffset)) return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if (targetType.IsPrimitive || targetType == typeof(decimal))
            {
                return Convert.ChangeType(text, targetType, CultureInfo.InvariantCulture);
            }

            return JsonSerializer.Deserialize(text, targetType);
        }

        private sealed record LocalInputBinding(
            Type ValueType,
            Action<IWorkflowSharpScript, object?> Setter);
    }

    private sealed class LocalSharpScriptLoadContext : AssemblyLoadContext
    {
        private readonly IReadOnlyDictionary<string, string> _assemblyPaths;

        public LocalSharpScriptLoadContext(IReadOnlyList<string> referencePaths)
            : base($"local-sharp-script:{Guid.NewGuid():N}", isCollectible: true)
        {
            _assemblyPaths = referencePaths.ToDictionary(
                path => AssemblyName.GetAssemblyName(path).Name
                        ?? throw new InvalidDataException($"Script Library assembly '{path}' has no identity."),
                Path.GetFullPath,
                StringComparer.OrdinalIgnoreCase);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (string.Equals(
                assemblyName.Name,
                typeof(IWorkflowSharpScript).Assembly.GetName().Name,
                StringComparison.OrdinalIgnoreCase))
            {
                return typeof(IWorkflowSharpScript).Assembly;
            }

            if (assemblyName.Name == null || !_assemblyPaths.TryGetValue(assemblyName.Name, out var path))
            {
                return null;
            }

            using var stream = File.OpenRead(path);
            return LoadFromStream(stream);
        }
    }

    private sealed class LocalSharpScriptContext : IWorkflowSharpScriptContext
    {
        private readonly ICollection<string> _messages;

        public LocalSharpScriptContext(ICollection<string> messages)
        {
            _messages = messages;
        }

        public Guid RunId { get; } = Guid.NewGuid();

        public string? WorkflowId => null;

        public string? MethodName => "Local Test";

        public int? LineNumber => null;

        public DateTimeOffset UtcNow { get; } = DateTimeOffset.UtcNow;

        public void Log(string message) => _messages.Add(message);
    }
}
