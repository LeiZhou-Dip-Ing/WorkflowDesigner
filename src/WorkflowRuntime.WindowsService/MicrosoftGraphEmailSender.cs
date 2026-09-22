using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Identity.Client;
using WorkflowCore.Communication;

namespace WorkflowRuntime.WindowsService;

public sealed class MicrosoftGraphEmailSender
{
    private static readonly string[] Scopes = ["Mail.Send"];
    private static readonly HttpClient SharedHttpClient = new();
    private readonly string _cachePath;
    private readonly HttpClient _httpClient;
    private readonly object _gate = new();
    private string _connectionState = "disconnected";
    private string _connectionError = string.Empty;
    private Task? _connectionTask;

    public MicrosoftGraphEmailSender(string storageDirectory, HttpClient? httpClient = null)
    {
        _cachePath = Path.Combine(storageDirectory, "microsoft-mail-token-cache.dat");
        _httpClient = httpClient ?? SharedHttpClient;
    }

    public MicrosoftGraphConnectionStatus GetConnectionStatus()
    {
        lock (_gate) return new MicrosoftGraphConnectionStatus(_connectionState, _connectionError);
    }

    public async Task<MicrosoftGraphConnectChallenge> StartConnectionAsync(
        string clientId, Action<string> onConnected, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(clientId, out _))
            throw new ArgumentException("A valid Microsoft Application (client) ID is required.");

        var challenge = new TaskCompletionSource<MicrosoftGraphConnectChallenge>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_connectionTask is { IsCompleted: false })
                throw new InvalidOperationException("A Microsoft account connection is already in progress.");
            _connectionState = "pending";
            _connectionError = string.Empty;
            _connectionTask = ConnectAsync(clientId, onConnected, challenge);
        }
        return await challenge.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SendAsync(string clientId, string connectedAccount, string expectedSender,
        WorkflowEmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!string.Equals(connectedAccount, expectedSender, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The connected Microsoft account does not match the configured sender address.");
        var app = CreateApplication(clientId);
        var accounts = await app.GetAccountsAsync().ConfigureAwait(false);
        var account = accounts.SingleOrDefault(candidate =>
            string.Equals(candidate.Username, connectedAccount, StringComparison.OrdinalIgnoreCase));
        if (account is null)
            throw new InvalidOperationException("Microsoft mail is not connected. Open Settings > E-Mail and connect the account.");

        AuthenticationResult auth;
        try
        {
            auth = await app.AcquireTokenSilent(Scopes, account).ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (MsalUiRequiredException error)
        {
            throw new InvalidOperationException(
                "Microsoft mail authorization expired. Reconnect the account in Settings > E-Mail.", error);
        }

        var attachments = new List<object>();
        foreach (var path in message.AttachmentPaths)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Email attachment was not found.", path);
            var file = new FileInfo(path);
            if (file.Length > 3_000_000)
                throw new InvalidOperationException("Microsoft Graph email attachments must be smaller than 3 MB.");
            attachments.Add(new Dictionary<string, object>
            {
                ["@odata.type"] = "#microsoft.graph.fileAttachment",
                ["name"] = file.Name,
                ["contentBytes"] = Convert.ToBase64String(await File.ReadAllBytesAsync(path, cancellationToken))
            });
        }

        var payload = new
        {
            message = new
            {
                subject = message.Subject,
                body = new { contentType = "Text", content = message.Body },
                toRecipients = message.Recipients.Select(address => new { emailAddress = new { address } }),
                ccRecipients = message.CcRecipients.Select(address => new { emailAddress = new { address } }),
                attachments
            }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/me/sendMail")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Microsoft Graph rejected the email (HTTP {(int)response.StatusCode}). " +
                "Check the connected account and Mail.Send permission.");
    }

    private async Task ConnectAsync(string clientId, Action<string> onConnected,
        TaskCompletionSource<MicrosoftGraphConnectChallenge> challenge)
    {
        try
        {
            var app = CreateApplication(clientId);
            var result = await app.AcquireTokenWithDeviceCode(Scopes, code =>
            {
                challenge.TrySetResult(new MicrosoftGraphConnectChallenge(
                    code.VerificationUrl, code.UserCode, code.Message, code.ExpiresOn));
                return Task.CompletedTask;
            }).ExecuteAsync().ConfigureAwait(false);
            onConnected(result.Account.Username);
            lock (_gate) _connectionState = "connected";
        }
        catch (Exception error)
        {
            challenge.TrySetException(error);
            lock (_gate)
            {
                _connectionState = "failed";
                _connectionError = error.Message;
            }
        }
    }

    private IPublicClientApplication CreateApplication(string clientId)
    {
        if (!Guid.TryParse(clientId, out _))
            throw new InvalidOperationException("Microsoft Application (client) ID is not configured.");
        var app = PublicClientApplicationBuilder.Create(clientId)
            .WithAuthority("https://login.microsoftonline.com/consumers")
            .Build();
        app.UserTokenCache.SetBeforeAccess(args =>
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("Microsoft mail token protection requires Windows.");
            lock (_gate)
            {
                if (!File.Exists(_cachePath)) return;
                var protectedBytes = File.ReadAllBytes(_cachePath);
                args.TokenCache.DeserializeMsalV3(ProtectedData.Unprotect(
                    protectedBytes, null, DataProtectionScope.CurrentUser));
            }
        });
        app.UserTokenCache.SetAfterAccess(args =>
        {
            if (!args.HasStateChanged) return;
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("Microsoft mail token protection requires Windows.");
            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
                var protectedBytes = ProtectedData.Protect(
                    args.TokenCache.SerializeMsalV3(), null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(_cachePath + ".tmp", protectedBytes);
                File.Move(_cachePath + ".tmp", _cachePath, true);
            }
        });
        return app;
    }
}

public sealed record MicrosoftGraphConnectChallenge(
    string VerificationUrl, string UserCode, string Message, DateTimeOffset ExpiresOn);

public sealed record MicrosoftGraphConnectionStatus(string State, string Error);
