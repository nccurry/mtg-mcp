using MtgMcp.Core.Decks;

namespace MtgMcp.Archidekt.Tests;

/// <summary>
/// Proves local/provider translation, baseline validation, and deterministic primitive planning.
/// </summary>
public sealed class ArchidektLocalMapperTests
{
    /// <summary>
    /// Verifies remote evidence becomes a deterministic local graph and lossless baseline.
    /// </summary>
    [Fact]
    public void RemoteRoundTrip_PreservesStableIdsAndProviderRelations()
    {
        RemoteDeckSnapshot remote = ParseDeck();
        Guid bindingId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        DeckProviderBinding binding = new(
            bindingId,
            "archidekt",
            remote.RemoteId,
            remote.RemoteUri,
            remote.Evidence.ContractVersion,
            remote.RemoteFingerprint,
            DateTimeOffset.UtcNow,
            null);

        DeckCreateRequest request = ArchidektLocalMapper.ToCreateRequest(remote, binding);
        string baselineJson = ArchidektLocalMapper.CreateBaseline(request, remote);
        ArchidektSyncBaseline baseline = ArchidektLocalMapper.ParseBaseline(baselineJson);

        Assert.Equal(2, request.Entries?.Count);
        Assert.Equal(2, request.Categories?.Count);
        Assert.Equal(2, request.CategoryAssignments?.Count);
        Assert.Equal(bindingId, Assert.Single(request.ProviderBindings!).BindingId);
        Assert.Equal(remote.RemoteFingerprint, baseline.RemoteFingerprint);
        Assert.Equal(2, baseline.EntryRelations.Count);
        Assert.Equal(2, baseline.CategoryRelations.Count);
    }

