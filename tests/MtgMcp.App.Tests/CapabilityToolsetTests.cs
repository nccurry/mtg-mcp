using MtgMcp.App.Capabilities;
using MtgMcp.App.Configuration;
using MtgMcp.App.Hosting;
using MtgMcp.Archidekt;
using MtgMcp.Core.Results;
using MtgMcp.Playgroup;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies static toolset policy, descriptor ownership, and canonical selection.
/// </summary>
public sealed class CapabilityToolsetTests
{
    /// <summary>
    /// Verifies the stable vocabulary and ordinary-profile policy have no hidden capability names.
    /// </summary>
    [Fact]
    public void Policy_ContainsOnlyApprovedStableNamesAndDefaults()
    {
        CapabilityToolset[] toolsets = Enum.GetValues<CapabilityToolset>();

        Assert.Equal(
            ["decks", "scryfall", "stats", "archidekt", "playgroup"],
            toolsets.Select(CapabilityToolsetPolicy.Format));
        Assert.Equal(
            [true, true, true, false, false],
            toolsets.Select(CapabilityToolsetPolicy.IsDefaultEnabled));
        Assert.Equal("stable", CapabilityToolsetPolicy.Format(CapabilityToolsetStability.Stable));
        Assert.Equal("experimental", CapabilityToolsetPolicy.Format(CapabilityToolsetStability.Experimental));
        Assert.Equal("default", CapabilityToolsetPolicy.Format(CapabilityToolsetSelectionKind.Default));
        Assert.Equal("all", CapabilityToolsetPolicy.Format(CapabilityToolsetSelectionKind.All));
        Assert.Equal("none", CapabilityToolsetPolicy.Format(CapabilityToolsetSelectionKind.None));
        Assert.Equal("explicit", CapabilityToolsetPolicy.Format(CapabilityToolsetSelectionKind.Explicit));
    }

