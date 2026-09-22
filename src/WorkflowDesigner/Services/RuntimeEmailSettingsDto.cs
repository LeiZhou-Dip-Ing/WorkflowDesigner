namespace WorkflowCore.WpfDemo.Services;

public sealed record RuntimeEmailSettingsDto(
    string SmtpHost, int SmtpPort, string SenderAddress, string UserName, bool EnableSsl,
    string AutomaticErrorRecipients, string AutomaticErrorCcRecipients, bool HasPassword,
    string DeliveryMode = "smtp", string MicrosoftClientId = "", string MicrosoftAccount = "");

public sealed record RuntimeEmailSettingsUpdateDto(
    string SmtpHost, int SmtpPort, string SenderAddress, string UserName, bool EnableSsl,
    string AutomaticErrorRecipients, string AutomaticErrorCcRecipients, string? Password,
    string DeliveryMode = "smtp", string MicrosoftClientId = "");

public sealed record MicrosoftGraphConnectChallengeDto(
    string VerificationUrl, string UserCode, string Message, DateTimeOffset ExpiresOn);

public sealed record MicrosoftGraphConnectionStatusDto(string State, string Error);
