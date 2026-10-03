using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using WorkflowCore.Execution;

namespace WorkflowRuntime.WindowsService;

public sealed record SqlScriptPreviewRequest(
    string Sql,
    Dictionary<string, JsonElement>? Parameters,
    bool ValidateOnly);

public sealed record SqlScriptPreviewResponse(
    bool Succeeded,
    string Message,
    IReadOnlyList<Dictionary<string, string?>> Rows,
    int RowsAffected);

public sealed class SqlScriptPreviewService(RuntimeDatabaseSettingsStore databaseSettings)
{
    private const int MaximumRows = 1000;

    public async Task<SqlScriptPreviewResponse> RunAsync(
        SqlScriptPreviewRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Sql)) return Failure("SQL script is empty.");
        var connectionString = databaseSettings.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
            return Failure("Runtime database is not configured. Open Settings / Database in Designer.");

        var sql = SqlScriptParameters.ToParameterizedSql(request.Sql);

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            if (request.ValidateOnly)
            {
                if (request.Sql.Contains("PARSEONLY", StringComparison.OrdinalIgnoreCase))
                    return Failure("SET PARSEONLY is not allowed in a script being validated.");
                await using (var enableParsing = new SqlCommand("SET PARSEONLY ON", connection))
                    await enableParsing.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await using var command = new SqlCommand(sql, connection) { CommandTimeout = 30 };
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    return new SqlScriptPreviewResponse(true, "SQL syntax is valid.", [], 0);
                }
                finally
                {
                    await using var disableParsing = new SqlCommand("SET PARSEONLY OFF", connection);
                    await disableParsing.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }

            await using var execute = new SqlCommand(sql, connection) { CommandTimeout = 30 };
            foreach (var parameter in request.Parameters ?? [])
            {
                var name = parameter.Key.Trim().TrimStart('@');
                if (name.Length == 0 || !name.All(character => char.IsLetterOrDigit(character) || character == '_'))
                    return Failure($"Invalid SQL parameter name '{parameter.Key}'.");
                execute.Parameters.AddWithValue("@" + name, ConvertParameter(parameter.Value));
            }

            await using var reader = await execute.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var rows = new List<Dictionary<string, string?>>();
            if (reader.FieldCount > 0)
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (rows.Count >= MaximumRows)
                        return Failure($"Query returned more than {MaximumRows} rows. Narrow it before previewing.");
                    var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                    for (var index = 0; index < reader.FieldCount; index++)
                    {
                        var value = reader.IsDBNull(index) ? null : reader.GetValue(index);
                        row[reader.GetName(index)] = value switch
                        {
                            null => null,
                            byte[] bytes => Convert.ToBase64String(bytes),
                            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
                        };
                    }
                    rows.Add(row);
                }
            }

            while (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) { }

            var affected = reader.RecordsAffected;
            var message = rows.Count > 0 ? $"{rows.Count} row(s) returned."
                : affected >= 0 ? $"{affected} row(s) affected."
                : "Statement completed.";
            return new SqlScriptPreviewResponse(true, message, rows, affected);
        }
        catch (SqlException error)
        {
            return Failure(error.Message);
        }
        catch (InvalidOperationException error)
        {
            return Failure(error.Message);
        }
    }

    private static object ConvertParameter(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number when value.TryGetDecimal(out var decimalNumber) => decimalNumber,
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => DBNull.Value,
            _ => throw new InvalidOperationException("SQL parameters must be strings, numbers, booleans, or null.")
        };

    private static SqlScriptPreviewResponse Failure(string message) => new(false, message, [], 0);
}
