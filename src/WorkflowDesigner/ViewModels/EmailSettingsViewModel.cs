using WorkflowCore.WpfDemo.Services;

namespace WorkflowCore.WpfDemo.ViewModels;

public sealed class EmailSettingsViewModel : ObservableObject
{
    private readonly IRuntimeApiClient _runtime;
    private string _smtpHost = string.Empty;
    private string _smtpPort = "587";
    private string _senderAddress = string.Empty;
    private string _userName = string.Empty;
    private string _automaticErrorRecipients = string.Empty;
    private string _automaticErrorCcRecipients = string.Empty;
    private string _password = string.Empty;
    private string _status = string.Empty;
    private string _deliveryMode = "smtp";
    private string _microsoftClientId = string.Empty;
    private string _savedMicrosoftClientId = string.Empty;
    private string _savedSenderAddress = string.Empty;
    private string _microsoftAccount = string.Empty;
    private string _verificationUrl = string.Empty;
    private string _userCode = string.Empty;
    private bool _isConnecting;
    private bool _enableSsl = true;
    private bool _hasPassword;
    private bool _clearPassword;
    private bool _isBusy;

    public EmailSettingsViewModel(IRuntimeApiClient runtime)
    {
        _runtime = runtime;
        SaveCommand = new RelayCommand(() => _ = SaveAsync(), () => !IsBusy && !IsConnecting);
        RefreshCommand = new RelayCommand(() => _ = LoadAsync(), () => !IsBusy && !IsConnecting);
        ConnectMicrosoftCommand = new RelayCommand(() => _ = ConnectMicrosoftAsync(),
            () => !IsBusy && !IsConnecting && IsMicrosoftGraphMode);
    }

