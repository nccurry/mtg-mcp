using System.Text.Json;
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
    /// Verifies the category deletions and the plan fingerprint do not depend on the order Archidekt lists the
    /// categories in.
    /// </summary>
    [Fact]
    public void PlanRemoteApply_CategoriesListedInAnotherOrder_GiveTheSamePlanFingerprint()
    {
        RemoteDeckSnapshot start = ParseDeck();
        RemoteDeckCategory kept = start.Categories[0];
        RemoteDeckCategory zeta = new("901", "Zeta", true, false, false, 8);
        RemoteDeckCategory alpha = new("902", "Alpha", true, false, false, 9);
        RemoteDeckSnapshot target = start with { Categories = [kept] };

        ArchidektRemotePlan forward = ArchidektSyncPlanner.PlanRemoteApply(
            start with { Categories = [kept, zeta, alpha] },
            target);
        ArchidektRemotePlan reversed = ArchidektSyncPlanner.PlanRemoteApply(
            start with { Categories = [alpha, zeta, kept] },
            target);

        Assert.Equal(["Alpha", "Zeta"], forward.PublicOperations.Select(value => value.Subject));
        Assert.Equal(
            forward.PublicOperations.Select(value => value.Subject),
            reversed.PublicOperations.Select(value => value.Subject));
        Assert.Equal(forward.PlanFingerprint, reversed.PlanFingerprint);
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
    /// A fixed local deck ID, so the row IDs derived from it, and their sorted order, are the same on every run.
    /// </summary>
    private static readonly Guid PullFixtureDeckId = Guid.Parse("0f0e0d0c-0b0a-4908-8706-050403020100");

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
        DeckCreateRequest request = ArchidektLocalMapper.ToCreateRequest(remote, binding, PullFixtureDeckId);
        DeckDocument local = new(
            PullFixtureDeckId,
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
    /// Builds a local deck and a baseline the way version 0.10.0 pulled it: the baseline's local fingerprint is the hash
    /// of the creation draft with rows in the order of the Archidekt response, schema 1, and no hidden-field value.
    /// </summary>
    private static (RemoteDeckSnapshot Remote, DeckDocument Local, ArchidektSyncBaseline Baseline)
        Version0100PullFixture()
    {
        (RemoteDeckSnapshot remote, DeckDocument local, ArchidektSyncBaseline current) = PullFixture();
        // Port of FingerprintLocalContent(DeckCreateRequest) in 0.10.0: no sorting, rows in drafted order. The fixture
        // deck holds the rows exactly as the draft listed them.
        string fingerprint = ArchidektContract.Fingerprint(new
        {
            local.Name,
            local.Description,
            local.Format,
            entries = local.Entries.Select(value => new
            {
                value.EntryId,
                value.Quantity,
                value.CardName,
                value.OracleId,
                value.PrintingId,
                value.SetCode,
                value.CollectorNumber,
                value.Language,
                value.Finish,
                value.Zone,
                value.SortOrder,
            }),
            categories = local.Categories.Select(value => new
            {
                value.CategoryId,
                value.Name,
                value.Color,
                value.SortOrder,
            }),
            assignments = local.CategoryAssignments.Select(value => new
            {
                value.EntryId,
                value.CategoryId,
                value.IsPrimary,
            }),
        });
        ArchidektSyncBaseline baseline = current with
        {
            SchemaVersion = 1,
            LocalFingerprint = fingerprint,
            LocalInvisibleFingerprint = null,
        };
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
    /// Verifies a created row the provider returned with other zone or quantity is still linked to its provider row
    /// when it is the only row with that card, so the next push does not add it again.
    /// </summary>
    [Fact]
    public void LinkRows_ForACreatedRowReturnedDifferently_LinksByCardIdentity()
    {
        RemoteDeckSnapshot parsed = ParseDeck();
        RemoteDeckEntry returned = parsed.Entries[0] with { Zone = "sideboard", Quantity = parsed.Entries[0].Quantity + 1 };
        RemoteDeckSnapshot verified = parsed with { Entries = [returned] };
        RemoteDeckSnapshot target = parsed with
        {
            Entries = [parsed.Entries[0] with { ProviderRelationId = string.Empty }],
        };

        ArchidektRowLinks links = ArchidektSyncPlanner.LinkRows(verified, target);

        Assert.Equal([returned.ProviderRelationId], links.EntryRelationIds);
    }

    /// <summary>
    /// Verifies a row is refused exactly when its zone differs from the zone a read gives it: a Sideboard category
    /// that is included in the deck, or has no setting, is not a contradiction, and a category that is outside the
    /// deck but has no special name is not one either.
    /// </summary>
    [Theory]
    [InlineData("sideboard", "Sideboard", true, null)]
    [InlineData("sideboard", "Sideboard", null, null)]
    [InlineData("main", "Out", false, "maybeboard")]
    [InlineData("maybeboard", "Out", false, null)]
    [InlineData("main", "Out", true, null)]
    [InlineData("main", "Out", null, null)]
    [InlineData("main", "Sideboard", true, "sideboard")]
    [InlineData("main", "Sideboard", false, "sideboard")]
    [InlineData("maybeboard", "Sideboard", false, "sideboard")]
    [InlineData("sideboard", "Removal", true, "main")]
    public void FindZoneContradictions_FollowsTheZoneAReadGives(
        string zone,
        string category,
        bool? includedInDeck,
        string? expectedZone)
    {
        RemoteDeckSnapshot parsed = ParseDeck();
        RemoteDeckCategory named = parsed.Categories[0] with { Name = category, IncludedInDeck = includedInDeck };
        RemoteDeckEntry existing = parsed.Entries[0] with
        {
            Zone = "main",
            CategoryNames = ["Other"],
            PrimaryCategoryName = "Other",
        };
        RemoteDeckEntry wanted = existing with
        {
            Zone = zone,
            CategoryNames = [category],
            PrimaryCategoryName = category,
        };
        RemoteDeckSnapshot current = parsed with { Categories = [named], Entries = [existing] };
        RemoteDeckSnapshot target = current with { Entries = [wanted] };

        IReadOnlyList<ArchidektZoneContradiction> found = ArchidektSyncPlanner.FindZoneContradictions(
            ArchidektSyncPlanner.PlanRemoteApply(current, target),
            target);

        Assert.Equal(expectedZone is null ? [] : [expectedZone], found.Select(value => value.ExpectedZone));
    }

    /// <summary>
    /// Verifies only the first category decides whether an entry is in the deck, and that the commander, sideboard and
    /// maybeboard rules come before it: an entry with no category, or a category with no flag, is in the deck.
    /// </summary>
    [Theory]
    [InlineData("Multiplayer", "maybeboard")]
    [InlineData("Removal", "main")]
    [InlineData("Multiplayer,Removal", "maybeboard")]
    [InlineData("Removal,Multiplayer", "main")]
    [InlineData("Unflagged", "main")]
    [InlineData("Unknown", "main")]
    [InlineData("", "main")]
    [InlineData("Multiplayer,Sideboard", "sideboard")]
    [InlineData("Multiplayer,Commander", "commander")]
    [InlineData("Multiplayer,Maybeboard", "maybeboard")]
    public void MapZone_FollowsTheFirstCategoryFlag(string categories, string expectedZone)
    {
        RemoteDeckSnapshot deck = WithCategories("Multiplayer", "Removal", "Sideboard", "Commander", "Maybeboard");
        deck = deck with
        {
            Categories =
            [
                deck.Categories[0] with { IncludedInDeck = false },
                deck.Categories[1] with { IncludedInDeck = true },
                deck.Categories[2],
                deck.Categories[3] with { IncludedInDeck = true },
                deck.Categories[4],
                new RemoteDeckCategory("200", "Unflagged", null, true, false, 9),
            ],
        };
        string[] names = categories.Length == 0 ? [] : categories.Split(',');
        Dictionary<string, RemoteDeckCategory> index = deck.Categories.ToDictionary(value => value.Name);

        Assert.Equal(expectedZone, ArchidektJsonContract.MapZone(names, primary: null, index));
    }

    /// <summary>
    /// Verifies a deck says an entry is in the deck unless its first category is flagged out: no category, a category
    /// the deck does not list, a category with no flag, and an included category all count as in the deck.
    /// </summary>
    [Theory]
    [InlineData("Multiplayer", false)]
    [InlineData("Multiplayer,Removal", false)]
    [InlineData("Removal,Multiplayer", true)]
    [InlineData("Unflagged", true)]
    [InlineData("Unknown", true)]
    [InlineData("", true)]
    public void IsInDeck_IsFalseOnlyWhenTheFirstCategoryIsFlaggedOut(string categories, bool expected)
    {
        RemoteDeckSnapshot deck = WithCategories("Multiplayer", "Removal");
        deck = deck with
        {
            Categories =
            [
                deck.Categories[0] with { IncludedInDeck = false },
                deck.Categories[1] with { IncludedInDeck = true },
                new RemoteDeckCategory("200", "Unflagged", null, true, false, 9),
            ],
        };
        RemoteDeckEntry entry = ParseDeck().Entries[0] with
        {
            CategoryNames = categories.Length == 0 ? [] : categories.Split(','),
            PrimaryCategoryName = null,
        };

        Assert.Equal(expected, deck.IsInDeck(entry));
    }

    /// <summary>
    /// Verifies the one card reader takes the set from the edition code first, as a deck relation and a search result
    /// both carry it, uses a top-level set code only when there is no edition code, and refuses a card with no ID or
    /// no oracle name.
    /// </summary>
    [Theory]
    [InlineData(
        """
        {"id":5,"uid":"11111111-1111-1111-1111-111111111111","setCode":"top","collectorNumber":"7",
        "edition":{"editioncode":"ed"},"oracleCard":{"name":"Card","uid":"22222222-2222-2222-2222-222222222222"}}
        """,
        "ed")]
    [InlineData("""{"id":5,"setCode":"top","oracleCard":{"name":"Card"}}""", "top")]
    [InlineData("""{"id":5,"oracleCard":{"name":"Card"}}""", null)]
    public void ReadPrinting_TakesTheEditionCodeBeforeATopLevelSetCode(string json, string? expectedSet)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        ArchidektCardPrinting? printing = ArchidektJsonContract.ReadPrinting(document.RootElement);

        Assert.NotNull(printing);
        Assert.Equal(expectedSet, printing.SetCode);
        Assert.Equal("5", printing.ProviderCardId);
        Assert.Equal("Card", printing.CardName);
    }

    /// <summary>
    /// Verifies a card with no ID, no oracle card, or no oracle name is not read.
    /// </summary>
    [Theory]
    [InlineData("""{"oracleCard":{"name":"Card"}}""")]
    [InlineData("""{"id":5}""")]
    [InlineData("""{"id":5,"oracleCard":{"uid":"22222222-2222-2222-2222-222222222222"}}""")]
    public void ReadPrinting_RefusesACardWithoutAnIdOrAName(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.Null(ArchidektJsonContract.ReadPrinting(document.RootElement));
    }

    /// <summary>
    /// Verifies a card in two categories whose primary category is the one outside the deck, listed second, is
    /// refused in zone main, as Archidekt would not count it toward the deck.
    /// </summary>
    [Fact]
    public void FindZoneContradictions_ReadsThePrimaryCategoryFirst()
    {
        RemoteDeckSnapshot parsed = ParseDeck();
        RemoteDeckCategory ramp = parsed.Categories[0] with { Name = "Ramp", IncludedInDeck = true };
        RemoteDeckCategory considering = parsed.Categories[0] with
        {
            ProviderCategoryId = "900",
            Name = "Considering",
            IncludedInDeck = false,
        };
        RemoteDeckEntry entry = parsed.Entries[0] with
        {
            Zone = "main",
            CategoryNames = ["Ramp", "Considering"],
            PrimaryCategoryName = "Considering",
        };

        IReadOnlyList<ArchidektZoneContradiction> found = ArchidektSyncPlanner.FindZoneContradictions(
            [entry],
            [ramp, considering]);

        Assert.Equal(["maybeboard"], found.Select(value => value.ExpectedZone));
    }

    /// <summary>
    /// Verifies a primary category that is not the first name decides, in the zone and in the deck count: Archidekt
    /// treats the primary as the first category, so every caller reads the same order.
    /// </summary>
    [Theory]
    [InlineData("Ramp,Considering", "Considering", "maybeboard", false)]
    [InlineData("Ramp,Considering", "Ramp", "main", true)]
    [InlineData("Considering,Ramp", "Ramp", "main", true)]
    [InlineData("Ramp,Considering", null, "main", true)]
    [InlineData("Ramp,Considering,Sideboard", "Considering", "sideboard", false)]
    public void MapZoneAndIsInDeck_ReadThePrimaryCategoryFirst(
        string categories,
        string? primary,
        string expectedZone,
        bool expectedInDeck)
    {
        RemoteDeckSnapshot deck = WithCategories("Ramp", "Considering", "Sideboard");
        deck = deck with
        {
            Categories =
            [
                deck.Categories[0] with { IncludedInDeck = true },
                deck.Categories[1] with { IncludedInDeck = false },
                deck.Categories[2],
            ],
        };
        string[] names = categories.Split(',');
        Dictionary<string, RemoteDeckCategory> index = deck.Categories.ToDictionary(value => value.Name);
        RemoteDeckEntry entry = ParseDeck().Entries[0] with { CategoryNames = names, PrimaryCategoryName = primary };

        Assert.Equal(expectedZone, ArchidektJsonContract.MapZone(names, primary, index));
        Assert.Equal(expectedInDeck, deck.IsInDeck(entry));
    }

    /// <summary>
    /// Verifies a row the plan does not send is never reported.
    /// </summary>
    [Fact]
    public void FindZoneContradictions_IgnoresRowsThePlanDoesNotSend()
    {
        RemoteDeckSnapshot parsed = ParseDeck();
        RemoteDeckEntry row = parsed.Entries[0] with { Zone = "main", CategoryNames = ["Sideboard"] };
        RemoteDeckSnapshot same = parsed with { Entries = [row] };

        Assert.Empty(ArchidektSyncPlanner.FindZoneContradictions(ArchidektSyncPlanner.PlanRemoteApply(same, same), same));
    }

    /// <summary>
    /// Builds a deck snapshot with the named categories and no entries.
    /// </summary>
    private static RemoteDeckSnapshot WithCategories(params string[] names)
    {
        RemoteDeckSnapshot parsed = ParseDeck();
        return parsed with
        {
            Entries = [],
            Categories =
            [
                .. names.Select((name, index) => new RemoteDeckCategory(
                    (100 + index).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    name,
                    false,
                    true,
                    false,
                    index)),
            ],
        };
    }

    /// <summary>
    /// Lists the planned category operations as kind and name.
    /// </summary>
    private static string[] CategoryOperations(RemoteDeckSnapshot current, RemoteDeckSnapshot target)
    {
        return
        [
            .. ArchidektSyncPlanner.PlanRemoteApply(current, target).PublicOperations
                .Where(value => value.Kind.StartsWith("category-", StringComparison.Ordinal))
                .Select(value => $"{value.Kind}:{value.Subject}"),
        ];
    }

    /// <summary>
    /// Verifies adding a category that differs only by case from an existing one creates it and never renames the
    /// existing category.
    /// </summary>
    [Fact]
    public void PlanRemoteApply_AddingACaseVariantCategory_CreatesIt()
    {
        string[] operations = CategoryOperations(
            WithCategories("Sideboard"),
            WithCategories("Sideboard", "SIDEBOARD"));

        Assert.Equal(["category-create:SIDEBOARD"], operations);
    }

    /// <summary>
    /// Verifies deleting one of two case-variant categories deletes that one and leaves the other.
    /// </summary>
    [Fact]
    public void PlanRemoteApply_RemovingACaseVariantCategory_DeletesOnlyThatOne()
    {
        string[] operations = CategoryOperations(
            WithCategories("Sideboard", "SIDEBOARD"),
            WithCategories("Sideboard"));

        Assert.Equal(["category-delete:SIDEBOARD"], operations);
    }

    /// <summary>
    /// Verifies a category written with different case is a new category and the old one is deleted, not renamed.
    /// </summary>
    [Fact]
    public void PlanRemoteApply_ARecasedCategory_IsCreatedAndTheOldOneDeleted()
    {
        string[] operations = CategoryOperations(WithCategories("Sideboard"), WithCategories("sideboard"));

        Assert.Equal(["category-create:sideboard", "category-delete:Sideboard"], operations.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Verifies linking after a push never gives a target category a verified category that another target category
    /// matched exactly.
    /// </summary>
    [Fact]
    public void LinkRows_ACaseVariantCategory_NeverTakesAnExactlyMatchedOne()
    {
        RemoteDeckSnapshot verified = WithCategories("Sideboard");
        RemoteDeckSnapshot target = WithCategories("Sideboard", "SIDEBOARD");

        ArchidektRowLinks links = ArchidektSyncPlanner.LinkRows(verified, target);

        Assert.Equal(["100", null], links.CategoryIds);
    }

    /// <summary>
    /// Verifies a category move between two case-variant categories is planned and sent with the exact names.
    /// </summary>
    [Fact]
    public void PlanRemoteApply_MovingAnEntryBetweenCaseVariantCategories_IsAnUpdate()
    {
        RemoteDeckSnapshot parsed = ParseDeck();
        RemoteDeckSnapshot current = WithCategories("Sideboard", "SIDEBOARD");
        RemoteDeckEntry before = parsed.Entries[0] with
        {
            CategoryNames = ["Sideboard"],
            PrimaryCategoryName = "Sideboard",
        };
        RemoteDeckSnapshot start = current with { Entries = [before] };
        RemoteDeckSnapshot moved = current with
        {
            Entries = [before with { CategoryNames = ["SIDEBOARD"], PrimaryCategoryName = "SIDEBOARD" }],
        };

        ArchidektRemotePlan plan = ArchidektSyncPlanner.PlanRemoteApply(start, moved);

        Assert.Equal(["entry-update"], plan.PublicOperations.Select(value => value.Kind));
    }

    /// <summary>
    /// Builds a baseline-matching remote, and the same deck after both sides raised one quantity by the same amount.
    /// </summary>
    private static (RemoteDeckSnapshot Before, RemoteDeckSnapshot After) ConvergedPair()
    {
        RemoteDeckSnapshot before = ParseDeck();
        RemoteDeckSnapshot after = before with
        {
            Entries = [before.Entries[0] with { Quantity = before.Entries[0].Quantity + 1 }, .. before.Entries.Skip(1)],
        };
        return (before, after);
    }

    /// <summary>
    /// Verifies a converged row does not hide a local edit to a field no row shows: the deck still reads as changed.
    /// </summary>
    [Fact]
    public void Diff_ConvergedRowBesideAHiddenLocalEdit_KeepsLocalChanged()
    {
        (RemoteDeckSnapshot before, RemoteDeckSnapshot after) = ConvergedPair();
        ArchidektSyncBaseline baseline = Baseline(before, "local-before") with { LocalInvisibleFingerprint = "hidden-before" };

        ArchidektSyncDiff diff = ArchidektSyncPlanner.Diff(
            Guid.NewGuid(), 2, "local-after", after, after, baseline, "hidden-after");

        Assert.Contains(diff.Differences, value => value.State == "converged");
        Assert.Contains(diff.Differences, value => value.Path == "/localOnly/content" && value.State == "local-changed");
    }

    /// <summary>
    /// Verifies a converged row with no hidden edit is not a local change, and an edit to a hidden field with no row
    /// at all is still reported.
    /// </summary>
    [Fact]
    public void Diff_ConvergedRowAlone_IsNotALocalChange_ButAHiddenEditAloneIs()
    {
        (RemoteDeckSnapshot before, RemoteDeckSnapshot after) = ConvergedPair();
        ArchidektSyncBaseline baseline = Baseline(before, "local-before") with { LocalInvisibleFingerprint = "hidden" };

        ArchidektSyncDiff converged = ArchidektSyncPlanner.Diff(
            Guid.NewGuid(), 2, "local-after", after, after, baseline, "hidden");
        ArchidektSyncDiff hiddenOnly = ArchidektSyncPlanner.Diff(
            Guid.NewGuid(), 2, "local-after", before, before, baseline, "hidden-after");

        Assert.DoesNotContain(converged.Differences, value => value.Path == "/localOnly/content");
        Assert.Contains(hiddenOnly.Differences, value => value.Path == "/localOnly/content");
    }

    /// <summary>
    /// Verifies a baseline made by an earlier version is read as unchanged when the deck matches the order that
    /// version recorded, and a visible edit is reported as an ordinary local change with no unverified row.
    /// </summary>
    [Fact]
    public void Diff_AnEarlierVersionBaseline_IsNotStuckAsLocalChanged()
    {
        RemoteDeckSnapshot remote = ParseDeck();
        ArchidektSyncBaseline legacy = Baseline(remote, "legacy-local") with { SchemaVersion = 1 };
        RemoteDeckSnapshot edited = remote with
        {
            Entries = [remote.Entries[0] with { Quantity = remote.Entries[0].Quantity + 1 }, .. remote.Entries.Skip(1)],
        };

        ArchidektSyncDiff sameOrder = ArchidektSyncPlanner.Diff(
            Guid.NewGuid(), 2, "new-order", remote, remote, legacy, "hidden", "legacy-local");
        ArchidektSyncDiff visibleEdit = ArchidektSyncPlanner.Diff(
            Guid.NewGuid(), 2, "new-order", edited, remote, legacy, "hidden", "other-order");

        Assert.Empty(sameOrder.Differences);
        Assert.Contains(visibleEdit.Differences, value => value.State == "local-changed");
        Assert.DoesNotContain(visibleEdit.Differences, value => value.State == "local-changed-unverified");
    }

    /// <summary>
    /// Verifies a deck pulled by 0.10.0 reads as unchanged when the store lists its rows in a different order than the
    /// Archidekt response, and that an edit to it still shows. The baseline is made by a port of the 0.10.0 pull.
    /// </summary>
    [Fact]
    public void Diff_ADeckPulledByVersion0100InAnotherStoreOrder_IsUnchangedUntilEdited()
    {
        (RemoteDeckSnapshot remote, DeckDocument local, ArchidektSyncBaseline baseline) = Version0100PullFixture();
        Assert.True(local.Entries.Count > 1 && local.Categories.Count > 1 && local.CategoryAssignments.Count > 1);
        DeckDocument stored = local with
        {
            Entries = [.. local.Entries.Reverse()],
            Categories = [.. local.Categories.Reverse()],
            CategoryAssignments = [.. local.CategoryAssignments.Reverse()],
        };

        // The fixed deck ID must make the ID-sorted order differ from the drafted order, or this test proves nothing.
        Assert.NotEqual(
            local.Entries.Select(value => value.EntryId),
            local.Entries.Select(value => value.EntryId).Order());
        Assert.NotEqual(
            local.Categories.Select(value => value.CategoryId),
            local.Categories.Select(value => value.CategoryId).Order());
        Assert.NotEqual(baseline.LocalFingerprint, ArchidektLocalMapper.LocalFingerprint(stored));
        Assert.NotEqual(baseline.LocalFingerprint, ArchidektLocalMapper.LegacyLocalFingerprint(stored));
        ArchidektSyncDiff unedited = ArchidektSyncPlanner.Diff(stored, remote, remote, baseline);
        DeckDocument edited = stored with
        {
            Entries = [stored.Entries[0] with { Quantity = stored.Entries[0].Quantity + 1 }, .. stored.Entries.Skip(1)],
        };
        ArchidektSyncDiff afterEdit = ArchidektSyncPlanner.Diff(edited, remote, remote, baseline);
        DeckDocument recolored = stored with
        {
            Categories = [stored.Categories[0] with { Color = "#ff0000" }, .. stored.Categories.Skip(1)],
        };
        ArchidektSyncDiff afterRecolor = ArchidektSyncPlanner.Diff(recolored, remote, remote, baseline);

        Assert.Empty(unedited.Differences);
        Assert.NotEmpty(afterEdit.Differences);
        Assert.Contains(afterEdit.Differences, value => value.State.StartsWith("local-", StringComparison.Ordinal));
        Assert.Equal("local-changed-unverified", Assert.Single(afterRecolor.Differences).State);
    }

    /// <summary>
    /// Verifies a schema 1 baseline whose local fingerprint differs, in either order, with no row to show why, gets
    /// the unverified local row, because an edit to a category color or ordering cannot be told from no edit.
    /// </summary>
    [Fact]
    public void Diff_AnEarlierVersionBaselineWithAnUnexplainedChange_ReportsAnUnverifiedLocalChange()
    {
        RemoteDeckSnapshot remote = ParseDeck();
        ArchidektSyncBaseline legacy = Baseline(remote, "legacy-local") with { SchemaVersion = 1 };

        ArchidektSyncDiff diff = ArchidektSyncPlanner.Diff(
            Guid.NewGuid(), 2, "new-order", remote, remote, legacy, "hidden", "other-order");
        ArchidektSyncDiff noLegacyFingerprint = ArchidektSyncPlanner.Diff(
            Guid.NewGuid(), 2, "new-order", remote, remote, legacy);

        ArchidektDifference row = Assert.Single(diff.Differences);
        Assert.Equal("/localOnly/content", row.Path);
        Assert.Equal("local-changed-unverified", row.State);
        Assert.Equal("legacy-local", row.BaselineValue);
        Assert.Equal("new-order", row.LocalValue);
        Assert.Equal(
            "local-changed-unverified",
            Assert.Single(noLegacyFingerprint.Differences).State);
        Assert.False(diff.HasConflicts);
    }

    /// <summary>
    /// Verifies a schema 2 baseline never gets the unverified row: its own hidden fingerprint decides, and a change it
    /// cannot place reads as an ordinary local change.
    /// </summary>
    [Fact]
    public void Diff_ACurrentBaselineWithAnUnexplainedChange_NeverReportsAnUnverifiedLocalChange()
    {
        RemoteDeckSnapshot remote = ParseDeck();
        ArchidektSyncBaseline current = Baseline(remote, "local-before") with { LocalInvisibleFingerprint = "hidden" };

        ArchidektSyncDiff hiddenEdit = ArchidektSyncPlanner.Diff(
            Guid.NewGuid(), 2, "local-after", remote, remote, current, "hidden-after", "legacy-local");
        ArchidektSyncDiff unchanged = ArchidektSyncPlanner.Diff(
            Guid.NewGuid(), 2, "local-before", remote, remote, current, "hidden", "legacy-local");

        Assert.Equal("local-changed", Assert.Single(hiddenEdit.Differences).State);
        Assert.Empty(unchanged.Differences);
    }

    /// <summary>
    /// Verifies the unverified row is not added when another row already shows a local change on the old baseline,
    /// or when both sides made the same change.
    /// </summary>
    [Fact]
    public void Diff_AnEarlierVersionBaselineWithAnExplainingRow_AddsNoUnverifiedRow()
    {
        (RemoteDeckSnapshot before, RemoteDeckSnapshot after) = ConvergedPair();
        ArchidektSyncBaseline legacy = Baseline(before, "legacy-local") with { SchemaVersion = 1 };

        ArchidektSyncDiff converged = ArchidektSyncPlanner.Diff(
            Guid.NewGuid(), 2, "new-order", after, after, legacy, "hidden", "other-order");
        ArchidektSyncDiff localEdit = ArchidektSyncPlanner.Diff(
            Guid.NewGuid(), 2, "new-order", after, before, legacy, "hidden", "other-order");

        Assert.Contains(converged.Differences, value => value.State == "converged");
        Assert.DoesNotContain(converged.Differences, value => value.State == "local-changed-unverified");
        Assert.Contains(localEdit.Differences, value => value.State.StartsWith("local-", StringComparison.Ordinal));
        Assert.DoesNotContain(localEdit.Differences, value => value.State == "local-changed-unverified");
    }

    /// <summary>
    /// Verifies a baseline left by a verification mismatch reads as a local change even when every row matches.
    /// </summary>
    [Fact]
    public void Diff_AnUnsynchronizedBaselineWhoseRowsAllMatch_StillReportsALocalChange()
    {
        RemoteDeckSnapshot remote = ParseDeck();
        ArchidektSyncBaseline baseline = Baseline(remote, ArchidektLocalMapper.UnsynchronizedFingerprint);

        ArchidektSyncDiff diff = ArchidektSyncPlanner.Diff(
            Guid.NewGuid(), 2, "any-local-fingerprint", remote, remote, baseline);

        Assert.Contains(diff.Differences, value => value.Path == "/localOnly/content" && value.State == "local-changed");
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
            2,
            remote.RemoteId,
            remote.RemoteFingerprint,
            localFingerprint,
            remote,
            new Dictionary<Guid, string>(),
            new Dictionary<Guid, string>());
    }
}