    /// <summary>
    /// Verifies undefined closed-category values fail or remain disabled rather than becoming aliases.
    /// </summary>
    [Fact]
    public void Policy_UndefinedValuesFailClosed()
    {
        CapabilityToolset undefinedToolset = (CapabilityToolset)999;

        Assert.False(CapabilityToolsetPolicy.IsDefaultEnabled(undefinedToolset));
        Assert.Throws<ArgumentOutOfRangeException>(() => CapabilityToolsetPolicy.Format(undefinedToolset));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CapabilityToolsetPolicy.Format((CapabilityToolsetSelectionKind)999));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CapabilityToolsetPolicy.Format((CapabilityToolsetStability)999));
    }

    /// <summary>
    /// Verifies descriptors defensively copy, sort, and expose exact per-mode assignments.
    /// </summary>
    [Fact]
    public void Descriptor_ProvidesImmutableCanonicalModeSurfaces()
    {
        string[] reads = ["z_read", "a_read"];
        string[] localWrites = ["local_write"];
        string[] remoteWrites = ["remote_write"];
        CapabilityToolsetDescriptor descriptor = new(
            CapabilityToolset.Decks,
            CapabilityToolsetStability.Stable,
            " Test capability. ",
            reads,
            localWrites,
            remoteWrites);
        reads[0] = "changed";

        Assert.Equal("decks", descriptor.Name);
        Assert.True(descriptor.DefaultEnabled);
        Assert.Equal("Test capability.", descriptor.Description);
        Assert.Equal(["a_read", "z_read"], descriptor.GetVisibleToolNames(OperationMode.ReadOnly));
        Assert.Equal(
            ["a_read", "local_write", "z_read"],
            descriptor.GetVisibleToolNames(OperationMode.Local));
        Assert.Equal(
            ["a_read", "local_write", "remote_write", "z_read"],
            descriptor.GetVisibleToolNames(OperationMode.Remote));
        Assert.Equal(descriptor.AllToolNames, descriptor.GetVisibleToolNames(OperationMode.Remote));
        Assert.Empty(descriptor.GetVisibleToolNames((OperationMode)999));
    }

    /// <summary>
    /// Verifies invalid descriptor definitions cannot create ambiguous tool ownership.
    /// </summary>
    [Fact]
    public void Descriptor_RejectsBlankDuplicateAndOverlappingDefinitions()
    {
        Assert.Throws<ArgumentException>(() => CreateDescriptor(" ", [], [], []));
        Assert.Throws<ArgumentException>(() => CreateDescriptor("description", [""], [], []));
        Assert.Throws<ArgumentException>(() => CreateDescriptor("description", ["same", "same"], [], []));
        Assert.Throws<ArgumentException>(() => CreateDescriptor("description", ["same"], ["same"], []));
        Assert.Throws<ArgumentException>(() => CreateDescriptor("description", [" padded"], [], []));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CapabilityToolsetDescriptor(
                (CapabilityToolset)999,
                CapabilityToolsetStability.Stable,
                "description",
                [],
                [],
                []));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CapabilityToolsetDescriptor(
                CapabilityToolset.Decks,
                (CapabilityToolsetStability)999,
                "description",
                [],
                [],
                []));
        Assert.Throws<ArgumentNullException>(
            () => new CapabilityToolsetDescriptor(
                CapabilityToolset.Decks,
                CapabilityToolsetStability.Stable,
                "description",
                null!,
                [],
                []));
    }

    /// <summary>
    /// Verifies reserved and explicit selections resolve against only implemented descriptors.
    /// </summary>
    [Theory]
    [InlineData(null, "default", true, true, true, false, false)]
    [InlineData("default", "default", true, true, true, false, false)]
    [InlineData("all", "all", true, true, true, true, true)]
    [InlineData("none", "none", false, false, false, false, false)]
    [InlineData("decks", "explicit", true, false, false, false, false)]
    [InlineData("scryfall", "explicit", false, true, false, false, false)]
    [InlineData("stats", "explicit", false, false, true, false, false)]
    [InlineData("archidekt", "explicit", false, false, false, true, false)]
    [InlineData("playgroup", "explicit", false, false, false, false, true)]
    public void Parser_ResolvesCurrentProfiles(
        string? value,
        string expectedSelection,
        bool decksEnabled,
        bool scryfallEnabled,
        bool statsEnabled,
        bool archidektEnabled,
        bool playgroupEnabled)
    {
        CapabilityToolsetSelection selection = RequireSuccess(
            CapabilityToolsetSelectionParser.Parse(value, CapabilityToolsetRegistry.Implemented));

        Assert.Equal(expectedSelection, selection.Label);
        Assert.Equal(decksEnabled, selection.Includes(CapabilityToolset.Decks));
        Assert.Equal(scryfallEnabled, selection.Includes(CapabilityToolset.Scryfall));
        Assert.Equal(statsEnabled, selection.Includes(CapabilityToolset.Stats));
        Assert.Equal(archidektEnabled, selection.Includes(CapabilityToolset.Archidekt));
        Assert.Equal(playgroupEnabled, selection.Includes(CapabilityToolset.Playgroup));
        List<CapabilityToolset> expected = [];
        if (decksEnabled)
        {
            expected.Add(CapabilityToolset.Decks);
        }

        if (scryfallEnabled)
        {
            expected.Add(CapabilityToolset.Scryfall);
        }

        if (statsEnabled)
        {
            expected.Add(CapabilityToolset.Stats);
        }

        if (archidektEnabled)
        {
            expected.Add(CapabilityToolset.Archidekt);
        }

        if (playgroupEnabled)
        {
            expected.Add(CapabilityToolset.Playgroup);
        }
        Assert.Equal(expected, selection.EnabledToolsets);
    }

    /// <summary>
    /// Verifies invalid, unimplemented, duplicate, uppercase, blank, and mixed-reserved inputs fail uniformly.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("DECKS")]
    [InlineData("tagger")]
    [InlineData("decks,decks")]
    [InlineData("decks,")]
    [InlineData(",decks")]
    [InlineData("default,decks")]
    [InlineData("all,decks")]
    [InlineData("none,decks")]
    public void Parser_RejectsEveryInvalidSelectionWithoutEchoingIt(string value)
    {
        OperationResult<CapabilityToolsetSelection> result = CapabilityToolsetSelectionParser.Parse(
            value,
            CapabilityToolsetRegistry.Implemented);

        OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>(result.Value);
        Assert.Equal("invalid-capability-toolsets", invalid.ReasonCode);
        if (!string.IsNullOrWhiteSpace(value))
        {
            Assert.DoesNotContain(value, invalid.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Verifies explicit lists canonicalize to registry order independently of caller order.
    /// </summary>
    [Fact]
    public void Parser_ExplicitSelectionUsesDescriptorOrder()
    {
        CapabilityToolsetDescriptor decks = CreateDescriptor("decks", CapabilityToolset.Decks);
        CapabilityToolsetDescriptor scryfall = CreateDescriptor("scryfall", CapabilityToolset.Scryfall);
        CapabilityToolsetDescriptor stats = CreateDescriptor("stats", CapabilityToolset.Stats);
        CapabilityToolsetDescriptor[] descriptors = [decks, scryfall, stats];

        CapabilityToolsetSelection first = RequireSuccess(
            CapabilityToolsetSelectionParser.Parse("stats,decks", descriptors));
        CapabilityToolsetSelection second = RequireSuccess(
            CapabilityToolsetSelectionParser.Parse("decks,stats", descriptors));

        Assert.Equal(
            [CapabilityToolset.Decks, CapabilityToolset.Stats],
            first.EnabledToolsets);
        Assert.Equal(first.EnabledToolsets.ToArray(), second.EnabledToolsets.ToArray());
    }

    /// <summary>
    /// Verifies a resolved selection cannot be changed through its caller-owned source collection.
    /// </summary>
    [Fact]
    public void Selection_DefensivelyCopiesEnabledToolsets()
    {
        CapabilityToolset[] enabled = [CapabilityToolset.Decks];
        CapabilityToolsetSelection selection = new(
            CapabilityToolsetSelectionKind.Explicit,
            enabled);
        enabled[0] = CapabilityToolset.Scryfall;

        Assert.Equal([CapabilityToolset.Decks], selection.EnabledToolsets);
        Assert.True(selection.Includes(CapabilityToolset.Decks));
        Assert.False(selection.Includes(CapabilityToolset.Scryfall));
    }

    /// <summary>
    /// Verifies selections reject undefined and duplicate closed-category values.
    /// </summary>
    [Fact]
    public void Selection_RejectsInvalidClosedState()
    {
        Assert.Throws<ArgumentNullException>(
            () => new CapabilityToolsetSelection(
                CapabilityToolsetSelectionKind.Explicit,
                null!));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CapabilityToolsetSelection((CapabilityToolsetSelectionKind)999, []));
        Assert.Throws<ArgumentException>(
            () => new CapabilityToolsetSelection(
                CapabilityToolsetSelectionKind.Explicit,
                [CapabilityToolset.Decks, CapabilityToolset.Decks]));
        Assert.Throws<ArgumentException>(
            () => new CapabilityToolsetSelection(
                CapabilityToolsetSelectionKind.Explicit,
                [(CapabilityToolset)999]));
    }

    /// <summary>
    /// Verifies experimental descriptors require explicit selection and never enter reserved profiles.
    /// </summary>
    [Fact]
    public void Parser_ReservedProfilesExcludeExperimentalDescriptors()
    {
        CapabilityToolsetDescriptor decks = CreateDescriptor("decks", CapabilityToolset.Decks);
        CapabilityToolsetDescriptor playgroupExperiment = CreateDescriptor(
            "playgroup",
            CapabilityToolset.Playgroup,
            CapabilityToolsetStability.Experimental);
        CapabilityToolsetDescriptor[] descriptors = [decks, playgroupExperiment];

        CapabilityToolsetSelection defaultSelection = RequireSuccess(
            CapabilityToolsetSelectionParser.Parse("default", descriptors));
        CapabilityToolsetSelection allSelection = RequireSuccess(
            CapabilityToolsetSelectionParser.Parse("all", descriptors));
        CapabilityToolsetSelection explicitSelection = RequireSuccess(
            CapabilityToolsetSelectionParser.Parse("playgroup", descriptors));

        Assert.Equal([CapabilityToolset.Decks], defaultSelection.EnabledToolsets);
        Assert.Equal([CapabilityToolset.Decks], allSelection.EnabledToolsets);
        Assert.Equal([CapabilityToolset.Playgroup], explicitSelection.EnabledToolsets);
    }

    /// <summary>
    /// Verifies the implemented registry owns every exact stable surface.
    /// </summary>
    [Fact]
    public void Registry_AssignsCurrentSurfaceToImplementedToolsets()
    {
        Assert.Equal(5, CapabilityToolsetRegistry.Implemented.Length);
        CapabilityToolsetDescriptor decks = CapabilityToolsetRegistry.Implemented[0];
        CapabilityToolsetDescriptor scryfall = CapabilityToolsetRegistry.Implemented[1];
        CapabilityToolsetDescriptor stats = CapabilityToolsetRegistry.Implemented[2];
        CapabilityToolsetDescriptor archidekt = CapabilityToolsetRegistry.Implemented[3];
        CapabilityToolsetDescriptor playgroup = CapabilityToolsetRegistry.Implemented[4];
        CapabilityToolsetSelection defaultSelection = RequireSuccess(CapabilityToolsetRegistry.Resolve(null));
        CapabilityToolsetSelection noneSelection = RequireSuccess(CapabilityToolsetRegistry.Resolve("none"));

        Assert.Equal(CapabilityToolset.Decks, decks.Toolset);
        Assert.Equal(25, decks.AllToolNames.Length);
        Assert.Equal(CapabilityToolset.Scryfall, scryfall.Toolset);
        Assert.Equal(18, scryfall.AllToolNames.Length);
        Assert.Equal(14, scryfall.GetVisibleToolCount(OperationMode.ReadOnly));
        Assert.Equal(CapabilityToolset.Stats, stats.Toolset);
        Assert.True(stats.DefaultEnabled);
        Assert.Equal(8, stats.GetVisibleToolCount(OperationMode.ReadOnly));
        Assert.Equal(8, stats.GetVisibleToolCount(OperationMode.Local));
        Assert.Equal(8, stats.GetVisibleToolCount(OperationMode.Remote));
        Assert.Equal(CapabilityToolset.Archidekt, archidekt.Toolset);
        Assert.False(archidekt.DefaultEnabled);
        Assert.Equal(11, archidekt.GetVisibleToolCount(OperationMode.ReadOnly));
        Assert.Equal(12, archidekt.GetVisibleToolCount(OperationMode.Local));
        Assert.Equal(23, archidekt.GetVisibleToolCount(OperationMode.Remote));
        Assert.Equal(CapabilityToolset.Playgroup, playgroup.Toolset);
        Assert.False(playgroup.DefaultEnabled);
        Assert.Equal(14, playgroup.GetVisibleToolCount(OperationMode.ReadOnly));
        Assert.Equal(14, playgroup.GetVisibleToolCount(OperationMode.Local));
        Assert.Equal(16, playgroup.GetVisibleToolCount(OperationMode.Remote));
        Assert.Equal(30, CapabilityToolsetRegistry.CountVisibleTools(defaultSelection, OperationMode.ReadOnly));
        Assert.Equal(51, CapabilityToolsetRegistry.CountVisibleTools(defaultSelection, OperationMode.Local));
        Assert.Equal(51, CapabilityToolsetRegistry.CountVisibleTools(defaultSelection, OperationMode.Remote));
        Assert.Equal(0, CapabilityToolsetRegistry.CountVisibleTools(noneSelection, OperationMode.Local));
        CapabilityToolsetSelection allSelection = RequireSuccess(CapabilityToolsetRegistry.Resolve("all"));
        Assert.Equal(55, CapabilityToolsetRegistry.CountVisibleTools(allSelection, OperationMode.ReadOnly));
        Assert.Equal(77, CapabilityToolsetRegistry.CountVisibleTools(allSelection, OperationMode.Local));
        Assert.Equal(90, CapabilityToolsetRegistry.CountVisibleTools(allSelection, OperationMode.Remote));
    }

    /// <summary>
    /// Verifies capability credential metadata reports configuration presence without authenticating.
    /// </summary>
    [Fact]
    public void CredentialProjection_DistinguishesPresenceWithoutProviderAccess()
    {
        FoundationConfiguration absent = CreateConfiguration(
            ArchidektOptions.CreateDefault(),
            PlaygroupOptions.CreateDefault(null));
        FoundationConfiguration configured = CreateConfiguration(
            ArchidektOptions.CreateDefault("private-user", "private-password"),
            PlaygroupOptions.CreateDefault("private-key"));

        Assert.Equal(
            ("not-required", null),
            FoundationResources.GetCredentialProjection(absent, CapabilityToolset.Decks));
        Assert.Equal(
            ("not-required", null),
            FoundationResources.GetCredentialProjection(configured, CapabilityToolset.Scryfall));
        Assert.Equal(
            ("not-required", null),
            FoundationResources.GetCredentialProjection(configured, CapabilityToolset.Stats));
        Assert.Equal(
            ("not-configured", "archidekt_auth_status"),
            FoundationResources.GetCredentialProjection(absent, CapabilityToolset.Archidekt));
        Assert.Equal(
            ("not-configured", "playgroup_auth_status"),
            FoundationResources.GetCredentialProjection(absent, CapabilityToolset.Playgroup));
        Assert.Equal(
            ("configured-unverified", "archidekt_auth_status"),
            FoundationResources.GetCredentialProjection(configured, CapabilityToolset.Archidekt));
        Assert.Equal(
            ("configured-unverified", "playgroup_auth_status"),
            FoundationResources.GetCredentialProjection(configured, CapabilityToolset.Playgroup));
    }

    /// <summary>
    /// Creates one descriptor for validation-failure scenarios.
    /// </summary>
    private static CapabilityToolsetDescriptor CreateDescriptor(
        string description,
        IEnumerable<string> readTools,
        IEnumerable<string> localWriteTools,
        IEnumerable<string> remoteWriteTools)
    {
        return new CapabilityToolsetDescriptor(
            CapabilityToolset.Decks,
            CapabilityToolsetStability.Stable,
            description,
            readTools,
            localWriteTools,
            remoteWriteTools);
    }

    /// <summary>
    /// Creates private runtime configuration for capability-projection tests.
    /// </summary>
    private static FoundationConfiguration CreateConfiguration(
        ArchidektOptions archidekt,
        PlaygroupOptions playgroup)
    {
        return new FoundationConfiguration(
            OperationMode.Local,
            RequireSuccess(CapabilityToolsetRegistry.Resolve("default")),
            TimeSpan.FromHours(24),
            Path.Combine(Path.GetTempPath(), "mtg-mcp-capability-test"),
            DataRootState.NotCreated,
            false,
            new LegacyDataBoundary(
                LegacyDataState.NotDetected,
                "Legacy data was not detected; automatic migration remains disabled."),
            archidekt,
            playgroup);
    }

    /// <summary>
    /// Creates a synthetic implemented descriptor for canonical-order tests.
    /// </summary>
    private static CapabilityToolsetDescriptor CreateDescriptor(
        string toolName,
        CapabilityToolset toolset,
        CapabilityToolsetStability stability = CapabilityToolsetStability.Stable)
    {
        return new CapabilityToolsetDescriptor(
            toolset,
            stability,
            $"Synthetic {toolName} capability.",
            [$"{toolName}_read"],
            [],
            []);
    }

    /// <summary>
    /// Extracts one successful selection while preserving useful failure output.
    /// </summary>
    private static CapabilityToolsetSelection RequireSuccess(
        OperationResult<CapabilityToolsetSelection> result)
    {
        return Assert.IsType<OperationSuccess<CapabilityToolsetSelection>>(result.Value).Data;
    }
}
