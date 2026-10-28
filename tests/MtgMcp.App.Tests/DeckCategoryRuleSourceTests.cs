using MtgMcp.App.Decks;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies deck categorization uses exact installed Scryfall source tags without changing its MCP surface.
/// </summary>
public sealed class DeckCategoryRuleSourceTests
{
    /// <summary>
    /// Resolves parent selectors to exact source IDs, and inline rules for the four common roles resolve to the
    /// reviewed tags.
    /// </summary>
    [Fact]
    public async Task Preview_UsesExactSourceIdsForInlineRoleRules()
    {
        using TemporaryDirectory temporary = new();
        DeckCategorizationSourceFixture.DeckCategorizationSourceHandler handler =
            DeckCategorizationSourceFixture.CreateHandler();
        using ScryfallService scryfall = DeckCategorizationSourceFixture.CreateService(temporary.Path, handler);
        ScryfallCorpusSyncResult sync = await InstallAsync(scryfall);
        int requestsAfterInstall = handler.RequestCount;
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        DeckCategorizationCoordinator coordinator = new(store, scryfall);
        Guid entryId = Guid.CreateVersion7();
        Guid rampCategory = Guid.CreateVersion7();
        Guid directOnlyCategory = Guid.CreateVersion7();
        Guid drawCategory = Guid.CreateVersion7();
        Guid removalCategory = Guid.CreateVersion7();
        Guid recursionCategory = Guid.CreateVersion7();
        DeckDocument deck = RequireSuccess(await store.CreateAsync(
            new DeckCreateRequest(
                "Fixture",
                Entries: [new DeckEntryDraft(
                    1,
                    "Fixture Ramp Card",
                    OracleId: DeckCategorizationSourceFixture.OracleId,
                    EntryId: entryId)],
                Categories:
                [
                    new DeckCategoryDraft("Ramp", CategoryId: rampCategory),
                    new DeckCategoryDraft("Direct Only", CategoryId: directOnlyCategory),
                    new DeckCategoryDraft("Draw", CategoryId: drawCategory),
                    new DeckCategoryDraft("Removal", CategoryId: removalCategory),
                    new DeckCategoryDraft("Recursion", CategoryId: recursionCategory),
                ]),
            TestContext.Current.CancellationToken));

        DeckCategoryRulesPreview parentPreview = RequireSuccess(await coordinator.PreviewAsync(
            deck.DeckId,
            deck.Revision,
            new InlineCategoryRuleSource(new CategoryRuleSet(
                "add-only",
                [
                    new CategoryRule(
                        rampCategory,
                        [new CategoryTagSelector("oracle", ExactSlug: "ramp", IncludeDescendants: true)],
                        [],
                        []),
                    new CategoryRule(
                        directOnlyCategory,
                        [new CategoryTagSelector("oracle", ExactSlug: "ramp")],
                        [],
                        []),
                ])),
            "cache-only",
            TestContext.Current.CancellationToken));
        Assert.Equal(sync.GenerationId, parentPreview.CorpusGenerationId);
        CategoryTagSelector resolvedParent = parentPreview.ExpandedRules.Rules[0].AllOf.Single();
        Assert.Equal(DeckCategorizationSourceFixture.RampTagId, resolvedParent.TagId);
        Assert.Null(resolvedParent.ExactSlug);
        Assert.True(resolvedParent.IncludeDescendants);
        Assert.Equal(
            "matched",
            Assert.Single(parentPreview.Decisions, value => value.CategoryId == rampCategory).Status);
        Assert.Equal(
            "unmatched",
            Assert.Single(parentPreview.Decisions, value => value.CategoryId == directOnlyCategory).Status);

        InlineCategoryRuleSource roleRules = new(new CategoryRuleSet(
            "add-only",
            [
                new CategoryRule(rampCategory, [SourceSelector(DeckCategorizationSourceFixture.RampTagId)], [], []),
                new CategoryRule(drawCategory, [SourceSelector(DeckCategorizationSourceFixture.DrawTagId)], [], []),
                new CategoryRule(removalCategory, [SourceSelector(DeckCategorizationSourceFixture.RemovalTagId)], [], []),
                new CategoryRule(recursionCategory, [SourceSelector(DeckCategorizationSourceFixture.RecursionTagId)], [], []),
            ]));
        DeckCategoryRulesPreview rolePreview = RequireSuccess(await coordinator.PreviewAsync(
            deck.DeckId,
            deck.Revision,
            roleRules,
            "default",
            TestContext.Current.CancellationToken));

        Assert.Equal(
            [
                DeckCategorizationSourceFixture.RampTagId,
                DeckCategorizationSourceFixture.DrawTagId,
                DeckCategorizationSourceFixture.RemovalTagId,
                DeckCategorizationSourceFixture.RecursionTagId,
            ],
            rolePreview.ExpandedRules.Rules.Select(rule => rule.AllOf.Single().TagId!.Value));
        Assert.All(rolePreview.ExpandedRules.Rules, rule => Assert.True(rule.AllOf.Single().IncludeDescendants));
        // The reviewed result of the removed common-v1 preset at c4ed454 for this fixture: the test there proved the
        // preset gave the same expanded rules and decisions as these inline rules.
        Assert.Equal(
            new[]
            {
                (entryId, rampCategory, "matched", DeckCategorizationSourceFixture.RampChildTagId.ToString()),
                (entryId, drawCategory, "unmatched", string.Empty),
                (entryId, removalCategory, "unmatched", string.Empty),
                (entryId, recursionCategory, "unmatched", string.Empty),
                        }.OrderBy(value => value.Item2),
            rolePreview.Decisions.Select(decision => (
                decision.EntryId,
                decision.CategoryId,
                decision.Status,
                string.Join(',', decision.MatchedTagIds))).OrderBy(value => value.CategoryId));
        Assert.Equal(
            new[]
            {
                (rampCategory, DeckCategorizationSourceFixture.RampTagId, true),
                (drawCategory, DeckCategorizationSourceFixture.DrawTagId, true),
                (removalCategory, DeckCategorizationSourceFixture.RemovalTagId, true),
                (recursionCategory, DeckCategorizationSourceFixture.RecursionTagId, true),
                        }.OrderBy(value => value.Item1),
            rolePreview.ExpandedRules.Rules.Select(rule => (
                rule.CategoryId,
                rule.AllOf.Single().TagId!.Value,
                rule.AllOf.Single().IncludeDescendants)).OrderBy(value => value.CategoryId));
        Assert.True(rolePreview.IsComplete);
        Assert.Equal(requestsAfterInstall, handler.RequestCount);
    }

