using System.Diagnostics;

namespace MtgMcp.E2E.Tests;

/// <summary>
/// Verifies the foundation executable across the real process boundary.
/// </summary>
public sealed class FoundationProcessTests
{
    /// <summary>
    /// Verifies that the built application accepts valid configuration and performs its smoke probe.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task SmokeProbe_WithValidConfiguration_StartsTheBuiltApplication()
    {
        (int exitCode, string output, string error) = await RunApplicationAsync(
            "--smoke",
            "--mode",
            "read-only",
            "--toolsets=decks",
            "--data-dir",
            Path.GetTempPath()).ConfigureAwait(false);

        Assert.Equal(0, exitCode);
        Assert.Equal($"mtg-mcp process ready{Environment.NewLine}", output);
        Assert.Equal(string.Empty, error);
    }

    /// <summary>
    /// Verifies that invalid startup configuration fails without echoing the rejected value.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task SmokeProbe_WithUnknownMode_ReturnsSanitizedFailure()
    {
        const string rejectedValue = "private-invalid-mode";

        (int exitCode, string output, string error) = await RunApplicationAsync(
            "--smoke",
            "--mode",
            rejectedValue).ConfigureAwait(false);

        Assert.Equal(2, exitCode);
        Assert.Equal(string.Empty, output);
        Assert.Equal($"Operation mode must be read-only, local, or remote.{Environment.NewLine}", error);
        Assert.DoesNotContain(rejectedValue, error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies invalid configuration is rejected before the stdio transport writes protocol output.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task McpHost_WithInvalidStartup_EmitsOnlySanitizedDiagnostic()
    {
        const string rejectedValue = "private-invalid-mode";

        (int exitCode, string output, string error) = await RunApplicationAsync(
            "--mode",
            rejectedValue).ConfigureAwait(false);

        Assert.Equal(2, exitCode);
        Assert.Equal(string.Empty, output);
        Assert.Equal($"Operation mode must be read-only, local, or remote.{Environment.NewLine}", error);
        Assert.DoesNotContain(rejectedValue, error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies an invalid toolset fails before transport without echoing the configured name.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task McpHost_WithInvalidToolsets_EmitsOnlySanitizedDiagnostic()
    {
        const string rejectedValue = "private-provider";

        (int exitCode, string output, string error) = await RunApplicationAsync(
            "--toolsets",
            rejectedValue).ConfigureAwait(false);

        Assert.Equal(2, exitCode);
        Assert.Equal(string.Empty, output);
        Assert.Contains("implemented lowercase capabilities", error, StringComparison.Ordinal);
        Assert.DoesNotContain(rejectedValue, error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies closing standard input cleanly stops the long-running MCP host.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task McpHost_WithClosedInput_ExitsWithoutProtocolOrDiagnosticNoise()
    {
        (int exitCode, string output, string error) = await RunWithEnvironmentAsync(
            ProviderCredentials,
            []).ConfigureAwait(false);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, output);
        Assert.Equal(string.Empty, error);
    }

    /// <summary>
    /// Verifies a start without provider credentials writes one stderr line for each provider toolset that is off,
    /// naming the settings to add and no secret value.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task McpHost_WithoutProviderCredentials_NamesTheSettingsOnStderr()
    {
        (int exitCode, string output, string error) = await RunWithEnvironmentAsync(
            NoCredentials,
            []).ConfigureAwait(false);

        string[] lines = error.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, output);
        Assert.Equal(2, lines.Length);
        Assert.Contains("'archidekt'", lines[0], StringComparison.Ordinal);
        Assert.Contains("credentials-missing", lines[0], StringComparison.Ordinal);
        Assert.Contains("ARCHIDEKT:USERNAME", lines[0], StringComparison.Ordinal);
        Assert.Contains("'playgroup'", lines[1], StringComparison.Ordinal);
        Assert.Contains("PLAYGROUP:API_KEY", lines[1], StringComparison.Ordinal);
    }

    /// <summary>
    /// Provides fake credentials for both providers, so every toolset is on and the host writes no notice.
    /// </summary>
    private static readonly Dictionary<string, string?> ProviderCredentials = new(StringComparer.Ordinal)
    {
        ["MTGMCP__ARCHIDEKT__USERNAME"] = "e2e-fake-user",
        ["MTGMCP__ARCHIDEKT__PASSWORD"] = "e2e-fake-password",
        ["MTGMCP__PLAYGROUP__API_KEY"] = "e2e-fake-key",
    };

    /// <summary>
    /// Removes provider credentials and points both credential files at paths that do not exist, so the server
    /// never falls back to a credential file in the user's profile.
    /// </summary>
    private static readonly Dictionary<string, string?> NoCredentials = new(StringComparer.Ordinal)
    {
        ["MTGMCP__ARCHIDEKT__USERNAME"] = null,
        ["MTGMCP__ARCHIDEKT__PASSWORD"] = null,
        ["MTGMCP__PLAYGROUP__API_KEY"] = null,
        ["MTGMCP__ARCHIDEKT__CREDENTIALS_FILE"] = Path.Combine(Path.GetTempPath(), "mtg-mcp-e2e-absent", "a.json"),
        ["MTGMCP__PLAYGROUP__CREDENTIALS_FILE"] = Path.Combine(Path.GetTempPath(), "mtg-mcp-e2e-absent", "p.json"),
    };

    /// <summary>
    /// Runs the built application with isolated configuration sources and captures its result.
    /// </summary>
    private static Task<(int ExitCode, string Output, string Error)> RunApplicationAsync(
        params string[] arguments)
    {
        return RunWithEnvironmentAsync(null, arguments);
    }

    /// <summary>
    /// Runs the built application with isolated configuration sources and extra environment values, and captures
    /// its result.
    /// </summary>
    private static async Task<(int ExitCode, string Output, string Error)> RunWithEnvironmentAsync(
        IReadOnlyDictionary<string, string?>? environment,
        string[] arguments)
    {
        string repositoryRoot = FindRepositoryRoot();
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Release";
        string appPath = Path.Combine(
            repositoryRoot,
            "src",
            "MtgMcp.App",
            "bin",
            configuration,
            "net11.0",
            "MtgMcp.App.dll");
        if (!File.Exists(appPath))
        {
            throw new FileNotFoundException("The built foundation application was not found.", appPath);
        }

        DirectoryInfo workingDirectory = Directory.CreateTempSubdirectory("mtg-mcp-e2e-");
        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = ResolveDotnetHost(),
                RedirectStandardError = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WorkingDirectory = workingDirectory.FullName,
            };
            startInfo.Environment.Remove("MTGMCP__MODE");
            startInfo.Environment.Remove("MTGMCP__DATA_DIR");
            startInfo.Environment.Remove("MTGMCP__TOOLSETS");
            foreach ((string key, string? value) in environment ?? new Dictionary<string, string?>())
            {
                if (value is null)
                {
                    startInfo.Environment.Remove(key);
                }
                else
                {
                    startInfo.Environment[key] = value;
                }
            }

            startInfo.ArgumentList.Add(appPath);
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The foundation process did not start.");
            process.StandardInput.Close();
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }

                throw;
            }

            string output = await outputTask.ConfigureAwait(false);
            string error = await errorTask.ConfigureAwait(false);
            return (process.ExitCode, output, error);
        }
        finally
        {
            workingDirectory.Refresh();
            if (workingDirectory.Exists)
            {
                workingDirectory.Delete(recursive: true);
            }
        }
    }

    /// <summary>
    /// Finds the dotnet host supplied by the test runner or the Mise environment.
    /// </summary>
    private static string ResolveDotnetHost()
    {
        string? testHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(testHost))
        {
            return testHost;
        }

        return "dotnet";
    }

    /// <summary>
    /// Finds the repository root from the test output directory.
    /// </summary>
    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "mtg-mcp.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the mtg-mcp repository root.");
    }
}
