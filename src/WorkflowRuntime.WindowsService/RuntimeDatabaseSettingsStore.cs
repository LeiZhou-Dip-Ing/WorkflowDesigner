using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using WorkflowCore.Execution;
using WorkflowRuntime.Application.Runtime;

namespace WorkflowRuntime.WindowsService;

public sealed record RuntimeDatabaseSettingsView(string Provider, bool IsConfigured, string Server, string Database);
public sealed record RuntimeDatabaseSettingsUpdate(string Provider, string? ConnectionString);
public sealed record RuntimeDatabaseConnectionTest(bool Succeeded, string Message);

public sealed class RuntimeDatabaseSettingsStore : IWorkflowSqlExecutor
{
    private readonly string _filePath;
    private readonly object _gate = new();
    private string _connectionString;

    public RuntimeDatabaseSettingsStore(string storageDirectory, string? initialConnectionString)
    {
        _filePath = Path.Combine(storageDirectory, "database-settings.json");
        _connectionString = File.Exists(_filePath)
            ? Unprotect(JsonSerializer.Deserialize<DatabaseSettingsFile>(File.ReadAllText(_filePath))?.EncryptedConnectionString
                ?? throw new InvalidDataException("Runtime database settings are empty."))
            : initialConnectionString ?? string.Empty;
    }

    public string ConnectionString
    {
        get { lock (_gate) return _connectionString; }
    }

    public RuntimeDatabaseSettingsView GetSettings()
    {
        var connectionString = ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString)) return new("sqlServer", false, string.Empty, string.Empty);
        var builder = new SqlConnectionStringBuilder(connectionString);
        return new("sqlServer", true, builder.DataSource, builder.InitialCatalog);
    }

    public RuntimeDatabaseSettingsView Save(RuntimeDatabaseSettingsUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (update.Provider != "sqlServer") throw new ArgumentException("Only the SQL Server provider is currently installed.");
        var next = update.ConnectionString is null ? ConnectionString : update.ConnectionString.Trim();
        if (!string.IsNullOrEmpty(next)) _ = new SqlConnectionStringBuilder(next);
        var file = new DatabaseSettingsFile { EncryptedConnectionString = Protect(next) };
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var temporaryPath = _filePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(file));
            File.Move(temporaryPath, _filePath, true);
            _connectionString = next;
        }
        return GetSettings();
    }

    public async Task<RuntimeDatabaseConnectionTest> TestConnectionAsync(CancellationToken cancellationToken)
    {
        var connectionString = ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString)) return new(false, "Configure and save a database connection first.");
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return new(true, $"Connected to {connection.DataSource} / {connection.Database}.");
        }
        catch (SqlException error) { return new(false, error.Message); }
        catch (InvalidOperationException error) { return new(false, error.Message); }
    }

    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
        string sql, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
        => CreateExecutor().QueryAsync(sql, parameters, cancellationToken);

    public Task<int> ExecuteAsync(string sql, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
        => CreateExecutor().ExecuteAsync(sql, parameters, cancellationToken);

    private IWorkflowSqlExecutor CreateExecutor()
    {
        var connectionString = ConnectionString;
        return string.IsNullOrWhiteSpace(connectionString)
            ? UnavailableWorkflowSqlExecutor.Instance
            : new SqlServerWorkflowSqlExecutor(connectionString);
    }

    private static string Protect(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Database secret storage requires Windows.");
        return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.LocalMachine));
    }

    private static string Unprotect(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Database secret storage requires Windows.");
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.LocalMachine));
    }

    private sealed class DatabaseSettingsFile
    {
        public string EncryptedConnectionString { get; set; } = string.Empty;
    }
}
