using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using MtgMcp.Archidekt;
using MtgMcp.App;
using MtgMcp.Core;
using MtgMcp.Playgroup;

namespace MtgMcp.App.Tests;

/// <summary>
/// Contains tests for command-line helpers.
/// </summary>
public sealed class CliTests
{
    /// <summary>
    /// Verifies that auth archidekt writes a credentials file and redacted setup output.
    /// </summary>
    [Fact]
    public void AuthArchidekt_WritesCredentialsFileAndRedactedSnippet()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string credentialsFile = Path.Combine(tempRoot, "nested", "archidekt.json");
            using StringWriter output = new();
            using StringWriter error = new();

            int exitCode = ArchidektAuthCommand.Run(
                [
                    "auth",
                    "archidekt",
                    "--credentials-file",
                    credentialsFile,
                    "--username",
                    "test-user",
                    "--password",
                    "secret-password",
                ],
                output,
                error
            );

            exitCode.Should().Be(0);
            error.ToString().Should().BeEmpty();
            File.Exists(credentialsFile).Should().BeTrue();

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(credentialsFile));
            JsonElement root = document.RootElement;
            root.GetProperty("username").GetString().Should().Be("test-user");
            root.GetProperty("password").GetString().Should().Be("secret-password");

            string text = output.ToString();
            text.Should().Contain("MTGMCP__ARCHIDEKT__CREDENTIALS_FILE");
            text.Should().Contain(credentialsFile);
            text.Should().Contain("username");
            text.Should().Contain("password");
            text.Should().NotContain("secret-password");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    /// <summary>
    /// Verifies that auth archidekt writes a file the Archidekt gateway can load.
    /// </summary>
    [Fact]
    public async Task AuthArchidekt_WritesCredentialFileLoadedByGateway()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string credentialsFile = Path.Combine(tempRoot, "archidekt.json");
            using StringWriter output = new();
            using StringWriter error = new();

            int exitCode = ArchidektAuthCommand.Run(
                [
                    "auth",
                    "archidekt",
                    "--credentials-file",
                    credentialsFile,
                    "--username",
                    "test-user",
                    "--password",
                    "secret-password",
                ],
                output,
                error
            );
            using ArchidektGateway gateway = new(
                new HttpClient(),
                Options.Create(
                    new ArchidektOptions
                    {
                        BaseAddress = new Uri("https://archidekt.test/"),
                        CredentialsFile = credentialsFile,
                    }
                )
            );

            AuthStatus status = await gateway.GetAuthStatusAsync(
                TestContext.Current.CancellationToken
            );

            exitCode.Should().Be(0);
            status.HasCredentialsFile.Should().BeTrue();
            status.HasUsernamePassword.Should().BeTrue();
            status.Mode.Should().Be("username-password");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    /// <summary>
    /// Verifies that auth archidekt reports write failures without exposing secrets.
    /// </summary>
    [Fact]
    public void AuthArchidekt_ReportsWriteFailureWithoutLeakingSecret()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string credentialsFile = Path.Combine(tempRoot, "archidekt.json");
            Directory.CreateDirectory(credentialsFile);
            using StringWriter output = new();
            using StringWriter error = new();

            int exitCode = ArchidektAuthCommand.Run(
                [
                    "auth",
                    "archidekt",
                    "--credentials-file",
                    credentialsFile,
                    "--username",
                    "test-user",
                    "--password",
                    "secret-password",
                ],
                output,
                error
            );

            exitCode.Should().Be(1);
            output.ToString().Should().BeEmpty();
            error.ToString().Should().Contain("could not be written");
            error.ToString().Should().NotContain("secret-password");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    /// <summary>
    /// Verifies that auth archidekt restricts file permissions on Unix-like systems.
    /// </summary>
    [Fact]
    public void AuthArchidekt_RestrictsUnixCredentialFilePermissions()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string tempRoot = CreateTempRoot();
        try
        {
            string credentialsFile = Path.Combine(tempRoot, "nested", "archidekt.json");
            using StringWriter output = new();
            using StringWriter error = new();

            int exitCode = ArchidektAuthCommand.Run(
                [
                    "auth",
                    "archidekt",
                    "--credentials-file",
                    credentialsFile,
                    "--username",
                    "test-user",
                    "--password",
                    "secret-password",
                ],
                output,
                error
            );

            exitCode.Should().Be(0);
            File.GetUnixFileMode(credentialsFile)
                .Should()
                .Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.GetUnixFileMode(Path.GetDirectoryName(credentialsFile)!)
                .Should()
                .Be(
                    UnixFileMode.UserRead
                        | UnixFileMode.UserWrite
                        | UnixFileMode.UserExecute
                );
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    /// <summary>
    /// Verifies that auth archidekt does not overwrite credentials by default.
    /// </summary>
    [Fact]
    public void AuthArchidekt_RefusesOverwriteWithoutForce()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string credentialsFile = Path.Combine(tempRoot, "archidekt.json");
            File.WriteAllText(credentialsFile, "original");
            using StringWriter output = new();
            using StringWriter error = new();

            int exitCode = ArchidektAuthCommand.Run(
                [
                    "auth",
                    "archidekt",
                    "--credentials-file",
                    credentialsFile,
                    "--username",
                    "test-user",
                    "--password",
                    "secret-password",
                ],
                output,
                error
            );

            exitCode.Should().Be(1);
            File.ReadAllText(credentialsFile).Should().Be("original");
            output.ToString().Should().BeEmpty();
            error.ToString().Should().Contain("already exists");
            error.ToString().Should().NotContain("secret-password");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    /// <summary>
    /// Verifies that auth archidekt requires an actual authentication credential.
    /// </summary>
    [Fact]
    public void AuthArchidekt_RequiresUsableCredential()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string credentialsFile = Path.Combine(tempRoot, "archidekt.json");
            using StringWriter output = new();
            using StringWriter error = new();

            int exitCode = ArchidektAuthCommand.Run(
                ["auth", "archidekt", "--credentials-file", credentialsFile, "--username", "test-user"],
                output,
                error
            );

            exitCode.Should().Be(1);
            File.Exists(credentialsFile).Should().BeFalse();
            error.ToString().Should().Contain("Provide --username with --password");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    /// <summary>
    /// Verifies that auth archidekt prints usage successfully when help is requested.
    /// </summary>
    [Fact]
    public void AuthArchidekt_PrintsHelp()
    {
        using StringWriter output = new();
        using StringWriter error = new();

        int exitCode = ArchidektAuthCommand.Run(
            ["auth", "archidekt", "--help"],
            output,
            error
        );

        exitCode.Should().Be(0);
        output.ToString().Should().Contain("Usage:");
        error.ToString().Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that auth archidekt reports unknown options through the parse-error path.
    /// </summary>
    [Fact]
    public void AuthArchidekt_ReportsUnknownOption()
    {
        using StringWriter output = new();
        using StringWriter error = new();

        int exitCode = ArchidektAuthCommand.Run(
            ["auth", "archidekt", "--wat", "value"],
            output,
            error
        );

        exitCode.Should().Be(1);
        output.ToString().Should().BeEmpty();
        error.ToString().Should().Contain("Unknown option '--wat'.");
        error.ToString().Should().Contain("Usage:");
    }

    /// <summary>
    /// Verifies that auth playgroup writes a credentials file and redacted setup output.
    /// </summary>
    [Fact]
    public void AuthPlaygroup_WritesCredentialsFileAndRedactedSnippet()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string credentialsFile = Path.Combine(tempRoot, "nested", "playgroup.json");
            using StringWriter output = new();
            using StringWriter error = new();

            int exitCode = PlaygroupAuthCommand.Run(
                [
                    "auth",
                    "playgroup",
                    "--credentials-file",
                    credentialsFile,
                    "--api-key",
                    "secret-api-key",
                ],
                output,
                error
            );

            exitCode.Should().Be(0);
            error.ToString().Should().BeEmpty();
            File.Exists(credentialsFile).Should().BeTrue();

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(credentialsFile));
            JsonElement root = document.RootElement;
            root.GetProperty("apiKey").GetString().Should().Be("secret-api-key");

            string text = output.ToString();
            text.Should().Contain("MTGMCP__PLAYGROUP__CREDENTIALS_FILE");
            text.Should().Contain(credentialsFile);
            text.Should().Contain("apiKey");
            text.Should().NotContain("secret-api-key");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    /// <summary>
    /// Verifies that auth playgroup writes a file the Playgroup gateway can load.
    /// </summary>
    [Fact]
    public async Task AuthPlaygroup_WritesCredentialFileLoadedByGateway()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string credentialsFile = Path.Combine(tempRoot, "playgroup.json");
            using StringWriter output = new();
            using StringWriter error = new();

            int exitCode = PlaygroupAuthCommand.Run(
                [
                    "auth",
                    "playgroup",
                    "--credentials-file",
                    credentialsFile,
                    "--api-key",
                    "secret-api-key",
                ],
                output,
                error
            );
            using HttpClient httpClient = new();
            PlaygroupGateway gateway = new(
                httpClient,
                Options.Create(
                    new PlaygroupOptions
                    {
                        BaseAddress = new Uri("https://playgroup.test/api/public/v1/"),
                        CredentialsFile = credentialsFile,
                    }
                )
            );

            PlaygroupAuthStatus status = await gateway.GetAuthStatusAsync(
                TestContext.Current.CancellationToken
            );

            exitCode.Should().Be(0);
            status.HasCredentialsFile.Should().BeTrue();
            status.HasApiKey.Should().BeTrue();
            status.Mode.Should().Be("api-key");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    /// <summary>
    /// Verifies that auth playgroup reports write failures without exposing secrets.
    /// </summary>
    [Fact]
    public void AuthPlaygroup_ReportsWriteFailureWithoutLeakingSecret()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string credentialsFile = Path.Combine(tempRoot, "playgroup.json");
            Directory.CreateDirectory(credentialsFile);
            using StringWriter output = new();
            using StringWriter error = new();

            int exitCode = PlaygroupAuthCommand.Run(
                [
                    "auth",
                    "playgroup",
                    "--credentials-file",
                    credentialsFile,
                    "--api-key",
                    "secret-api-key",
                ],
                output,
                error
            );

            exitCode.Should().Be(1);
            output.ToString().Should().BeEmpty();
            error.ToString().Should().Contain("could not be written");
            error.ToString().Should().NotContain("secret-api-key");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    /// <summary>
    /// Verifies that auth playgroup does not overwrite credentials by default.
    /// </summary>
    [Fact]
    public void AuthPlaygroup_RefusesOverwriteWithoutForce()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string credentialsFile = Path.Combine(tempRoot, "playgroup.json");
            File.WriteAllText(credentialsFile, "original");
            using StringWriter output = new();
            using StringWriter error = new();

            int exitCode = PlaygroupAuthCommand.Run(
                [
                    "auth",
                    "playgroup",
                    "--credentials-file",
                    credentialsFile,
                    "--api-key",
                    "secret-api-key",
                ],
                output,
                error
            );

            exitCode.Should().Be(1);
            File.ReadAllText(credentialsFile).Should().Be("original");
            output.ToString().Should().BeEmpty();
            error.ToString().Should().Contain("already exists");
            error.ToString().Should().NotContain("secret-api-key");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    /// <summary>
    /// Verifies that auth playgroup requires an API key.
    /// </summary>
    [Fact]
    public void AuthPlaygroup_RequiresApiKey()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string credentialsFile = Path.Combine(tempRoot, "playgroup.json");
            using StringWriter output = new();
            using StringWriter error = new();

            int exitCode = PlaygroupAuthCommand.Run(
                ["auth", "playgroup", "--credentials-file", credentialsFile],
                output,
                error
            );

            exitCode.Should().Be(1);
            File.Exists(credentialsFile).Should().BeFalse();
            error.ToString().Should().Contain("Provide --api-key");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    /// <summary>
    /// Verifies that auth playgroup prints usage successfully when help is requested.
    /// </summary>
    [Fact]
    public void AuthPlaygroup_PrintsHelp()
    {
        using StringWriter output = new();
        using StringWriter error = new();

        int exitCode = PlaygroupAuthCommand.Run(
            ["auth", "playgroup", "--help"],
            output,
            error
        );

        exitCode.Should().Be(0);
        output.ToString().Should().Contain("Usage:");
        output.ToString().Should().Contain("auth playgroup");
        error.ToString().Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that auth playgroup reports unknown options through the parse-error path.
    /// </summary>
    [Fact]
    public void AuthPlaygroup_ReportsUnknownOption()
    {
        using StringWriter output = new();
        using StringWriter error = new();

        int exitCode = PlaygroupAuthCommand.Run(
            ["auth", "playgroup", "--wat", "value"],
            output,
            error
        );

        exitCode.Should().Be(1);
        output.ToString().Should().BeEmpty();
        error.ToString().Should().Contain("Unknown option '--wat'.");
        error.ToString().Should().Contain("Usage:");
    }

    /// <summary>
    /// Verifies that top-level help prints without building the MCP host.
    /// </summary>
    [Fact]
    public async Task CliRunAsync_PrintsTopLevelHelp()
    {
        using StringWriter output = new();
        using StringWriter error = new();

        int exitCode = await MtgMcpCli.RunAsync(
            ["--help"],
            output,
            error,
            _ => throw new InvalidOperationException("Host should not be built.")
        );

        exitCode.Should().Be(0);
        output.ToString().Should().Contain("mtg-mcp [--smoke|--version]");
        output.ToString().Should().Contain("auth archidekt");
        output.ToString().Should().Contain("auth playgroup");
        error.ToString().Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that version output does not start the MCP host.
    /// </summary>
    [Fact]
    public async Task CliRunAsync_PrintsVersion()
    {
        using StringWriter output = new();
        using StringWriter error = new();

        int exitCode = await MtgMcpCli.RunAsync(
            ["--version"],
            output,
            error,
            _ => throw new InvalidOperationException("Host should not be built.")
        );

        exitCode.Should().Be(0);
        output.ToString().Should().StartWith("mtg-mcp ");
        error.ToString().Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that auth help prints both credential helper shapes without building the host.
    /// </summary>
    [Fact]
    public async Task CliRunAsync_PrintsAuthHelp()
    {
        using StringWriter output = new();
        using StringWriter error = new();

        int exitCode = await MtgMcpCli.RunAsync(
            ["auth", "--help"],
            output,
            error,
            _ => throw new InvalidOperationException("Host should not be built.")
        );

        exitCode.Should().Be(0);
        output.ToString().Should().Contain("mtg-mcp auth archidekt");
        output.ToString().Should().Contain("--username <name-or-email>");
        output.ToString().Should().Contain("mtg-mcp auth playgroup");
        output.ToString().Should().Contain("--api-key <key>");
        error.ToString().Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that auth commands are handled before the MCP host is built.
    /// </summary>
    [Fact]
    public async Task CliRunAsync_AuthCommandDoesNotBuildMcpHost()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string credentialsFile = Path.Combine(tempRoot, "archidekt.json");
            using StringWriter output = new();
            using StringWriter error = new();

            int exitCode = await MtgMcpCli
                .RunAsync(
                    [
                        "auth",
                        "archidekt",
                        "--credentials-file",
                        credentialsFile,
                        "--username",
                        "test-user",
                        "--password",
                        "secret-password",
                    ],
                    output,
                    error,
                    _ => throw new InvalidOperationException("Host should not be built.")
                );

            exitCode.Should().Be(0);
            File.Exists(credentialsFile).Should().BeTrue();
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    /// <summary>
    /// Verifies that the Playgroup auth command is handled before the MCP host is built.
    /// </summary>
    [Fact]
    public async Task CliRunAsync_PlaygroupAuthCommandDoesNotBuildMcpHost()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string credentialsFile = Path.Combine(tempRoot, "playgroup.json");
            using StringWriter output = new();
            using StringWriter error = new();

            int exitCode = await MtgMcpCli
                .RunAsync(
                    [
                        "auth",
                        "playgroup",
                        "--credentials-file",
                        credentialsFile,
                        "--api-key",
                        "secret-api-key",
                    ],
                    output,
                    error,
                    _ => throw new InvalidOperationException("Host should not be built.")
                );

            exitCode.Should().Be(0);
            File.Exists(credentialsFile).Should().BeTrue();
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    /// <summary>
    /// Creates a temporary test root.
    /// </summary>
    private static string CreateTempRoot()
    {
        string path = Path.Combine(Path.GetTempPath(), "mtg-mcp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Deletes a temporary test root.
    /// </summary>
    private static void DeleteTempRoot(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
