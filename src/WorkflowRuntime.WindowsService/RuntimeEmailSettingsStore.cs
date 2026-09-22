using System.Net.Mail;
using System.Security.Cryptography;
using System.Text.Json;
using WorkflowCore.Communication;

namespace WorkflowRuntime.WindowsService;

public sealed class RuntimeEmailSettingsStore : IWorkflowEmailSender
{
    private readonly string _filePath;
    private readonly object _gate = new();
    private readonly MicrosoftGraphEmailSender _microsoftGraph;
    private RuntimeEmailSettingsFile _current;

    public RuntimeEmailSettingsStore(string storageDirectory, SmtpWorkflowEmailSettings initialSettings,
        MicrosoftGraphEmailSender? microsoftGraph = null)
    {
        _filePath = Path.Combine(storageDirectory, "email-settings.json");
        _microsoftGraph = microsoftGraph ?? new MicrosoftGraphEmailSender(storageDirectory);
        _current = File.Exists(_filePath)
            ? JsonSerializer.Deserialize<RuntimeEmailSettingsFile>(File.ReadAllText(_filePath))
              ?? throw new InvalidDataException("Runtime email settings are empty.")
            : new RuntimeEmailSettingsFile
            {
                SmtpHost = initialSettings.Host,
                SmtpPort = initialSettings.Port,
                SenderAddress = initialSettings.SenderAddress,
                UserName = initialSettings.UserName,
                EnableSsl = initialSettings.EnableSsl,
                EncryptedPassword = Protect(initialSettings.Password)
            };
    }

    public RuntimeEmailSettingsView GetSettings()
    {
        lock (_gate)
        {
            return new RuntimeEmailSettingsView(
                _current.SmtpHost, _current.SmtpPort, _current.SenderAddress,
                _current.UserName, _current.EnableSsl, _current.AutomaticErrorRecipients,
                _current.AutomaticErrorCcRecipients, !string.IsNullOrEmpty(_current.EncryptedPassword),
                _current.DeliveryMode, _current.MicrosoftClientId, _current.MicrosoftAccount);
        }
    }

