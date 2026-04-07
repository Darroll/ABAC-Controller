using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

namespace AbacController.Tests.Integration.Fixtures;

/// <summary>
/// Starts the real Docker image via `sg docker -c "docker ..."` so the integration
/// suite exercises the shipped container exactly the way Darroll requested.
/// </summary>
public sealed class AbacContainerFixture : IAsyncLifetime
{
    private string _containerName = $"abac-it-{Guid.NewGuid():N}";
    private string _dataDirectory = Path.Combine(Path.GetTempPath(), $"abac-it-{Guid.NewGuid():N}");

    public string BaseUrl { get; private set; } = string.Empty;

    public string GrpcUrl { get; private set; } = string.Empty;

    public string DatabaseFilePath => Path.Combine(_dataDirectory, "integration-tests.db");

    public HttpClient HttpClient { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_dataDirectory);
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
        {
            // Required so the container's UID can write to the bind-mounted volume.
            await RunHostCommandAsync("chmod", $"0777 {_dataDirectory}");
        }

        var repoRoot = FindRepoRoot();
        await RunDockerAsync($"build -t abac-controller:v1.0 {repoRoot}");

        await RunDockerAsync($"run -d -P --name {_containerName} " +
                             $"-v {_dataDirectory}:/data " +
                             "-e ASPNETCORE_ENVIRONMENT=Development " +
                             "-e ABAC_Auth__EnableDevelopmentAuth=true " +
                             "-e ABAC_Database__ConnectionString='Data Source=/data/integration-tests.db' " +
                             "abac-controller:v1.0");

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

    public async Task DisposeAsync()
    {
        HttpClient?.Dispose();
        if (!string.IsNullOrWhiteSpace(_containerName))
        {
            try
            {
                await RunDockerAsync($"rm -f {_containerName}");
            }
            catch
            {
                // Best-effort cleanup in fixture teardown.
            }
        }

        if (Directory.Exists(_dataDirectory))
        {
            try
            {
                Directory.Delete(_dataDirectory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for test temp state.
            }
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

        var logs = await TryGetLogsAsync();
        throw new TimeoutException($"Container did not become healthy within 60 seconds. Last error: {lastError?.Message}\nLogs:\n{logs}");
    }

    public async Task<string> GetLogsAsync() => await TryGetLogsAsync();

    private async Task<string> TryGetLogsAsync()
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

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var hasSolution = File.Exists(Path.Combine(current.FullName, "AbacController.slnx"));
            var hasDockerfile = File.Exists(Path.Combine(current.FullName, "Dockerfile"));
            if (hasSolution && hasDockerfile)
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing AbacController.slnx and Dockerfile.");
    }

    private static async Task<string> RunDockerAsync(string dockerArgs)
    {
        // On Linux the original author runs the test suite under a non-docker user
        // and elevates with `sg docker -c "docker ..."`. On Windows (and macOS) docker
        // is invoked directly under the current user. Detect the OS at runtime so the
        // suite can run on either.
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
        {
            return await RunProcessAsync("sg", new[] { "docker", "-c", $"docker {dockerArgs}" }, $"docker {dockerArgs}");
        }

        return await RunProcessAsync("docker", SplitShellArgs(dockerArgs), $"docker {dockerArgs}");
    }

    /// <summary>
    /// Internal helper exposed so <see cref="ConfigurableAbacContainer"/> can reuse
    /// the same shell-style argument splitter.
    /// </summary>
    internal static IReadOnlyList<string> SplitShellArgsForTests(string input) => SplitShellArgs(input);

    /// <summary>
    /// Splits a shell-style argument string on whitespace while honouring single quotes
    /// (so values like <c>'Data Source=foo'</c> survive intact). Single quotes are
    /// stripped from the resulting tokens to match shell behaviour. We deliberately
    /// don't handle double quotes or backslash escapes — the call sites here only use
    /// single quotes.
    /// </summary>
    private static IReadOnlyList<string> SplitShellArgs(string input)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var c in input)
        {
            if (c == '\'')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0) result.Add(current.ToString());
        return result;
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

[CollectionDefinition("Docker")]
public sealed class DockerCollection : ICollectionFixture<AbacContainerFixture>
{
}
