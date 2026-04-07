using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

namespace AbacController.Tests.Integration.Fixtures;

/// <summary>
/// Starts an ABAC Controller Docker container with caller-supplied environment overrides.
/// Intended for isolated integration scenarios such as auth and rate-limiting tests.
/// </summary>
public sealed class ConfigurableAbacContainer : IAsyncDisposable
{
    private readonly Dictionary<string, string> _environment;
    private readonly string _containerName = $"abac-it-{Guid.NewGuid():N}";
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), $"abac-it-{Guid.NewGuid():N}");

    public string BaseUrl { get; private set; } = string.Empty;
    public string GrpcUrl { get; private set; } = string.Empty;
    public HttpClient HttpClient { get; private set; } = null!;

    public ConfigurableAbacContainer(Dictionary<string, string>? environment = null)
    {
        _environment = environment ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public async Task StartAsync()
    {
        Directory.CreateDirectory(_dataDirectory);
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
        {
            await RunHostCommandAsync("chmod", $"0777 {_dataDirectory}");
        }

        var repoRoot = FindRepoRoot();
        await RunDockerAsync($"build -t abac-controller:v1.0 {repoRoot}");

        var envArgs = new List<string>
        {
            "-e ASPNETCORE_ENVIRONMENT=Development",
            $"-e ABAC_Database__ConnectionString='Data Source=/data/integration-tests.db'"
        };

        foreach (var kvp in _environment)
        {
            envArgs.Add($"-e {kvp.Key}='{kvp.Value.Replace("'", "'\\''")}'");
        }

        await RunDockerAsync($"run -d -P --name {_containerName} -v {_dataDirectory}:/data {string.Join(" ", envArgs)} abac-controller:v1.0");

        var httpPort = (await RunDockerAsync($"port {_containerName} 8080/tcp")).Trim();
        var grpcPort = (await RunDockerAsync($"port {_containerName} 8081/tcp")).Trim();
        var mappedHttpPort = httpPort.Split(':', StringSplitOptions.RemoveEmptyEntries).Last();
        var mappedGrpcPort = grpcPort.Split(':', StringSplitOptions.RemoveEmptyEntries).Last();
        BaseUrl = $"http://127.0.0.1:{mappedHttpPort}";
        GrpcUrl = $"http://127.0.0.1:{mappedGrpcPort}";

        HttpClient = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };
        HttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        await WaitForHealthyAsync();
    }

    public async ValueTask DisposeAsync()
    {
        HttpClient?.Dispose();

        try
        {
            await RunDockerAsync($"rm -f {_containerName}");
        }
        catch
        {
            // Best-effort cleanup.
        }

        try
        {
            if (Directory.Exists(_dataDirectory))
            {
                Directory.Delete(_dataDirectory, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    public async Task<string> GetLogsAsync()
    {
        try
        {
            return await RunDockerAsync($"logs {_containerName}");
        }
        catch (Exception ex)
        {
            return $"Failed to read logs: {ex.Message}";
        }
    }

    private async Task WaitForHealthyAsync()
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(60);
        Exception? lastError = null;

        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            try
            {
                var live = await HttpClient.GetAsync("/health/live");
                var ready = await HttpClient.GetAsync("/health/ready");
                if (live.StatusCode == HttpStatusCode.OK && ready.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            await Task.Delay(1000);
        }

        throw new TimeoutException($"Container did not become healthy within 60 seconds. Last error: {lastError?.Message}\nLogs:\n{await GetLogsAsync()}");
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AbacController.slnx")) &&
                File.Exists(Path.Combine(current.FullName, "Dockerfile")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing AbacController.slnx and Dockerfile.");
    }

    private static async Task<string> RunDockerAsync(string dockerArgs)
    {
        // Linux: elevate via `sg docker -c "docker ..."` for non-docker user setups.
        // Windows / macOS: invoke docker directly. (See AbacContainerFixture.cs.)
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
        {
            return await RunProcessAsync("sg", ["docker", "-c", $"docker {dockerArgs}"], $"docker {dockerArgs}");
        }

        return await RunProcessAsync("docker", AbacContainerFixture.SplitShellArgsForTests(dockerArgs), $"docker {dockerArgs}");
    }

    private static async Task<string> RunHostCommandAsync(string fileName, string arguments)
        => await RunProcessAsync(fileName, arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries), $"{fileName} {arguments}");

    private static async Task<string> RunProcessAsync(string fileName, IReadOnlyCollection<string> arguments, string displayCommand)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start process: {displayCommand}");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Command failed (exit {process.ExitCode}): {displayCommand}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        }

        return string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
    }
}
