using Microsoft.Extensions.Configuration;
using MtgMcp.App.Capabilities;
using MtgMcp.App.Configuration;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies that every toolset is on by default, that the Archidekt and Playgroup toolsets turn on only when their
/// credentials are present, and that the reason for a toolset that is off is reported.
/// </summary>
public sealed class ToolsetCredentialGateTests
{
    /// <summary>
    /// Verifies the default selection without credentials turns the two provider toolsets off with a reason, and
    /// leaves exactly the tools of the other four toolsets.
    /// </summary>
    [Fact]
    public void Default_WithoutCredentials_TurnsProviderToolsetsOffWithReasonAndCountsTools()
    {
        using TemporaryDirectory profile = new();

        FoundationConfiguration configuration = Resolve(profile, []);

        Assert.Equal(OperationMode.Remote, configuration.Mode);
        Assert.Equal(
            [CapabilityToolset.Decks, CapabilityToolset.Scryfall, CapabilityToolset.Stats, CapabilityToolset.Spellbook],
            configuration.Toolsets.EnabledToolsets);
        Assert.Equal(
            DisabledToolset.CredentialsMissing,
            configuration.Toolsets.FindDisabledReason(CapabilityToolset.Archidekt));
        Assert.Equal(
            DisabledToolset.CredentialsMissing,
            configuration.Toolsets.FindDisabledReason(CapabilityToolset.Playgroup));
        Assert.Null(configuration.Toolsets.FindDisabledReason(CapabilityToolset.Decks));
        Assert.Equal(30, Count(configuration, OperationMode.ReadOnly));
        Assert.Equal(43, Count(configuration, OperationMode.Local));
        Assert.Equal(43, Count(configuration, OperationMode.Remote));
    }

    /// <summary>
    /// Verifies the counts for Archidekt credentials alone and for both credentials, and that all means the same as
    /// the default.
    /// </summary>
    [Fact]
    public void Default_WithCredentials_CountsToolsPerMode()
    {
        using TemporaryDirectory profile = new();
        Dictionary<string, string?> archidekt = new()
        {
            ["ARCHIDEKT:USERNAME"] = "user",
            ["ARCHIDEKT:PASSWORD"] = "secret",
        };
        Dictionary<string, string?> both = new(archidekt) { ["PLAYGROUP:API_KEY"] = "key" };

        FoundationConfiguration archidektOnly = Resolve(profile, archidekt);
        FoundationConfiguration everything = Resolve(profile, both);
        FoundationConfiguration all = Resolve(profile, new Dictionary<string, string?>(both) { ["TOOLSETS"] = "all" });

        Assert.Equal((39, 53, 64), Counts(archidektOnly));
        Assert.Equal((53, 67, 80), Counts(everything));
        Assert.Equal((53, 67, 80), Counts(all));
        Assert.Empty(everything.Toolsets.DisabledToolsets);
        Assert.Equal(
            DisabledToolset.CredentialsMissing,
            archidektOnly.Toolsets.FindDisabledReason(CapabilityToolset.Playgroup));
    }

    /// <summary>
    /// Verifies only a username, or only a password, does not count as Archidekt credentials.
    /// </summary>
    [Theory]
    [InlineData("ARCHIDEKT:USERNAME")]
    [InlineData("ARCHIDEKT:PASSWORD")]
    public void Archidekt_OnlyUsernameOrOnlyPassword_StaysOff(string key)
    {
        using TemporaryDirectory profile = new();

        FoundationConfiguration configuration = Resolve(profile, new Dictionary<string, string?> { [key] = "value" });

        Assert.False(configuration.Toolsets.Includes(CapabilityToolset.Archidekt));
        Assert.Equal(
            DisabledToolset.CredentialsMissing,
            configuration.Toolsets.FindDisabledReason(CapabilityToolset.Archidekt));
    }