    /// <summary>
    /// Verifies a preview made without a revision gives a token that applies, writes the assignments, and then
    /// conflicts once the deck has changed.
    /// </summary>
    [Fact]
    public async Task Apply_TokenFromPreviewWithoutRevision_AppliesOnceThenConflictsAfterAnEdit()
    {
        using TemporaryDirectory temporary = new();
        DeckCategorizationSourceFixture.DeckCategorizationSourceHandler handler =
            DeckCategorizationSourceFixture.CreateHandler();
        using ScryfallService scryfall = DeckCategorizationSourceFixture.CreateService(temporary.Path, handler);
        ScryfallCorpusSyncResult sync = await InstallAsync(scryfall);
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        DeckCategorizationCoordinator coordinator = new(store, scryfall);
        Guid entryId = Guid.CreateVersion7();
        Guid categoryId = Guid.CreateVersion7();
        DeckDocument deck = RequireSuccess(await store.CreateAsync(
            new DeckCreateRequest(
                "Fixture",
                Entries: [new DeckEntryDraft(
                    1,
                    "Fixture Ramp Card",
                    OracleId: DeckCategorizationSourceFixture.OracleId,
                    EntryId: entryId)],
                Categories: [new DeckCategoryDraft("Ramp", CategoryId: categoryId)]),
            TestContext.Current.CancellationToken));
        InlineCategoryRuleSource source = new(new CategoryRuleSet(
            "add-only",
            [new CategoryRule(categoryId, [SourceSelector(DeckCategorizationSourceFixture.RampTagId)], [], [])]));
        DeckCategoryRulesPreview preview = RequireSuccess(await coordinator.PreviewAsync(
            deck.DeckId,
            null,
            source,
            "cache-only",
            TestContext.Current.CancellationToken));

        DeckDocument applied = RequireSuccess(await coordinator.ApplyAsync(
            deck.DeckId,
            deck.Revision,
            sync.GenerationId,
            source,
            "cache-only",
            preview.PreviewFingerprint,
            preview.ApplyToken,
            TestContext.Current.CancellationToken));
        DeckDocument edited = RequireSuccess(await store.ApplyChangesAsync(
            deck.DeckId,
            applied.Revision,
            [new UpdateDeckMetadataChange("Edited", null, "custom")],
            TestContext.Current.CancellationToken));
        OperationResult<DeckDocument> stale = await coordinator.ApplyAsync(
            deck.DeckId,
            deck.Revision,
            sync.GenerationId,
            source,
            "cache-only",
            preview.PreviewFingerprint,
            preview.ApplyToken,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            new DeckCategoryAssignment(entryId, categoryId, false),
            Assert.Single(applied.CategoryAssignments));
        Assert.Equal(applied.Revision + 1, edited.Revision);
        Assert.Equal("deck-revision-conflict", Assert.IsType<OperationConflict>(stale.Value).ReasonCode);
    }

