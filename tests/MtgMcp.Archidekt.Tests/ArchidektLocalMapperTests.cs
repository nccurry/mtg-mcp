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
    /// Verifies reordered or recased secondary categories never plan an entry update.
    /// </summary>
    [Fact]
    public void PlanRemoteApply_CategoryOrderOnlyDifference_PlansNoEntryOperation()
    {
        RemoteDeckSnapshot current = ParseDeck();
        RemoteDeckEntry remote = current.Entries[0] with
        {
            CategoryNames = ["Maybeboard", "Sideboard", "Removal"],
            PrimaryCategoryName = "Removal",
        };
        current = current with { Entries = [remote] };
        RemoteDeckSnapshot target = current with
        {
            Entries = [remote with { CategoryNames = ["maybeboard", "Removal", "Sideboard"] }],
        };

        ArchidektRemotePlan plan = ArchidektSyncPlanner.PlanRemoteApply(current, target);

        Assert.Empty(plan.PublicOperations);
    }

    /// <summary>
    /// Verifies the same category set with a different primary category still plans one entry update.
    /// </summary>
    [Fact]
    public void PlanRemoteApply_PrimaryCategoryChange_PlansEntryUpdate()
    {
        RemoteDeckSnapshot current = ParseDeck();
        RemoteDeckEntry remote = current.Entries[0] with
        {
            CategoryNames = ["Removal", "Sideboard"],
            PrimaryCategoryName = "Removal",
        };
        current = current with { Entries = [remote] };
        RemoteDeckSnapshot target = current with
        {
            Entries = [remote with { CategoryNames = ["Sideboard", "Removal"], PrimaryCategoryName = "Sideboard" }],
        };

        ArchidektRemotePlan plan = ArchidektSyncPlanner.PlanRemoteApply(current, target);

        Assert.Equal(["entry-update"], plan.PublicOperations.Select(value => value.Kind));
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
    /// Verifies a newly created Maybeboard category remains outside Archidekt deck and price totals.
    /// </summary>
    [Fact]
    public void ToRemoteTarget_NewMaybeboardIsExcludedFromDeckAndPrice()
    {
        RemoteDeckSnapshot remote = ParseDeck();
        DeckDocument local = new(
            Guid.NewGuid(),
            remote.Name,
            remote.Description,
            remote.Format,
            Revision: 1,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            Entries: [],
            Categories: [new DeckCategory(Guid.NewGuid(), "Maybeboard", null, 2)],
            CategoryAssignments: [],
            ProviderBindings: []);

        RemoteDeckSnapshot target = ArchidektLocalMapper.ToRemoteTarget(
            local,
            Baseline(remote, "local"),
            remote);

        RemoteDeckCategory maybeboard = Assert.Single(target.Categories);
        Assert.Equal(false, maybeboard.IncludedInDeck);
        Assert.Equal(false, maybeboard.IncludedInPrice);
    }

    /// <summary>
    /// Verifies an unchanged pull retains Archidekt's primary-first category order instead of
    /// normalizing it by the opaque local category identifier.
    /// </summary>
    [Fact]
    public void ToRemoteTarget_PreservesStoredCategoryAssignmentOrder()
    {
        RemoteDeckSnapshot parsed = ParseDeck();
        RemoteDeckCategory first = parsed.Categories[0] with
        {
            Name = "Primary",
            SortOrder = 0,
        };
        RemoteDeckCategory second = parsed.Categories[1] with
        {
            Name = "Secondary",
            SortOrder = 1,
        };
        Guid firstId = ArchidektContract.StableGuid("category", first.ProviderCategoryId);
        Guid secondId = ArchidektContract.StableGuid("category", second.ProviderCategoryId);
        string[] categoryNames = firstId.CompareTo(secondId) < 0
            ? [second.Name, first.Name]
            : [first.Name, second.Name];
        RemoteDeckEntry entry = parsed.Entries[0] with
        {
            CategoryNames = categoryNames,
            PrimaryCategoryName = categoryNames[0],
        };
        RemoteDeckSnapshot remote = parsed with
        {
            Categories = [first, second],
            Entries = [entry],
        };
        DeckProviderBinding binding = new(
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            "archidekt",
            remote.RemoteId,
            remote.RemoteUri,
            remote.Evidence.ContractVersion,
            remote.RemoteFingerprint,
            DateTimeOffset.UtcNow,
            null);
        DeckCreateRequest request = ArchidektLocalMapper.ToCreateRequest(remote, binding);
        DeckDocument local = new(
            Guid.NewGuid(),
            remote.Name,
            remote.Description,
            remote.Format,
            Revision: 1,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            request.Entries!.Select(value => new DeckEntry(
                value.EntryId!.Value,
                value.Quantity,
                value.CardName,
                value.OracleId,
                value.PrintingId,
                value.SetCode,
                value.CollectorNumber,
                value.Language,
                value.Finish,
                value.Zone,
                value.SortOrder)).ToArray(),
            request.Categories!.Select(value => new DeckCategory(
                value.CategoryId!.Value,
                value.Name,
                value.Color,
                value.SortOrder)).ToArray(),
            request.CategoryAssignments!,
            [binding]);
        ArchidektSyncBaseline baseline = ArchidektLocalMapper.ParseBaseline(
            ArchidektLocalMapper.CreateBaseline(request, remote));

        RemoteDeckSnapshot target = ArchidektLocalMapper.ToRemoteTarget(local, baseline, remote);

        RemoteDeckEntry mapped = Assert.Single(target.Entries);
        Assert.Equal(categoryNames, mapped.CategoryNames);
        Assert.Equal(categoryNames[0], mapped.PrimaryCategoryName);
    }

    /// <summary>
    /// Verifies rows created by a push are linked to their new provider IDs, so a later push plans only real changes.
    /// </summary>
    [Fact]
    public void CreatePushBaseline_CreatedRows_LinkAndLaterPushPlansOnlyNewEntries()
    {
        (RemoteDeckSnapshot pulled, DeckDocument local, ArchidektSyncBaseline pullBaseline) = PullFixture();
        (DeckDocument edited, DeckEntry charm, DeckEntry bombardment, Guid protectionId) = AddProtectionRows(local);
        RemoteDeckSnapshot pushed = ArchidektLocalMapper.ToRemoteTarget(edited, pullBaseline, pulled);
        RemoteDeckSnapshot verified = AssignProviderIds(pushed);

        (string json, IReadOnlyList<Guid>? unlinkedLocalIds) =
            ArchidektLocalMapper.CreatePushBaseline(edited, verified, pushed);
        ArchidektSyncBaseline afterPush = ArchidektLocalMapper.ParseBaseline(json);
        DeckEntry[] sideboard = Enumerable.Range(0, 10)
            .Select(index => NewEntry($"Sideboard Card {index}", "sideboard", 100 + index))
            .ToArray();
        DeckDocument later = edited with { Entries = [.. edited.Entries, .. sideboard] };
        RemoteDeckSnapshot laterTarget = ArchidektLocalMapper.ToRemoteTarget(later, afterPush, verified);
        ArchidektRemotePlan plan = ArchidektSyncPlanner.PlanRemoteApply(verified, laterTarget);

        Assert.Null(unlinkedLocalIds);
        Assert.Equal("9001", afterPush.CategoryRelations[protectionId]);
        Assert.Equal("9101", afterPush.EntryRelations[charm.EntryId]);
        Assert.Equal("9102", afterPush.EntryRelations[bombardment.EntryId]);
        Assert.Equal(10, plan.PublicOperations.Count);
        Assert.All(plan.PublicOperations, value => Assert.Equal("entry-add", value.Kind));
    }

    /// <summary>
    /// Verifies links follow content, not row order, when the provider returns rows shuffled with filled card details.
    /// </summary>
    [Fact]
    public void CreatePushBaseline_ShuffledVerifiedRowsWithCardDetails_StillLinkByContent()
    {
        (RemoteDeckSnapshot pulled, DeckDocument local, ArchidektSyncBaseline pullBaseline) = PullFixture();
        (DeckDocument edited, DeckEntry charm, DeckEntry bombardment, _) = AddProtectionRows(local);
        RemoteDeckSnapshot pushed = ArchidektLocalMapper.ToRemoteTarget(edited, pullBaseline, pulled);
        RemoteDeckSnapshot assigned = AssignProviderIds(pushed);
        List<RemoteDeckEntry> shuffled = [];
        foreach (RemoteDeckEntry value in assigned.Entries.Reverse())
        {
            shuffled.Add(value.ProviderRelationId is "9101" or "9102"
                ? value with
                {
                    PrintingId = Guid.NewGuid(),
                    SetCode = "xyz",
                    ProviderCardId = "card-" + value.ProviderRelationId,
                }
                : value);
        }

        RemoteDeckSnapshot verified = assigned with { Entries = shuffled };

        (string json, IReadOnlyList<Guid>? unlinkedLocalIds) =
            ArchidektLocalMapper.CreatePushBaseline(edited, verified, pushed);
        ArchidektSyncBaseline afterPush = ArchidektLocalMapper.ParseBaseline(json);

        Assert.Null(unlinkedLocalIds);
        Assert.Equal("9101", afterPush.EntryRelations[charm.EntryId]);
        Assert.Equal("9102", afterPush.EntryRelations[bombardment.EntryId]);
    }

    /// <summary>
    /// Verifies a pushed row with no exact provider match is reported as unlinked rather than guessed.
    /// </summary>
    [Fact]
    public void CreatePushBaseline_UnmatchedCreatedRow_ReportsUnlinkedId()
    {
        (RemoteDeckSnapshot pulled, DeckDocument local, ArchidektSyncBaseline pullBaseline) = PullFixture();
        DeckEntry charm = NewEntry("Boros Charm", "main", 50);
        DeckDocument edited = local with { Entries = [.. local.Entries, charm] };
        RemoteDeckSnapshot pushed = ArchidektLocalMapper.ToRemoteTarget(edited, pullBaseline, pulled);
        RemoteDeckSnapshot verified = AssignProviderIds(pushed);
        verified = verified with
        {
            Entries = verified.Entries
                .Select(value => value.CardName == "Boros Charm" ? value with { CardName = "Lightning Helix" } : value)
                .ToArray(),
        };

        (string json, IReadOnlyList<Guid>? unlinkedLocalIds) =
            ArchidektLocalMapper.CreatePushBaseline(edited, verified, pushed);
        ArchidektSyncBaseline afterPush = ArchidektLocalMapper.ParseBaseline(json);

        Assert.Equal([charm.EntryId], unlinkedLocalIds);
        Assert.False(afterPush.EntryRelations.ContainsKey(charm.EntryId));
    }

    /// <summary>
    /// Verifies a pushed category the provider did not return stores no link and is not reported.
    /// </summary>
    [Fact]
    public void CreatePushBaseline_UnmatchedCategory_StoresNoLink()
    {
        (RemoteDeckSnapshot pulled, DeckDocument local, ArchidektSyncBaseline pullBaseline) = PullFixture();
        Guid protectionId = Guid.NewGuid();
        DeckDocument edited = local with
        {
            Categories = [.. local.Categories, new DeckCategory(protectionId, "Protection", null, 9)],
        };
        RemoteDeckSnapshot pushed = ArchidektLocalMapper.ToRemoteTarget(edited, pullBaseline, pulled);
        RemoteDeckSnapshot verified = AssignProviderIds(pushed);
        verified = verified with
        {
            Categories = verified.Categories.Where(value => value.Name != "Protection").ToArray(),
        };

        (string json, IReadOnlyList<Guid>? unlinkedLocalIds) =
            ArchidektLocalMapper.CreatePushBaseline(edited, verified, pushed);
        ArchidektSyncBaseline afterPush = ArchidektLocalMapper.ParseBaseline(json);

        Assert.Null(unlinkedLocalIds);
        Assert.False(afterPush.CategoryRelations.ContainsKey(protectionId));
    }

    /// <summary>
    /// Verifies a pushed target that was not built from the local deck is rejected.
    /// </summary>
    [Fact]
    public void CreatePushBaseline_TargetRowCountDiffersFromLocal_Throws()
    {
        (RemoteDeckSnapshot pulled, DeckDocument local, ArchidektSyncBaseline pullBaseline) = PullFixture();
        RemoteDeckSnapshot pushed = ArchidektLocalMapper.ToRemoteTarget(local, pullBaseline, pulled);
        RemoteDeckSnapshot shortTarget = pushed with { Entries = pushed.Entries.Skip(1).ToArray() };

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            ArchidektLocalMapper.CreatePushBaseline(local, pulled, shortTarget));

        Assert.Equal("target", exception.ParamName);
    }

    /// <summary>
    /// Adds two new entries and one new category to a pulled local deck, as a user's local edit would.
    /// </summary>
    private static (DeckDocument Edited, DeckEntry Charm, DeckEntry Bombardment, Guid CategoryId) AddProtectionRows(
        DeckDocument local)
    {
        Guid protectionId = Guid.NewGuid();
        DeckEntry charm = NewEntry("Boros Charm", "main", 50);
        DeckEntry bombardment = NewEntry("Goblin Bombardment", "main", 51);
        DeckDocument edited = local with
        {
            Entries = [.. local.Entries, charm, bombardment],
            Categories = [.. local.Categories, new DeckCategory(protectionId, "Protection", null, 9)],
            CategoryAssignments =
            [
                .. local.CategoryAssignments,
                new DeckCategoryAssignment(charm.EntryId, protectionId, true),
                new DeckCategoryAssignment(bombardment.EntryId, protectionId, true),
            ],
        };
        return (edited, charm, bombardment, protectionId);
    }

    /// <summary>
    /// Builds a local deck and baseline exactly as a pull of the shared fixture would.
    /// </summary>
    private static (RemoteDeckSnapshot Remote, DeckDocument Local, ArchidektSyncBaseline Baseline) PullFixture()
    {
        RemoteDeckSnapshot remote = ParseDeck();
        DeckProviderBinding binding = new(
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            "archidekt",
            remote.RemoteId,
            remote.RemoteUri,
            remote.Evidence.ContractVersion,
            remote.RemoteFingerprint,
            DateTimeOffset.UtcNow,
            null);
        DeckCreateRequest request = ArchidektLocalMapper.ToCreateRequest(remote, binding);
        DeckDocument local = new(
            Guid.NewGuid(),
            remote.Name,
            remote.Description,
            remote.Format,
            Revision: 1,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            request.Entries!.Select(value => new DeckEntry(
                value.EntryId!.Value,
                value.Quantity,
                value.CardName,
                value.OracleId,
                value.PrintingId,
                value.SetCode,
                value.CollectorNumber,
                value.Language,
                value.Finish,
                value.Zone,
                value.SortOrder)).ToArray(),
            request.Categories!.Select(value => new DeckCategory(
                value.CategoryId!.Value,
                value.Name,
                value.Color,
                value.SortOrder)).ToArray(),
            request.CategoryAssignments!,
            [binding]);
        ArchidektSyncBaseline baseline = ArchidektLocalMapper.ParseBaseline(
            ArchidektLocalMapper.CreateBaseline(request, remote));
        return (remote, local, baseline);
    }

    /// <summary>
    /// Creates one local entry identified only by name, as a user's local addition would be.
    /// </summary>
    private static DeckEntry NewEntry(string cardName, string zone, int sortOrder)
    {
        return new DeckEntry(Guid.NewGuid(), 1, cardName, null, null, null, null, "en", "nonfoil", zone, sortOrder);
    }

    /// <summary>
    /// Simulates Archidekt assigning IDs to the categories and relations a push created.
    /// </summary>
    private static RemoteDeckSnapshot AssignProviderIds(RemoteDeckSnapshot pushed)
    {
        int nextCategory = 9001;
        int nextRelation = 9101;
        return pushed with
        {
            Categories = pushed.Categories
                .Select(value => string.IsNullOrEmpty(value.ProviderCategoryId)
                    ? value with { ProviderCategoryId = (nextCategory++).ToString(System.Globalization.CultureInfo.InvariantCulture) }
                    : value)
                .ToArray(),
            Entries = pushed.Entries
                .Select(value => string.IsNullOrEmpty(value.ProviderRelationId)
                    ? value with
                    {
                        ProviderRelationId = (nextRelation++).ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ProviderCardId = "card",
                    }
                    : value)
                .ToArray(),
        };
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
