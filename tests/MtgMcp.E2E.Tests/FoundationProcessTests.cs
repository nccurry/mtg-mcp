using System.Diagnostics;

namespace MtgMcp.E2E.Tests;

/// <summary>
/// Verifies the foundation executable across the real process boundary.
/// </summary>
public sealed class FoundationProcessTests
{
    /// <summary>
    /// Verifies that the built application can perform its temporary smoke probe.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task SmokeProbe_StartsTheBuiltApplication()
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

        Assert.True(File.Exists(appPath), $"Expected the built application at '{appPath}'.");

        ProcessStartInfo startInfo = new()
        {
            FileName = ResolveDotnetHost(repositoryRoot),
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(appPath);
        startInfo.ArgumentList.Add("--smoke");

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The foundation process did not start.");
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        Task<string> errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        string output = await outputTask.ConfigureAwait(false);
        string error = await errorTask.ConfigureAwait(false);

        Assert.Equal(0, process.ExitCode);
        Assert.Equal($"mtg-mcp foundation process ready{Environment.NewLine}", output);
        Assert.Equal(string.Empty, error);
    }

    /// <summary>
    /// Finds the dotnet host used by the test runner or the repository toolchain.
    /// </summary>
    private static string ResolveDotnetHost(string repositoryRoot)
    {
        string? testHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(testHost))
        {
            return testHost;
        }

        string executableName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        string repositoryHost = Path.Combine(repositoryRoot, ".dotnet", executableName);
        return File.Exists(repositoryHost) ? repositoryHost : "dotnet";
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
