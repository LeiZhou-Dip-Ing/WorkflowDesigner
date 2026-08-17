using System.Collections.ObjectModel;
using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Services;
using WorkflowCore.WpfDemo.Services.Scripting;
using WorkflowCore.WpfDemo.Services.Ui;
using WorkflowRuntime.Contracts;

namespace WorkflowCore.WpfDemo.ViewModels;

public sealed class SharpScriptLibraryManagerViewModel : ObservableObject
{
    private readonly WorkflowProject _project;
    private readonly IRuntimeApiClient _runtimeApi;
    private readonly ISharpScriptLibraryCache _cache;
    private readonly IEditorFileDialogs _fileDialogs;
    private readonly IEditorDialogs _dialogs;
    private ScriptLibraryItemViewModel? _selectedLibrary;
    private bool _isBusy;
    private string _statusText = "Loading Runtime Script Library Catalog...";
    private string _packageId = string.Empty;
    private string _packageVersion = string.Empty;
    private string _packageSource = "https://api.nuget.org/v3/index.json";

    public SharpScriptLibraryManagerViewModel(
        WorkflowProject project,
        IRuntimeApiClient runtimeApi,
        ISharpScriptLibraryCache cache,
        IEditorFileDialogs fileDialogs,
        IEditorDialogs dialogs)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _runtimeApi = runtimeApi ?? throw new ArgumentNullException(nameof(runtimeApi));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _fileDialogs = fileDialogs ?? throw new ArgumentNullException(nameof(fileDialogs));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        RefreshCommand = new RelayCommand(() => _ = RefreshAsync(), () => !IsBusy);
        AddToProjectCommand = new RelayCommand(item => _ = AddToProjectAsync(item as ScriptLibraryItemViewModel),
            item => !IsBusy && item is ScriptLibraryItemViewModel { IsInProject: false });
        RemoveFromProjectCommand = new RelayCommand(RemoveFromProject,
            item => !IsBusy && item is ScriptLibraryItemViewModel { IsInProject: true });
        ImportManagedDllCommand = new RelayCommand(() => _ = ImportManagedDllAsync(), () => !IsBusy);
        InstallNuGetCommand = new RelayCommand(() => _ = InstallNuGetAsync(),
            () => !IsBusy
                  && !string.IsNullOrWhiteSpace(PackageId)
                  && !string.IsNullOrWhiteSpace(PackageVersion)
                  && !string.IsNullOrWhiteSpace(PackageSource));
    }

    public ObservableCollection<ScriptLibraryItemViewModel> Libraries { get; } = new();

    public ScriptLibraryItemViewModel? SelectedLibrary
    {
        get => _selectedLibrary;
        set => SetProperty(ref _selectedLibrary, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value)) RaiseCommandStates();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string PackageId
    {
        get => _packageId;
        set
        {
            if (SetProperty(ref _packageId, value)) InstallNuGetCommand.RaiseCanExecuteChanged();
        }
    }

    public string PackageVersion
    {
        get => _packageVersion;
        set
        {
            if (SetProperty(ref _packageVersion, value)) InstallNuGetCommand.RaiseCanExecuteChanged();
        }
    }

    public string PackageSource
    {
        get => _packageSource;
        set
        {
            if (SetProperty(ref _packageSource, value)) InstallNuGetCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasProjectChanges { get; private set; }

    public RelayCommand RefreshCommand { get; }

    public RelayCommand AddToProjectCommand { get; }

    public RelayCommand RemoveFromProjectCommand { get; }

    public RelayCommand ImportManagedDllCommand { get; }

    public RelayCommand InstallNuGetCommand { get; }

    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var catalog = await _cache.RefreshCatalogAsync().ConfigureAwait(true);
            Libraries.Clear();
            foreach (var library in catalog.Libraries
                         .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.Version, StringComparer.OrdinalIgnoreCase))
            {
                Libraries.Add(new ScriptLibraryItemViewModel(
                    library,
                    IsSelected(library),
                    _cache.IsLocallyAvailable(library)));
            }

            SelectedLibrary = Libraries.FirstOrDefault();
            StatusText = $"{Libraries.Count} Runtime Script Librar{(Libraries.Count == 1 ? "y" : "ies")} available.";
        }
        catch (Exception exception)
        {
            StatusText = exception.Message;
            _dialogs.ShowError("Manage Script Libraries", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddToProjectAsync(ScriptLibraryItemViewModel? item)
    {
        if (item == null || item.IsInProject || IsBusy) return;
        IsBusy = true;
        try
        {
            var candidate = _project.ScriptLibraries.Concat(
            [
                new SharpScriptLibraryReferenceDto
                {
                    LibraryId = item.LibraryId,
                    Version = item.Version
                }
            ]).ToArray();
            await _cache.ResolveCompilationReferencesAsync(candidate).ConfigureAwait(true);
            _project.ScriptLibraries.Add(candidate[^1]);
            item.IsInProject = true;
            item.LocalAvailability = "Available";
            HasProjectChanges = true;
            StatusText = $"Added '{item.DisplayName}' {item.Version} to this Workflow Project.";
        }
        catch (Exception exception)
        {
            _dialogs.ShowError("Add Script Library", exception.Message);
            StatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RemoveFromProject(object? parameter)
    {
        if (parameter is not ScriptLibraryItemViewModel item || !item.IsInProject) return;
        _project.ScriptLibraries.RemoveAll(reference =>
            string.Equals(reference.LibraryId, item.LibraryId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(reference.Version, item.Version, StringComparison.OrdinalIgnoreCase));
        item.IsInProject = false;
        HasProjectChanges = true;
        StatusText = $"Removed '{item.DisplayName}' {item.Version} from this Workflow Project.";
        RaiseCommandStates();
    }

    private async Task ImportManagedDllAsync()
    {
        var path = _fileDialogs.SelectManagedAssemblyFile();
        if (string.IsNullOrWhiteSpace(path)) return;
        await RunInstallAsync(
            () => _runtimeApi.ImportScriptLibraryAsync(path),
            "Managed DLL").ConfigureAwait(true);
    }

    private async Task InstallNuGetAsync()
    {
        await RunInstallAsync(
            () => _runtimeApi.InstallScriptLibraryNuGetAsync(new InstallSharpScriptNuGetRequest
            {
                PackageId = PackageId.Trim(),
                Version = PackageVersion.Trim(),
                Source = PackageSource.Trim()
            }),
            "NuGet package").ConfigureAwait(true);
    }

    private async Task RunInstallAsync(
        Func<Task<SharpScriptLibraryInstallResponse>> install,
        string sourceKind)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var result = await install().ConfigureAwait(true);
            StatusText = result.AlreadyInstalled
                ? $"'{result.Library.DisplayName}' {result.Library.Version} was already installed."
                : $"Installed {sourceKind} '{result.Library.DisplayName}' {result.Library.Version}.";
        }
        catch (Exception exception)
        {
            _dialogs.ShowError($"Install {sourceKind}", exception.Message);
            StatusText = exception.Message;
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    private bool IsSelected(SharpScriptLibraryDescriptorDto library)
        => _project.ScriptLibraries.Any(reference =>
            string.Equals(reference.LibraryId, library.LibraryId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(reference.Version, library.Version, StringComparison.OrdinalIgnoreCase));

    private void RaiseCommandStates()
    {
        RefreshCommand.RaiseCanExecuteChanged();
        AddToProjectCommand.RaiseCanExecuteChanged();
        RemoveFromProjectCommand.RaiseCanExecuteChanged();
        ImportManagedDllCommand.RaiseCanExecuteChanged();
        InstallNuGetCommand.RaiseCanExecuteChanged();
    }
}

public sealed class ScriptLibraryItemViewModel : ObservableObject
{
    private bool _isInProject;
    private string _localAvailability;

    public ScriptLibraryItemViewModel(
        SharpScriptLibraryDescriptorDto library,
        bool isInProject,
        bool isLocallyAvailable)
    {
        Library = library;
        _isInProject = isInProject;
        _localAvailability = isLocallyAvailable ? "Available" : "Not cached";
    }

    public SharpScriptLibraryDescriptorDto Library { get; }
    public string DisplayName => Library.DisplayName;
    public string LibraryId => Library.LibraryId;
    public string Version => Library.Version;
    public string SourceKind => Library.SourceKind;
    public string RuntimeAvailability => Library.Availability;
    public string TargetFramework => Library.TargetFramework;
    public string Architecture => Library.Architecture;
    public string Namespaces => string.Join(Environment.NewLine, Library.Namespaces);
    public string AssemblyDetails => string.Join(Environment.NewLine, Library.CompilationAssemblies.Select(item =>
        $"{item.Name} {item.AssemblyVersion}  SHA-256 {item.Sha256}"));

    public bool IsInProject
    {
        get => _isInProject;
        set => SetProperty(ref _isInProject, value);
    }

    public string LocalAvailability
    {
        get => _localAvailability;
        set => SetProperty(ref _localAvailability, value);
    }
}