    /// <summary>
    /// Verifies a source that is not inline is refused as invalid input at the source, before any provider call.
    /// </summary>
    [Fact]
    public async Task Resolve_SourceThatIsNotInline_ReturnsInvalidInputWithoutThrowing()
    {
        using TemporaryDirectory temporary = new();
        DeckCategorizationSourceFixture.DeckCategorizationSourceHandler handler =
            DeckCategorizationSourceFixture.CreateHandler();
        using ScryfallService scryfall = DeckCategorizationSourceFixture.CreateService(temporary.Path, handler);
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        DeckDocument deck = RequireSuccess(await store.CreateAsync(
            new DeckCreateRequest("Fixture"),
            TestContext.Current.CancellationToken));
        DeckCategoryRuleResolver resolver = new(scryfall);

        OperationResult<ResolvedDeckCategoryRules> result = await resolver.ResolveAsync(
            new OtherSource(),
            deck,
            "default",
            TestContext.Current.CancellationToken);

        OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>(result.Value);
        Assert.Equal(("invalid-category-rule-source", "source"), (invalid.ReasonCode, invalid.Path));
        Assert.Equal(0, handler.RequestCount);
    }

    /// <summary>
    /// Stands in for a source kind that the resolver does not know.
    /// </summary>
    private sealed record OtherSource : CategoryRuleSource;

    /// <summary>
    /// Stops a synchronize preview before a missing source selector can produce a destructive change.
    /// </summary>
    [Fact]
    public async Task Preview_MissingSourceTagLeavesExistingAssignmentsUntouched()
    {
        using TemporaryDirectory temporary = new();
        DeckCategorizationSourceFixture.DeckCategorizationSourceHandler handler =
            DeckCategorizationSourceFixture.CreateHandler();
        using ScryfallService scryfall = DeckCategorizationSourceFixture.CreateService(temporary.Path, handler);
        _ = await InstallAsync(scryfall);
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        DeckCategorizationCoordinator coordinator = new(store, scryfall);
        Guid entryId = Guid.CreateVersion7();
        Guid categoryId = Guid.CreateVersion7();
        DeckDocument deck = RequireSuccess(await store.CreateAsync(
            new DeckCreateRequest(
                "Fixture",
                Entries: [new DeckEntryDraft(
                    1,
                    "Fixture Ramp Card",
                    OracleId: DeckCategorizationSourceFixture.OracleId,
                    EntryId: entryId)],
                Categories: [new DeckCategoryDraft("Ramp", CategoryId: categoryId)],
                CategoryAssignments: [new DeckCategoryAssignment(entryId, categoryId, false)]),
            TestContext.Current.CancellationToken));

        OperationResult<DeckCategoryRulesPreview> result = await coordinator.PreviewAsync(
            deck.DeckId,
            deck.Revision,
            new InlineCategoryRuleSource(new CategoryRuleSet(
                "synchronize-listed-categories",
                [new CategoryRule(
                    categoryId,
                    [new CategoryTagSelector("oracle", ExactSlug: "missing-source-tag")],
                    [],
                    [])])),
            "cache-only",
            TestContext.Current.CancellationToken);

        Assert.Equal("scryfall-tag-not-found", Assert.IsType<OperationNotFound>(result.Value).ReasonCode);
        DeckDocument unchanged = RequireSuccess(await store.GetAsync(deck.DeckId, TestContext.Current.CancellationToken));
        Assert.Equal([new DeckCategoryAssignment(entryId, categoryId, false)], unchanged.CategoryAssignments);
    }

    /// <summary>
    /// Installs the deterministic Scryfall-format source fixture.
    /// </summary>
    private static async Task<ScryfallCorpusSyncResult> InstallAsync(ScryfallService service)
    {
        return RequireSuccess(await service.SyncCorpusAsync(
            "refresh",
            cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Creates one exact source tag selector that includes direct child tags.
    /// </summary>
    private static CategoryTagSelector SourceSelector(Guid tagId)
    {
        return new CategoryTagSelector("oracle", tagId, IncludeDescendants: true);
    }

    /// <summary>
    /// Extracts successful data for concise fixture assertions.
    /// </summary>
    private static T RequireSuccess<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }
}
