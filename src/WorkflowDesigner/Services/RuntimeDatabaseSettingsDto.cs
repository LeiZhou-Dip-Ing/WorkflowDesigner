namespace WorkflowCore.WpfDemo.Services;

public sealed record RuntimeDatabaseSettingsDto(string Provider, bool IsConfigured, string Server, string Database);
public sealed record RuntimeDatabaseSettingsUpdateDto(string Provider, string? ConnectionString);
public sealed record RuntimeDatabaseConnectionTestDto(bool Succeeded, string Message);
