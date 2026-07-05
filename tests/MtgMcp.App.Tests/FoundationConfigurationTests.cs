using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MtgMcp.App.Capabilities;
using MtgMcp.App.Configuration;
using MtgMcp.Archidekt;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies configuration precedence, data-root isolation, and the legacy clean-break boundary.
/// </summary>
[Collection(ProcessEnvironmentTestGroup.Name)]
public sealed class FoundationConfigurationTests
{
    /// <summary>
    /// Provides the web serializer behavior used by the future MCP host.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Verifies that omitted configuration selects local mode and the versioned platform path without creating it.
    /// </summary>
    [Fact]
    public void Resolve_OmittedValues_UsesLocalVersionedPathWithoutCreatingIt()
    {
        using TemporaryDirectory temporary = new();
        IConfiguration configuration = new ConfigurationBuilder().Build();
        string expectedPath = Path.Combine(temporary.Path, "mtg-mcp", "v0.9");

        FoundationConfiguration resolved = RequireSuccess(
            FoundationConfigurationLoader.Resolve(configuration, temporary.Path, string.Empty));

        Assert.Equal(OperationMode.Local, resolved.Mode);
        Assert.Equal(CapabilityToolsetSelectionKind.Default, resolved.Toolsets.Kind);
        Assert.True(resolved.Toolsets.Includes(CapabilityToolset.Decks));
        Assert.True(resolved.Toolsets.Includes(CapabilityToolset.Scryfall));
        Assert.Equal(TimeSpan.FromHours(24), resolved.ScryfallFreshnessTtl);
        Assert.Equal(expectedPath, resolved.DataRoot);
        Assert.Equal(DataRootState.NotCreated, resolved.DataRootState);
        Assert.False(resolved.DataRootConfigured);
        Assert.False(Directory.Exists(expectedPath));
    }

    /// <summary>
    /// Verifies the roaming application-data folder is used when the local folder is unavailable.
    /// </summary>
    [Fact]
    public void Resolve_MissingLocalApplicationData_UsesRoamingVersionedPath()
    {
        using TemporaryDirectory temporary = new();
        IConfiguration configuration = new ConfigurationBuilder().Build();

        FoundationConfiguration resolved = RequireSuccess(
            FoundationConfigurationLoader.Resolve(configuration, string.Empty, temporary.Path));

        Assert.Equal(Path.Combine(temporary.Path, "mtg-mcp", "v0.9"), resolved.DataRoot);
        Assert.Equal(DataRootState.NotCreated, resolved.DataRootState);
        Assert.False(Directory.Exists(resolved.DataRoot));
    }