    /// <summary>
    /// Verifies a named credentials file that exists turns Archidekt on, even if its contents are not read, and a
    /// named file that does not exist leaves it off and does not fall back to the default file.
    /// </summary>
    [Fact]
    public void Archidekt_CredentialsFile_CountsOnlyWhenItExists()
    {
        using TemporaryDirectory profile = new();
        string existing = Path.Combine(profile.Path, "archidekt-override.json");
        File.WriteAllText(existing, "{}");
        WriteDefaultFile(profile, "archidekt.json");

        FoundationConfiguration present = Resolve(
            profile,
            new Dictionary<string, string?> { ["ARCHIDEKT:CREDENTIALS_FILE"] = existing });
        FoundationConfiguration missing = Resolve(
            profile,
            new Dictionary<string, string?>
            {
                ["ARCHIDEKT:CREDENTIALS_FILE"] = Path.Combine(profile.Path, "absent.json"),
            });

        Assert.True(present.Toolsets.Includes(CapabilityToolset.Archidekt));
        Assert.False(missing.Toolsets.Includes(CapabilityToolset.Archidekt));
    }

    /// <summary>
    /// Verifies the default credentials file in the user profile turns each provider toolset on.
    /// </summary>
    [Fact]
    public void DefaultCredentialFiles_TurnProviderToolsetsOn()
    {
        using TemporaryDirectory profile = new();
        WriteDefaultFile(profile, "archidekt.json");
        WriteDefaultFile(profile, "playgroup.json");

        FoundationConfiguration configuration = Resolve(profile, []);

        Assert.True(configuration.Toolsets.Includes(CapabilityToolset.Archidekt));
        Assert.True(configuration.Toolsets.Includes(CapabilityToolset.Playgroup));
    }

    /// <summary>
    /// Verifies a Playgroup API key or an existing credentials file turns Playgroup on.
    /// </summary>
    [Fact]
    public void Playgroup_KeyOrExistingFile_TurnsOn()
    {
        using TemporaryDirectory profile = new();
        string file = Path.Combine(profile.Path, "playgroup-override.json");
        File.WriteAllText(file, "{}");

        FoundationConfiguration withKey = Resolve(
            profile,
            new Dictionary<string, string?> { ["PLAYGROUP:API_KEY"] = "key" });
        FoundationConfiguration withFile = Resolve(
            profile,
            new Dictionary<string, string?> { ["PLAYGROUP:CREDENTIALS_FILE"] = file });

        Assert.True(withKey.Toolsets.Includes(CapabilityToolset.Playgroup));
        Assert.True(withFile.Toolsets.Includes(CapabilityToolset.Playgroup));
    }

    /// <summary>
    /// Verifies naming a provider toolset without credentials still leaves it off, with a reason, and keeps the
    /// selection explicit.
    /// </summary>
    [Fact]
    public void ExplicitSelection_NamingProviderWithoutCredentials_LeavesItOff()
    {
        using TemporaryDirectory profile = new();

        FoundationConfiguration configuration = Resolve(
            profile,
            new Dictionary<string, string?> { ["TOOLSETS"] = "archidekt,decks" });

        Assert.Equal(CapabilityToolsetSelectionKind.Explicit, configuration.Toolsets.Kind);
        Assert.Equal([CapabilityToolset.Decks], configuration.Toolsets.EnabledToolsets);
        Assert.Equal(
            DisabledToolset.CredentialsMissing,
            configuration.Toolsets.FindDisabledReason(CapabilityToolset.Archidekt));
        Assert.Equal(19, Count(configuration, OperationMode.Remote));
    }