    public string SmtpHost { get => _smtpHost; set => SetProperty(ref _smtpHost, value); }
    public string SmtpPort { get => _smtpPort; set => SetProperty(ref _smtpPort, value); }
    public string SenderAddress { get => _senderAddress; set => SetProperty(ref _senderAddress, value); }
    public string UserName { get => _userName; set => SetProperty(ref _userName, value); }
    public bool EnableSsl { get => _enableSsl; set => SetProperty(ref _enableSsl, value); }
    public string AutomaticErrorRecipients { get => _automaticErrorRecipients; set => SetProperty(ref _automaticErrorRecipients, value); }
    public string AutomaticErrorCcRecipients { get => _automaticErrorCcRecipients; set => SetProperty(ref _automaticErrorCcRecipients, value); }
    public string Password { get => _password; set => SetProperty(ref _password, value); }
    public string DeliveryMode
    {
        get => _deliveryMode;
        set
        {
            if (!SetProperty(ref _deliveryMode, value)) return;
            OnPropertyChanged(nameof(IsSmtpMode));
            OnPropertyChanged(nameof(IsMicrosoftGraphMode));
            ConnectMicrosoftCommand.RaiseCanExecuteChanged();
        }
    }
    public bool IsSmtpMode => DeliveryMode == "smtp";
    public bool IsMicrosoftGraphMode => DeliveryMode == "microsoftGraph";
    public string MicrosoftClientId { get => _microsoftClientId; set => SetProperty(ref _microsoftClientId, value); }
    public string MicrosoftAccount { get => _microsoftAccount; private set => SetProperty(ref _microsoftAccount, value); }
    public string VerificationUrl { get => _verificationUrl; private set => SetProperty(ref _verificationUrl, value); }
    public string UserCode
    {
        get => _userCode;
        private set
        {
            if (SetProperty(ref _userCode, value)) OnPropertyChanged(nameof(HasUserCode));
        }
    }
    public bool HasUserCode => !string.IsNullOrEmpty(UserCode);
    public bool IsConnecting
    {
        get => _isConnecting;
        private set
        {
            if (!SetProperty(ref _isConnecting, value)) return;
            ConnectMicrosoftCommand.RaiseCanExecuteChanged();
            SaveCommand.RaiseCanExecuteChanged();
            RefreshCommand.RaiseCanExecuteChanged();
        }
    }
    public bool HasPassword { get => _hasPassword; private set => SetProperty(ref _hasPassword, value); }
    public bool ClearPassword { get => _clearPassword; set => SetProperty(ref _clearPassword, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                SaveCommand.RaiseCanExecuteChanged();
                RefreshCommand.RaiseCanExecuteChanged();
                ConnectMicrosoftCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public RelayCommand SaveCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand ConnectMicrosoftCommand { get; }

    public async Task LoadAsync()
    {
        if (IsBusy || IsConnecting) return;
        IsBusy = true;
        Status = "Loading Runtime settings...";
        try
        {
            var settings = await _runtime.GetEmailSettingsAsync();
            SmtpHost = settings.SmtpHost;
            SmtpPort = settings.SmtpPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
            SenderAddress = settings.SenderAddress;
            UserName = settings.UserName;
            EnableSsl = settings.EnableSsl;
            AutomaticErrorRecipients = settings.AutomaticErrorRecipients;
            AutomaticErrorCcRecipients = settings.AutomaticErrorCcRecipients;
            HasPassword = settings.HasPassword;
            DeliveryMode = settings.DeliveryMode;
            MicrosoftClientId = settings.MicrosoftClientId;
            _savedMicrosoftClientId = settings.MicrosoftClientId;
            _savedSenderAddress = settings.SenderAddress;
            MicrosoftAccount = settings.MicrosoftAccount;
            Password = string.Empty;
            ClearPassword = false;
            Status = IsMicrosoftGraphMode
                ? string.IsNullOrEmpty(MicrosoftAccount) ? "Microsoft account not connected." : $"Connected: {MicrosoftAccount}"
                : HasPassword ? "Runtime settings loaded. Password is saved." : "Runtime settings loaded.";
        }
        catch (Exception error)
        {
            Status = $"Could not load Runtime settings: {error.Message}";
        }
        finally { IsBusy = false; }
    }

    private async Task SaveAsync()
    {
        if (IsBusy || IsConnecting) return;
        if (!int.TryParse(SmtpPort, out var port) || port is < 1 or > 65535)
        {
            Status = "SMTPPort must be between 1 and 65535.";
            return;
        }
        if (IsMicrosoftGraphMode && !Guid.TryParse(MicrosoftClientId, out _))
        {
            Status = "Enter a valid Microsoft Application (client) ID.";
            return;
        }
        if (IsMicrosoftGraphMode && string.IsNullOrWhiteSpace(SenderAddress))
        {
            Status = "Enter the expected Microsoft sender address.";
            return;
        }

        IsBusy = true;
        Status = "Saving Runtime settings...";
        try
        {
            var saved = await _runtime.SaveEmailSettingsAsync(new RuntimeEmailSettingsUpdateDto(
                SmtpHost, port, SenderAddress, UserName, EnableSsl,
                AutomaticErrorRecipients, AutomaticErrorCcRecipients,
                ClearPassword ? string.Empty : Password.Length == 0 ? null : Password,
                DeliveryMode, MicrosoftClientId));
            HasPassword = saved.HasPassword;
            _savedMicrosoftClientId = saved.MicrosoftClientId;
            _savedSenderAddress = saved.SenderAddress;
            MicrosoftAccount = saved.MicrosoftAccount;
            Password = string.Empty;
            ClearPassword = false;
            Status = "E-Mail settings saved to Runtime.";
        }
        catch (Exception error)
        {
            Status = $"Could not save E-Mail settings: {error.Message}";
        }
        finally { IsBusy = false; }
    }

    private async Task ConnectMicrosoftAsync()
    {
        if (IsBusy || IsConnecting) return;
        if (!string.Equals(MicrosoftClientId, _savedMicrosoftClientId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(SenderAddress, _savedSenderAddress, StringComparison.OrdinalIgnoreCase))
        {
            Status = "Save the Microsoft sender address and Application (client) ID before connecting.";
            return;
        }
        IsConnecting = true;
        Status = "Requesting Microsoft sign-in code...";
        try
        {
            var challenge = await _runtime.StartMicrosoftMailConnectionAsync();
            VerificationUrl = challenge.VerificationUrl;
            UserCode = challenge.UserCode;
            Status = "Open the Microsoft sign-in page and enter the code shown below.";
            _ = PollConnectionAsync(challenge.ExpiresOn);
        }
        catch (Exception error)
        {
            IsConnecting = false;
            Status = $"Could not start Microsoft sign-in: {error.Message}";
        }
    }

    private async Task PollConnectionAsync(DateTimeOffset expiresOn)
    {
        try
        {
            while (DateTimeOffset.UtcNow < expiresOn)
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
                var connection = await _runtime.GetMicrosoftMailConnectionStatusAsync();
                if (connection.State == "pending") continue;
                if (connection.State == "failed")
                {
                    Status = $"Microsoft sign-in failed: {connection.Error}";
                    return;
                }
                if (connection.State == "connected")
                {
                    var settings = await _runtime.GetEmailSettingsAsync();
                    MicrosoftAccount = settings.MicrosoftAccount;
                    UserCode = string.Empty;
                    Status = $"Connected: {MicrosoftAccount}";
                    return;
                }
            }
            Status = "Microsoft sign-in code expired. Connect again.";
        }
        catch (Exception error)
        {
            Status = $"Could not check Microsoft sign-in: {error.Message}";
        }
        finally { IsConnecting = false; }
    }
}
