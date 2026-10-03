using System.Diagnostics;
using System.Net.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WorkflowRuntime.WindowsService.Grafana;

public sealed class GrafanaServerHostedService : IHostedService, IDisposable
{
    private readonly GrafanaRuntimeOptions _options;
    private readonly ILogger<GrafanaServerHostedService> _logger;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(3) };
    private Process? _ownedProcess;
    private string _lastHealthFailure = "Grafana has not answered the health request.";

    public GrafanaServerHostedService(
        WorkflowRuntimeOptions runtimeOptions,
        ILogger<GrafanaServerHostedService> logger)
    {
        _options = runtimeOptions.Grafana;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled) return;
        if (await IsHealthyAsync(cancellationToken).ConfigureAwait(false))
        {
            _logger.LogInformation("Using Grafana already running at {BaseUrl}.", _options.BaseUrl);
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.ServerExecutablePath))
            throw new InvalidOperationException(
                "Grafana is offline and WorkflowRuntime:Grafana:ServerExecutablePath is not configured.");
        if (!File.Exists(_options.ServerExecutablePath))
            throw new FileNotFoundException("Configured Grafana server executable was not found.", _options.ServerExecutablePath);

        var workingDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(_options.ServerWorkingDirectory)
            ? Path.GetDirectoryName(_options.ServerExecutablePath)!
            : _options.ServerWorkingDirectory);
        var arguments = _options.ServerArguments.Trim();
        if (!arguments.Contains("--homepath", StringComparison.OrdinalIgnoreCase))
        {
            arguments = $"{arguments} --homepath \"{workingDirectory}\"".Trim();
        }
        _ownedProcess = Process.Start(new ProcessStartInfo
        {
            FileName = _options.ServerExecutablePath,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        }) ?? throw new InvalidOperationException("Grafana server process could not be started.");

        var deadline = DateTimeOffset.UtcNow.AddSeconds(_options.StartupTimeoutSeconds);
        try
        {
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_ownedProcess.HasExited)
                    throw new InvalidOperationException(
                        $"Grafana exited with code {_ownedProcess.ExitCode} during startup. Check {GetLogPath(workingDirectory)}.");
                if (await IsHealthyAsync(cancellationToken).ConfigureAwait(false))
                {
                    _logger.LogInformation("Started Grafana at {BaseUrl}.", _options.BaseUrl);
                    return;
                }
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }

            throw new TimeoutException(
                $"Grafana did not become healthy within {_options.StartupTimeoutSeconds} seconds at {_options.BaseUrl}/api/health. "
                + $"Last check: {_lastHealthFailure} Check {GetLogPath(workingDirectory)}.");
        }
        catch
        {
            StopOwnedProcess();
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        StopOwnedProcess();
        return Task.CompletedTask;
    }

    private async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(
                $"{_options.BaseUrl.TrimEnd('/')}/api/health", cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) return true;
            _lastHealthFailure = $"HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).";
            return false;
        }
        catch (HttpRequestException error)
        {
            _lastHealthFailure = error.Message;
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _lastHealthFailure = "Health request timed out.";
            return false;
        }
    }

    private static string GetLogPath(string workingDirectory)
        => Path.Combine(workingDirectory, "data", "log", "grafana.log");

    private void StopOwnedProcess()
    {
        if (_ownedProcess is null) return;
        try
        {
            if (!_ownedProcess.HasExited) _ownedProcess.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            _ownedProcess.Dispose();
            _ownedProcess = null;
        }
    }

    public void Dispose()
    {
        StopOwnedProcess();
        _httpClient.Dispose();
    }
}