    /// <summary>
    /// Verifies that explicit mode and data-root values are normalized without exposing the path in public status.
    /// </summary>
    [Fact]
    public void Resolve_ExplicitValues_ProjectsPathFreeStatus()
    {
        using TemporaryDirectory temporary = new();
        string configuredPath = Path.Combine(temporary.Path, "private-data");
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MODE"] = "REMOTE",
                ["DATA_DIR"] = configuredPath,
            })
            .Build();

        FoundationConfiguration resolved = RequireSuccess(
            FoundationConfigurationLoader.Resolve(configuration, temporary.Path, string.Empty));
        FoundationConfigurationStatus status = resolved.ToPublicStatus();
        string statusJson = JsonSerializer.Serialize(status, SerializerOptions);

        Assert.Equal(OperationMode.Remote, resolved.Mode);
        Assert.Equal(Path.GetFullPath(configuredPath), resolved.DataRoot);
        Assert.True(status.DataRootConfigured);
        Assert.Equal("not-created", status.DataRootState);
        Assert.DoesNotContain(configuredPath, statusJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(temporary.Path, statusJson, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies standard source precedence from JSON through environment to command line.
    /// </summary>
    [Fact]
    public void Load_SourcesUseCommandLineEnvironmentJsonPrecedence()
    {
        using TemporaryDirectory temporary = new();
        string configurationFile = Path.Combine(temporary.Path, "mtg-mcp.json");
        string jsonDataRoot = Path.Combine(temporary.Path, "json-data");
        string environmentDataRoot = Path.Combine(temporary.Path, "environment-data");
        string commandLineDataRoot = Path.Combine(temporary.Path, "command-line-data");
        File.WriteAllText(
            configurationFile,
            JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["MODE"] = "remote",
                ["DATA_DIR"] = jsonDataRoot,
                ["TOOLSETS"] = "none",
            }));
        FoundationConfiguration jsonResolved;
        using (new EnvironmentVariableScope("MTGMCP__MODE", null))
        using (new EnvironmentVariableScope("MTGMCP__DATA_DIR", null))
        using (new EnvironmentVariableScope("MTGMCP__TOOLSETS", null))
        {
            jsonResolved = RequireSuccess(
                FoundationConfigurationLoader.Load([], configurationFile, temporary.Path, string.Empty));
        }

        FoundationConfiguration environmentResolved;
        FoundationConfiguration commandLineResolved;
        using (new EnvironmentVariableScope("MTGMCP__MODE", "read-only"))
        using (new EnvironmentVariableScope("MTGMCP__DATA_DIR", environmentDataRoot))
        using (new EnvironmentVariableScope("MTGMCP__TOOLSETS", "all"))
        {
            environmentResolved = RequireSuccess(
                FoundationConfigurationLoader.Load([], configurationFile, temporary.Path, string.Empty));
            commandLineResolved = RequireSuccess(
                FoundationConfigurationLoader.Load(
                    ["--mode=local", $"--data-dir={commandLineDataRoot}", "--toolsets=decks", "--scryfall-ttl-hours=6.5"],
                    configurationFile,
                    temporary.Path,
                    string.Empty));
        }

        Assert.Equal(OperationMode.Remote, jsonResolved.Mode);
        Assert.Equal(CapabilityToolsetSelectionKind.None, jsonResolved.Toolsets.Kind);
        Assert.Equal(jsonDataRoot, jsonResolved.DataRoot);
        Assert.Equal(OperationMode.ReadOnly, environmentResolved.Mode);
        Assert.Equal(CapabilityToolsetSelectionKind.All, environmentResolved.Toolsets.Kind);
        Assert.Equal(environmentDataRoot, environmentResolved.DataRoot);
        Assert.Equal(OperationMode.Local, commandLineResolved.Mode);
        Assert.Equal(CapabilityToolsetSelectionKind.Explicit, commandLineResolved.Toolsets.Kind);
        Assert.Equal(commandLineDataRoot, commandLineResolved.DataRoot);
        Assert.Equal(TimeSpan.FromHours(6.5), commandLineResolved.ScryfallFreshnessTtl);
    }

    /// <summary>
    /// Verifies explicit unavailable and invalid states instead of inventing a fallback path.
    /// </summary>
    [Fact]
    public void Resolve_UnavailableOrInvalidDataRoot_ReturnsStructuredFailure()
    {
        IConfiguration emptyConfiguration = new ConfigurationBuilder().Build();
        IConfiguration invalidConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DATA_DIR"] = "invalid\0path",
            })
            .Build();

        OperationResult<FoundationConfiguration> unavailable =
            FoundationConfigurationLoader.Resolve(emptyConfiguration, string.Empty, string.Empty);
        OperationResult<FoundationConfiguration> invalid =
            FoundationConfigurationLoader.Resolve(invalidConfiguration, "unused", string.Empty);

        Assert.Equal(
            "application-data-unavailable",
            Assert.IsType<OperationUnavailable>(unavailable.Value).ReasonCode);
        Assert.Equal(
            "invalid-data-root",
            Assert.IsType<OperationInvalidInput>(invalid.Value).ReasonCode);
    }

    /// <summary>
    /// Verifies an unimplemented or non-lowercase toolset fails before any filesystem work.
    /// </summary>
    [Theory]
    [InlineData("tagger")]
    [InlineData("DECKS")]
    [InlineData("default,decks")]
    public void Resolve_InvalidToolsets_ReturnsSanitizedInvalidInput(string configuredValue)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TOOLSETS"] = configuredValue,
            })
            .Build();

        OperationResult<FoundationConfiguration> result = FoundationConfigurationLoader.Resolve(
            configuration,
            "unused",
            string.Empty);

        OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>(result.Value);
        Assert.Equal("invalid-capability-toolsets", invalid.ReasonCode);
        Assert.DoesNotContain(configuredValue, invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies malformed, nonpositive, and excessive Scryfall TTL values fail before filesystem work.
    /// </summary>
    [Theory]
    [InlineData("invalid")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("8761")]
    public void Resolve_InvalidScryfallTtl_ReturnsStructuredInvalidInput(string configuredValue)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SCRYFALL_TTL_HOURS"] = configuredValue,
            })
            .Build();

        OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>(
            FoundationConfigurationLoader.Resolve(configuration, "unused", string.Empty).Value);

        Assert.Equal("invalid-scryfall-ttl", invalid.ReasonCode);
        Assert.Equal(
            "Scryfall freshness hours must be a positive number no greater than 8760.",
            invalid.Message);
    }

    /// <summary>
    /// Verifies a present directory is reported while a regular file is rejected as a data root.
    /// </summary>
    [Fact]
    public void Resolve_ExistingDataRoot_DistinguishesDirectoryFromFile()
    {
        using TemporaryDirectory temporary = new();
        string directoryPath = Path.Combine(temporary.Path, "directory");
        string filePath = Path.Combine(temporary.Path, "private-file");
        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(filePath, "private");
        IConfiguration directoryConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DATA_DIR"] = directoryPath })
            .Build();
        IConfiguration fileConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DATA_DIR"] = filePath })
            .Build();

        FoundationConfiguration directory = RequireSuccess(
            FoundationConfigurationLoader.Resolve(
                directoryConfiguration,
                temporary.Path,
                string.Empty));
        OperationResult<FoundationConfiguration> file = FoundationConfigurationLoader.Resolve(
            fileConfiguration,
            temporary.Path,
            string.Empty);

        Assert.Equal(DataRootState.DirectoryPresent, directory.DataRootState);
        Assert.Equal("directory-present", directory.ToPublicStatus().DataRootState);
        OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>(file.Value);
        Assert.Equal("invalid-data-root", invalid.ReasonCode);
        Assert.DoesNotContain(filePath, invalid.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies malformed JSON returns a sanitized result without echoing its file path.
    /// </summary>
    [Fact]
    public void Load_MalformedJson_ReturnsSanitizedInvalidConfiguration()
    {
        using TemporaryDirectory temporary = new();
        using EnvironmentVariableScope toolsets = new("MTGMCP__TOOLSETS", null);
        string configurationFile = Path.Combine(temporary.Path, "private-config.json");
        File.WriteAllText(configurationFile, "{ invalid json");

        OperationResult<FoundationConfiguration> result = FoundationConfigurationLoader.Load(
            [],
            configurationFile,
            temporary.Path,
            string.Empty);
        OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>(result.Value);

        Assert.Equal("invalid-configuration", invalid.ReasonCode);
        Assert.DoesNotContain(configurationFile, invalid.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("invalid json", invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies unknown and incomplete command-line options share a sanitized structured failure.
    /// </summary>
    [Fact]
    public void Load_InvalidCommandLine_ReturnsSanitizedInvalidInput()
    {
        using TemporaryDirectory temporary = new();
        using EnvironmentVariableScope toolsets = new("MTGMCP__TOOLSETS", null);
        string configurationFile = Path.Combine(temporary.Path, "missing-config.json");
        IReadOnlyList<string>[] invalidArguments =
        [
            ["--unknown", "private-value"],
            ["--mode"],
            ["--mode", "local", "--mode", "remote"],
            ["--mode=local", "--mode", "remote"],
            ["--toolsets"],
            ["--toolsets", "decks", "--toolsets=none"],
        ];

        foreach (IReadOnlyList<string> arguments in invalidArguments)
        {
            OperationResult<FoundationConfiguration> result = FoundationConfigurationLoader.Load(
                arguments,
                configurationFile,
                temporary.Path,
                string.Empty);
            OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>(result.Value);

            Assert.Equal("invalid-command-line", invalid.ReasonCode);
            Assert.DoesNotContain("unknown", invalid.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("private-value", invalid.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Verifies every valid result case is forwarded explicitly and an unexpected success fails closed.
    /// </summary>
    [Fact]
    public void ForwardFailure_AllUnionCasesRemainStructured()
    {
        OperationResult<int>[] failures =
        [
            new OperationNotFound("not-found", "Not found."),
            new OperationNotCached("not-cached", "Not cached."),
            new OperationUnsupported("unsupported", "Unsupported."),
            new OperationUnavailable("unavailable", "Unavailable."),
            new OperationConflict("conflict", "Conflict."),
            new OperationInvalidInput("invalid-input", "Invalid input."),
        ];

        foreach (OperationResult<int> failure in failures)
        {
            OperationResult<FoundationConfiguration> forwarded =
                FoundationConfigurationLoader.ForwardFailure(failure);

            Assert.Same(failure.Value, forwarded.Value);
        }

        OperationResult<FoundationConfiguration> unexpectedSuccess =
            FoundationConfigurationLoader.ForwardFailure<int>(new OperationSuccess<int>(42));
        OperationUnavailable unavailable =
            Assert.IsType<OperationUnavailable>(unexpectedSuccess.Value);
        Assert.Equal("configuration-resolution-failed", unavailable.ReasonCode);
    }

    /// <summary>
    /// Verifies legacy data remains byte-identical and no v0.9 directory is created during inspection.
    /// </summary>
    [Fact]
    public void Resolve_LegacyData_ReportsCleanBreakWithoutLoadingOrChangingIt()
    {
        using TemporaryDirectory temporary = new();
        string legacyDirectory = Path.Combine(temporary.Path, "mtg-mcp", "workspaces");
        string legacyFile = Path.Combine(legacyDirectory, "deck.json");
        Directory.CreateDirectory(legacyDirectory);
        byte[] expectedBytes = [0x7b, 0x22, 0x69, 0x64, 0x22, 0x3a, 0x31, 0x7d];
        File.WriteAllBytes(legacyFile, expectedBytes);
        using EnvironmentVariableScope mode = new("MTGMCP__MODE", null);
        using EnvironmentVariableScope dataRoot = new("MTGMCP__DATA_DIR", null);
        using EnvironmentVariableScope toolsets = new("MTGMCP__TOOLSETS", null);
        string configurationFile = Path.Combine(temporary.Path, "missing-config.json");

        FoundationConfiguration resolved = RequireSuccess(
            FoundationConfigurationLoader.Load(
                [],
                configurationFile,
                temporary.Path,
                string.Empty));
        FoundationConfigurationStatus status = resolved.ToPublicStatus();
        string serializedStatus = JsonSerializer.Serialize(status, SerializerOptions);

        Assert.Equal(LegacyDataState.Detected, resolved.LegacyData.State);
        Assert.Equal(expectedBytes, File.ReadAllBytes(legacyFile));
        Assert.False(Directory.Exists(Path.Combine(temporary.Path, "mtg-mcp", "v0.9")));
        Assert.Contains("will not be loaded, migrated, or modified", status.MigrationBoundary);
        Assert.DoesNotContain(temporary.Path, serializedStatus, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("deck.json", serializedStatus, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies an absent legacy root, an empty application-data value, and a v0.9-only root stay distinct.
    /// </summary>
    [Fact]
    public void LegacyInspection_BoundaryStatesRemainExplicit()
    {
        using TemporaryDirectory temporary = new();
        LegacyDataBoundary absent = LegacyDataInspector.Inspect(temporary.Path);
        LegacyDataBoundary unavailable = LegacyDataInspector.Inspect(string.Empty);
        Directory.CreateDirectory(Path.Combine(temporary.Path, "mtg-mcp", "v0.9"));
        LegacyDataBoundary versionedOnly = LegacyDataInspector.Inspect(temporary.Path);

        Assert.Equal(LegacyDataState.NotDetected, absent.State);
        Assert.Equal(LegacyDataState.InspectionUnavailable, unavailable.State);
        Assert.Equal(LegacyDataState.NotDetected, versionedOnly.State);
    }

    /// <summary>
    /// Verifies an undefined inspection state is projected conservatively without exposing private data.
    /// </summary>
    [Fact]
    public void PublicStatus_UndefinedLegacyState_IsInspectionUnavailable()
    {
        FoundationConfiguration configuration = new(
            OperationMode.Local,
            Assert.IsType<OperationSuccess<CapabilityToolsetSelection>>(
                CapabilityToolsetRegistry.Resolve(null).Value).Data,
            TimeSpan.FromHours(24),
            "private-path",
            DataRootState.NotCreated,
            false,
            new LegacyDataBoundary((LegacyDataState)999, "Migration remains disabled."),
            ArchidektOptions.CreateDefault());

        FoundationConfigurationStatus status = configuration.ToPublicStatus();

        Assert.Equal("inspection-unavailable", status.LegacyDataState);
        Assert.DoesNotContain("private-path", JsonSerializer.Serialize(status, SerializerOptions));
    }

    /// <summary>
    /// Extracts successful configuration data while preserving a useful test failure for other cases.
    /// </summary>
    private static FoundationConfiguration RequireSuccess(
        OperationResult<FoundationConfiguration> result)
    {
        return Assert.IsType<OperationSuccess<FoundationConfiguration>>(result.Value).Data;
    }
}