    /// <summary>
    /// Verifies an invalid Playgroup configuration is only a reason under the default selection, and a startup
    /// failure when Playgroup is named.
    /// </summary>
    [Fact]
    public void InvalidPlaygroupConfiguration_FailsStartupOnlyWhenPlaygroupIsNamed()
    {
        using TemporaryDirectory profile = new();
        Dictionary<string, string?> invalid = new() { ["PLAYGROUP:CREDENTIALS_FILE"] = "bad\0path" };

        FoundationConfiguration defaulted = Resolve(profile, invalid);
        FoundationConfiguration notNamed = Resolve(
            profile,
            new Dictionary<string, string?>(invalid) { ["TOOLSETS"] = "decks" });
        OperationResult<FoundationConfiguration> named = FoundationConfigurationLoader.Resolve(
            Build(new Dictionary<string, string?>(invalid) { ["TOOLSETS"] = "playgroup" }),
            profile.Path,
            string.Empty,
            profile.Path);

        Assert.False(defaulted.Toolsets.Includes(CapabilityToolset.Playgroup));
        Assert.Equal(
            DisabledToolset.CredentialsInvalid,
            defaulted.Toolsets.FindDisabledReason(CapabilityToolset.Playgroup));
        Assert.Null(notNamed.Toolsets.FindDisabledReason(CapabilityToolset.Playgroup));
        Assert.Equal(
            "invalid-playgroup-configuration",
            Assert.IsType<OperationInvalidInput>(named.Value).ReasonCode);
    }

    /// <summary>
    /// Verifies an invalid Playgroup configuration under the default selection and under <c>all</c> is a reason,
    /// not a startup failure.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("default")]
    [InlineData("all")]
    public void InvalidPlaygroupConfiguration_UnderDefaultOrAll_IsOnlyAReason(string? toolsets)
    {
        using TemporaryDirectory profile = new();
        Dictionary<string, string?> settings = new() { ["PLAYGROUP:CREDENTIALS_FILE"] = "bad\0path" };
        if (toolsets is not null)
        {
            settings["TOOLSETS"] = toolsets;
        }

        FoundationConfiguration configuration = Resolve(profile, settings);

        Assert.False(configuration.Toolsets.Includes(CapabilityToolset.Playgroup));
        Assert.Equal(
            DisabledToolset.CredentialsInvalid,
            configuration.Toolsets.FindDisabledReason(CapabilityToolset.Playgroup));
    }