    /// <summary>
    /// Verifies corrupt and unsupported baseline shapes remain explicitly unavailable.
    /// </summary>
    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":2,\"remoteDeckId\":\"42\",\"remoteFingerprint\":\"x\",\"localFingerprint\":\"y\"}")]
    public void ParseBaseline_RejectsCorruptOrUnsupportedState(string json)
    {
        Assert.Throws<InvalidDataException>(() => ArchidektLocalMapper.ParseBaseline(json));
    }

    /// <summary>
    /// Verifies the three-way diff distinguishes local, remote, and concurrent changes.
    /// </summary>
    [Fact]
    public void Diff_ReportsLocalRemoteAndConflictStates()
    {
        RemoteDeckSnapshot remote = ParseDeck();
        ArchidektSyncBaseline baseline = Baseline(remote, "local-before");

        ArchidektSyncDiff unchanged = ArchidektSyncPlanner.Diff(
            Guid.NewGuid(), 1, "local-before", remote, remote, baseline);
        RemoteDeckSnapshot localProjection = remote with { Name = "Local name" };
        ArchidektSyncDiff local = ArchidektSyncPlanner.Diff(
            Guid.NewGuid(), 2, "local-after", localProjection, remote, baseline);
        RemoteDeckSnapshot changedRemote = remote with
        {
            Description = "Remote description",
            RemoteFingerprint = "remote-after",
        };
        ArchidektSyncDiff remoteChanged = ArchidektSyncPlanner.Diff(
            Guid.NewGuid(), 1, "local-before", remote, changedRemote, baseline);
        ArchidektSyncDiff conflict = ArchidektSyncPlanner.Diff(
            Guid.NewGuid(), 2, "local-after", localProjection, changedRemote, baseline);

        Assert.Empty(unchanged.Differences);
        ArchidektDifference localDifference = Assert.Single(local.Differences);
        Assert.Equal("/metadata/name", localDifference.Path);
        Assert.Equal("local-changed", localDifference.State);
        ArchidektDifference remoteDifference = Assert.Single(remoteChanged.Differences);
        Assert.Equal("/metadata/description", remoteDifference.Path);
        Assert.Equal("remote-changed", remoteDifference.State);
        Assert.True(conflict.HasConflicts);
        Assert.Equal(2, conflict.Differences.Count);
    }

    /// <summary>
    /// Verifies entry additions and removals retain stable paths and model-readable values.
    /// </summary>
    [Fact]
    public void Diff_ReportsPathAddressedEntryAdditionsAndRemovals()
    {
        RemoteDeckSnapshot remote = ParseDeck();
        ArchidektSyncBaseline baseline = Baseline(remote, "local-before");
        RemoteDeckEntry added = remote.Entries[0] with
        {
            ProviderRelationId = string.Empty,
            ProviderCardId = "999",
            CardName = "Added Card",
            PrintingId = null,
            SetCode = "TST",
            CollectorNumber = "1",
        };
        RemoteDeckSnapshot localProjection = remote with { Entries = [remote.Entries[0], added] };

        ArchidektSyncDiff diff = ArchidektSyncPlanner.Diff(
            Guid.NewGuid(),
            2,
            "local-after",
            localProjection,
            remote,
            baseline);

        Assert.Contains(diff.Differences, value =>
            value.State == "local-added" &&
            value.Path.StartsWith("/entries/printing:", StringComparison.Ordinal) &&
            value.LocalValue!.Contains("Added Card", StringComparison.Ordinal));
        Assert.Contains(diff.Differences, value =>
            value.State == "local-removed" &&
            value.Path.StartsWith("/entries/relation:", StringComparison.Ordinal) &&
            value.BaselineValue is not null);
    }

    /// <summary>
    /// Verifies a local-only field change still blocks a pull even when Archidekt cannot represent it.
    /// </summary>
    [Fact]
    public void Diff_ReportsUnrepresentableLocalContentChange()
    {
        RemoteDeckSnapshot remote = ParseDeck();
        ArchidektSyncBaseline baseline = Baseline(remote, "local-before");

        ArchidektSyncDiff diff = ArchidektSyncPlanner.Diff(
            Guid.NewGuid(),
            2,
            "local-color-changed",
            remote,
            remote,
            baseline);

        ArchidektDifference difference = Assert.Single(diff.Differences);
        Assert.Equal("/localOnly/content", difference.Path);
        Assert.Equal("local-changed", difference.State);
    }

    /// <summary>
    /// Verifies remote planning orders metadata, categories, entry upserts, removals, and final verification cost.
    /// </summary>
    [Fact]
    public void PlanRemoteApply_ProducesStablePrimitiveSequenceAndBound()
    {
        RemoteDeckSnapshot current = ParseDeck();
        RemoteDeckCategory existing = current.Categories[0];
        RemoteDeckCategory added = new("", "Removal", true, true, false, 3);
        RemoteDeckEntry updated = current.Entries[0] with { Quantity = 2 };
        RemoteDeckEntry addedEntry = current.Entries[1] with
        {
            ProviderRelationId = string.Empty,
            ProviderCardId = string.Empty,
            CardName = "New Card",
            PrintingId = null,
            SetCode = null,
            CollectorNumber = null,
        };
        RemoteDeckSnapshot target = current with
        {
            Name = "Changed",
            Categories = [existing with { SortOrder = 2 }, added],
            Entries = [updated, addedEntry],
            ContentFingerprint = "target-content",
        };

        ArchidektRemotePlan plan = ArchidektSyncPlanner.PlanRemoteApply(current, target);

        Assert.Equal(
            ["metadata-update", "category-create", "category-delete", "entry-update", "entry-add", "entry-remove"],
            plan.PublicOperations.Select(value => value.Kind));
        Assert.Equal(8, plan.PredictedProviderRequests);
        Assert.Equal(64, plan.PlanFingerprint.Length);
    }

    /// <summary>
    /// Verifies final apply checks bind unique provider-generated relations and accept provider ordering inside a caller tie.
    /// </summary>
    [Fact]
    public void PlanRemoteVerification_RebindsGeneratedRelationsWithinTiedOrder()
    {
        RemoteDeckSnapshot observed = ParseDeck();
        RemoteDeckSnapshot expected = observed with
        {
            Entries = observed.Entries.Select(value => value with
            {
                ProviderRelationId = string.Empty,
                ProviderCardId = string.Empty,
                SortOrder = 0,
            }).ToArray(),
        };

        ArchidektRemotePlan ordinary = ArchidektSyncPlanner.PlanRemoteApply(observed, expected);
        ArchidektRemotePlan verification = ArchidektSyncPlanner.PlanRemoteVerification(observed, expected);

        Assert.NotEmpty(ordinary.PublicOperations);
        Assert.Empty(verification.PublicOperations);
    }

    /// <summary>
    /// Verifies indistinguishable provider relations bind deterministically while preserving multiplicity.
    /// </summary>
    [Fact]
    public void PlanRemoteVerification_BindsOneDuplicateAndRemovesTheExtra()
    {
        RemoteDeckSnapshot observed = ParseDeck();
        RemoteDeckEntry first = observed.Entries[0];
        RemoteDeckEntry duplicate = first with { ProviderRelationId = "different-relation" };
        observed = observed with { Entries = [first, duplicate] };
        RemoteDeckSnapshot expected = observed with
        {
            Entries =
            [
                first with
                {
                    ProviderRelationId = string.Empty,
                    ProviderCardId = string.Empty,
                },
            ],
        };

        ArchidektRemotePlan verification = ArchidektSyncPlanner.PlanRemoteVerification(observed, expected);

        Assert.DoesNotContain(verification.PublicOperations, value => value.Kind == "entry-add");
        Assert.Single(verification.PublicOperations, value => value.Kind == "entry-remove");
    }

    /// <summary>
    /// Verifies provider-controlled category array ordering does not create a false residual mutation.
    /// </summary>
    [Fact]
    public void PlanRemoteVerification_IgnoresCategoryArrayOrder()
    {
        RemoteDeckSnapshot observed = ParseDeck();
        RemoteDeckEntry source = observed.Entries[0] with
        {
            CategoryNames = ["Commander", "Mainboard"],
            PrimaryCategoryName = "Commander",
        };
        observed = observed with { Entries = [source, .. observed.Entries.Skip(1)] };
        RemoteDeckSnapshot expected = observed with
        {
            Entries =
            [
                source with
                {
                    ProviderRelationId = string.Empty,
                    ProviderCardId = string.Empty,
                    CategoryNames = ["Mainboard", "Commander"],
                },
                .. observed.Entries.Skip(1),
            ],
        };

        ArchidektRemotePlan verification = ArchidektSyncPlanner.PlanRemoteVerification(observed, expected);

        Assert.Empty(verification.PublicOperations);
    }

    /// <summary>
    /// Verifies model collections copy caller-owned mutable inputs.
    /// </summary>
    [Fact]
    public void Models_CopyCallerCollections()
    {
        List<string> categories = ["Mainboard"];
        RemoteDeckEntry entry = new(
            "1", "2", 1, "Card", null, null, null, null, "en", "nonfoil", "main",
            categories, "Mainboard", 0);
        categories.Add("Late mutation");

        Assert.Equal(["Mainboard"], entry.CategoryNames);
    }

    /// <summary>
    /// Parses the reusable complete deck fixture.
    /// </summary>
    private static RemoteDeckSnapshot ParseDeck()
    {
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(
            ArchidektTestPayloads.Deck);
        return ArchidektDeckContractMapper.MapDeck(
            document.RootElement,
            ArchidektTestPayloads.Deck,
            DateTimeOffset.UtcNow,
            "GET");
    }

    /// <summary>
    /// Creates one complete baseline around the supplied remote evidence.
    /// </summary>
    private static ArchidektSyncBaseline Baseline(RemoteDeckSnapshot remote, string localFingerprint)
    {
        return new ArchidektSyncBaseline(
            1,
            remote.RemoteId,
            remote.RemoteFingerprint,
            localFingerprint,
            remote,
            new Dictionary<Guid, string>(),
            new Dictionary<Guid, string>());
    }
}