    public RuntimeEmailSettingsView Save(RuntimeEmailSettingsUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (update.SmtpHost is null || update.SenderAddress is null || update.UserName is null
            || update.AutomaticErrorRecipients is null || update.AutomaticErrorCcRecipients is null)
            throw new ArgumentException("E-Mail settings cannot contain null text fields.");
        if (update.SmtpPort is < 1 or > 65535)
            throw new ArgumentException("SMTP port must be between 1 and 65535.");
        if (update.DeliveryMode is not ("smtp" or "microsoftGraph"))
            throw new ArgumentException("Delivery mode must be smtp or microsoftGraph.");
        if (update.DeliveryMode == "microsoftGraph" && !Guid.TryParse(update.MicrosoftClientId, out _))
            throw new ArgumentException("Microsoft Graph requires an Application (client) ID.");
        if (update.DeliveryMode == "microsoftGraph" && string.IsNullOrWhiteSpace(update.SenderAddress))
            throw new ArgumentException("Microsoft Graph requires the expected sender address.");
        if (!string.IsNullOrWhiteSpace(update.SenderAddress))
            _ = new MailAddress(update.SenderAddress);

        lock (_gate)
        {
            var next = new RuntimeEmailSettingsFile
            {
                SmtpHost = update.SmtpHost.Trim(),
                SmtpPort = update.SmtpPort,
                SenderAddress = update.SenderAddress.Trim(),
                UserName = update.UserName.Trim(),
                EnableSsl = update.EnableSsl,
                AutomaticErrorRecipients = update.AutomaticErrorRecipients.Trim(),
                AutomaticErrorCcRecipients = update.AutomaticErrorCcRecipients.Trim(),
                DeliveryMode = update.DeliveryMode,
                MicrosoftClientId = update.MicrosoftClientId.Trim(),
                MicrosoftAccount = string.Equals(update.MicrosoftClientId.Trim(), _current.MicrosoftClientId,
                    StringComparison.OrdinalIgnoreCase)
                    && string.Equals(update.SenderAddress.Trim(), _current.SenderAddress,
                        StringComparison.OrdinalIgnoreCase) ? _current.MicrosoftAccount : string.Empty,
                EncryptedPassword = update.Password is null
                    ? _current.EncryptedPassword
                    : Protect(update.Password)
            };
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var temporaryPath = _filePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(next));
            File.Move(temporaryPath, _filePath, true);
            _current = next;
            return GetSettings();
        }
    }

    public Task SendAsync(WorkflowEmailMessage message, CancellationToken cancellationToken)
    {
        SmtpWorkflowEmailSettings settings;
        string deliveryMode;
        string clientId;
        string microsoftAccount;
        string senderAddress;
        lock (_gate)
        {
            deliveryMode = _current.DeliveryMode;
            clientId = _current.MicrosoftClientId;
            microsoftAccount = _current.MicrosoftAccount;
            senderAddress = _current.SenderAddress;
            settings = new SmtpWorkflowEmailSettings
            {
                Host = _current.SmtpHost,
                Port = _current.SmtpPort,
                SenderAddress = _current.SenderAddress,
                UserName = string.IsNullOrWhiteSpace(_current.UserName) && !string.IsNullOrEmpty(_current.EncryptedPassword)
                    ? _current.SenderAddress
                    : _current.UserName,
                Password = deliveryMode == "smtp" ? Unprotect(_current.EncryptedPassword) : string.Empty,
                EnableSsl = _current.EnableSsl
            };
        }

        if (deliveryMode == "microsoftGraph")
            return _microsoftGraph.SendAsync(clientId, microsoftAccount, senderAddress, message, cancellationToken);
        if (!settings.IsConfigured)
            return UnavailableWorkflowEmailSender.Instance.SendAsync(message, cancellationToken);
        return new SmtpWorkflowEmailSender(settings).SendAsync(message, cancellationToken);
    }

    public Task<MicrosoftGraphConnectChallenge> StartMicrosoftConnectionAsync(CancellationToken cancellationToken)
    {
        string clientId;
        lock (_gate)
        {
            if (_current.DeliveryMode != "microsoftGraph")
                throw new InvalidOperationException("Select Microsoft Graph and save the settings first.");
            clientId = _current.MicrosoftClientId;
        }
        return _microsoftGraph.StartConnectionAsync(clientId, account =>
        {
            lock (_gate)
            {
                if (!string.Equals(clientId, _current.MicrosoftClientId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Microsoft Application (client) ID changed during sign-in. Connect again.");
                if (!string.Equals(account, _current.SenderAddress, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"Signed in as '{account}', but the configured sender is '{_current.SenderAddress}'. Connect the correct account.");
                _current.MicrosoftAccount = account;
                File.WriteAllText(_filePath + ".tmp", JsonSerializer.Serialize(_current));
                File.Move(_filePath + ".tmp", _filePath, true);
            }
        }, cancellationToken);
    }

    public MicrosoftGraphConnectionStatus GetMicrosoftConnectionStatus()
        => _microsoftGraph.GetConnectionStatus();

    private static string Protect(string value)
        => string.IsNullOrEmpty(value) ? string.Empty : !OperatingSystem.IsWindows()
            ? throw new PlatformNotSupportedException("SMTP password protection requires Windows.")
            : Convert.ToBase64String(
            ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(value), null, DataProtectionScope.LocalMachine));

    private static string Unprotect(string value)
        => string.IsNullOrEmpty(value) ? string.Empty : !OperatingSystem.IsWindows()
            ? throw new PlatformNotSupportedException("SMTP password protection requires Windows.")
            : System.Text.Encoding.UTF8.GetString(
            ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.LocalMachine));
}

public sealed class RuntimeEmailSettingsFile
{
    public string DeliveryMode { get; set; } = "smtp";
    public string MicrosoftClientId { get; set; } = string.Empty;
    public string MicrosoftAccount { get; set; } = string.Empty;
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public string SenderAddress { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public bool EnableSsl { get; set; } = true;
    public string AutomaticErrorRecipients { get; set; } = string.Empty;
    public string AutomaticErrorCcRecipients { get; set; } = string.Empty;
    public string EncryptedPassword { get; set; } = string.Empty;
}

public sealed record RuntimeEmailSettingsView(
    string SmtpHost, int SmtpPort, string SenderAddress, string UserName, bool EnableSsl,
    string AutomaticErrorRecipients, string AutomaticErrorCcRecipients, bool HasPassword,
    string DeliveryMode, string MicrosoftClientId, string MicrosoftAccount);

public sealed record RuntimeEmailSettingsUpdate(
    string SmtpHost, int SmtpPort, string SenderAddress, string UserName, bool EnableSsl,
    string AutomaticErrorRecipients, string AutomaticErrorCcRecipients, string? Password,
    string DeliveryMode = "smtp", string MicrosoftClientId = "");