    /// <summary>
    /// Verifies the capabilities resource reports <c>credentials-invalid</c> and <c>credentials-missing</c> as
    /// <c>disabledReason</c>, and null for a toolset that is on.
    /// </summary>
    [Fact]
    public async Task CapabilitiesResource_ReportsTheDisabledReason()
    {
        using TemporaryDirectory profile = new();
        FoundationConfiguration configuration = Resolve(
            profile,
            new Dictionary<string, string?> { ["PLAYGROUP:CREDENTIALS_FILE"] = "bad\0path" });
        await using ModelContextProtocol.Server.McpServer server = ModelContextProtocol.Server.McpServer.Create(
            new ModelContextProtocol.Server.StreamServerTransport(Stream.Null, Stream.Null),
            new ModelContextProtocol.Server.McpServerOptions());

        string json = new FoundationResources(configuration).GetCapabilities(server);

        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(json);
        Dictionary<string, string?> reasons = document.RootElement.GetProperty("toolsets").GetProperty("items")
            .EnumerateArray()
            .ToDictionary(
                item => item.GetProperty("name").GetString()!,
                item => item.GetProperty("disabledReason").GetString());
        Assert.Equal("credentials-missing", reasons["archidekt"]);
        Assert.Equal("credentials-invalid", reasons["playgroup"]);
        Assert.Null(reasons["decks"]);
        Assert.Equal(8, document.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    /// <summary>
    /// Verifies the capabilities resource projects the credential state and the reason without a secret value.
    /// </summary>
    [Fact]
    public void CredentialProjection_ReportsConfigurationInvalid()
    {
        using TemporaryDirectory profile = new();
        FoundationConfiguration invalid = Resolve(
            profile,
            new Dictionary<string, string?> { ["PLAYGROUP:CREDENTIALS_FILE"] = "bad\0path" });
        FoundationConfiguration missing = Resolve(profile, []);

        Assert.Equal(
            ("configuration-invalid", "playgroup_auth_status"),
            FoundationResources.GetCredentialProjection(invalid, CapabilityToolset.Playgroup));
        Assert.Equal(
            ("not-configured", "archidekt_auth_status"),
            FoundationResources.GetCredentialProjection(missing, CapabilityToolset.Archidekt));
    }

    /// <summary>
    /// Verifies the startup notice names the toolset, the reason, and the settings to add, and holds no value.
    /// </summary>
    [Fact]
    public void Notice_NamesToolsetReasonAndSettings()
    {
        DisabledToolset archidekt = new(CapabilityToolset.Archidekt, DisabledToolset.CredentialsMissing);
        DisabledToolset playgroup = new(CapabilityToolset.Playgroup, DisabledToolset.CredentialsInvalid);

        string archidektNotice = archidekt.DescribeNotice();
        string playgroupNotice = playgroup.DescribeNotice();

        Assert.Contains("'archidekt'", archidektNotice, StringComparison.Ordinal);
        Assert.Contains("credentials-missing", archidektNotice, StringComparison.Ordinal);
        Assert.Contains("ARCHIDEKT:USERNAME", archidektNotice, StringComparison.Ordinal);
        Assert.Contains("MTGMCP__", archidektNotice, StringComparison.Ordinal);
        Assert.Contains("'playgroup'", playgroupNotice, StringComparison.Ordinal);
        Assert.Contains("credentials-invalid", playgroupNotice, StringComparison.Ordinal);
        Assert.Contains("PLAYGROUP:API_KEY", playgroupNotice, StringComparison.Ordinal);
        Assert.Equal(
            "No credential setting applies.",
            new DisabledToolset(CapabilityToolset.Decks, "x").DescribeFix());
    }

    /// <summary>
    /// Verifies the gated toolsets are removed and recorded without changing the order of the others.
    /// </summary>
    [Fact]
    public void WithDisabledToolsets_RemovesGatedToolsetsAndKeepsTheOthersInOrder()
    {
        CapabilityToolsetSelection selection = new(
            CapabilityToolsetSelectionKind.Default,
            [CapabilityToolset.Decks, CapabilityToolset.Archidekt, CapabilityToolset.Stats]);

        CapabilityToolsetSelection gated = selection.WithDisabledToolsets(
            [new DisabledToolset(CapabilityToolset.Archidekt, DisabledToolset.CredentialsMissing)]);

        Assert.Equal([CapabilityToolset.Decks, CapabilityToolset.Stats], gated.EnabledToolsets);
        Assert.Equal(CapabilityToolsetSelectionKind.Default, gated.Kind);
        Assert.Single(gated.DisabledToolsets);
        Assert.Throws<ArgumentNullException>(() => selection.WithDisabledToolsets(null!));
    }

    /// <summary>
    /// Resolves configuration from in-memory settings and an isolated user profile.
    /// </summary>
    private static FoundationConfiguration Resolve(TemporaryDirectory profile, Dictionary<string, string?> settings)
    {
        OperationResult<FoundationConfiguration> result = FoundationConfigurationLoader.Resolve(
            Build(settings),
            profile.Path,
            string.Empty,
            profile.Path);
        return Assert.IsType<OperationSuccess<FoundationConfiguration>>(result.Value).Data;
    }

    /// <summary>
    /// Builds a configuration root from in-memory settings.
    /// </summary>
    private static IConfiguration Build(Dictionary<string, string?> settings)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    /// <summary>
    /// Writes an empty credentials file where the server looks for the default one under the user profile.
    /// </summary>
    private static void WriteDefaultFile(TemporaryDirectory profile, string fileName)
    {
        string directory = Path.Combine(profile.Path, ".mtg-mcp");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), "{}");
    }

    /// <summary>
    /// Counts the tools visible for the configuration's own selection in one mode.
    /// </summary>
    private static int Count(FoundationConfiguration configuration, OperationMode mode)
    {
        return CapabilityToolsetRegistry.CountVisibleTools(configuration.Toolsets, mode);
    }

    /// <summary>
    /// Counts the tools visible in read-only, local, and remote mode.
    /// </summary>
    private static (int ReadOnly, int Local, int Remote) Counts(FoundationConfiguration configuration)
    {
        return (
            Count(configuration, OperationMode.ReadOnly),
            Count(configuration, OperationMode.Local),
            Count(configuration, OperationMode.Remote));
    }
}
