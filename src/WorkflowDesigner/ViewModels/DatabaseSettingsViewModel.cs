using WorkflowCore.WpfDemo.Services;

namespace WorkflowCore.WpfDemo.ViewModels;

public sealed class DatabaseSettingsViewModel : ObservableObject
{
    private readonly IRuntimeApiClient _runtime;
    private string _connectionString = string.Empty;
    private string _server = string.Empty;
    private string _database = string.Empty;
    private string _status = string.Empty;
    private bool _isConfigured;
    private bool _clearConnection;
    private bool _isBusy;

    public DatabaseSettingsViewModel(IRuntimeApiClient runtime)
    {
        _runtime = runtime;
        RefreshCommand = new RelayCommand(() => _ = LoadAsync(), () => !IsBusy);
        SaveCommand = new RelayCommand(() => _ = SaveAsync(), () => !IsBusy);
        TestCommand = new RelayCommand(() => _ = TestAsync(), () => !IsBusy);
    }

    public string ConnectionString { get => _connectionString; set => SetProperty(ref _connectionString, value); }
    public string Server { get => _server; private set => SetProperty(ref _server, value); }
    public string Database { get => _database; private set => SetProperty(ref _database, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool IsConfigured { get => _isConfigured; private set => SetProperty(ref _isConfigured, value); }
    public bool ClearConnection { get => _clearConnection; set => SetProperty(ref _clearConnection, value); }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            RefreshCommand.RaiseCanExecuteChanged();
            SaveCommand.RaiseCanExecuteChanged();
            TestCommand.RaiseCanExecuteChanged();
        }
    }

    public RelayCommand RefreshCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand TestCommand { get; }

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            Apply(await _runtime.GetDatabaseSettingsAsync());
            Status = IsConfigured ? "Runtime database settings loaded." : "No Runtime database configured.";
        }
        catch (Exception error) { Status = $"Could not load database settings: {error.Message}"; }
        finally { IsBusy = false; }
    }

    private async Task SaveAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var connection = ClearConnection ? string.Empty : ConnectionString.Length == 0 ? null : ConnectionString;
            Apply(await _runtime.SaveDatabaseSettingsAsync(new("sqlServer", connection)));
            Status = "Database settings saved to Runtime.";
        }
        catch (Exception error) { Status = $"Could not save database settings: {error.Message}"; }
        finally { IsBusy = false; }
    }

    private async Task TestAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try { Status = (await _runtime.TestDatabaseConnectionAsync()).Message; }
        catch (Exception error) { Status = $"Connection test failed: {error.Message}"; }
        finally { IsBusy = false; }
    }

    private void Apply(RuntimeDatabaseSettingsDto settings)
    {
        Server = settings.Server;
        Database = settings.Database;
        IsConfigured = settings.IsConfigured;
        ConnectionString = string.Empty;
        ClearConnection = false;
    }
}
