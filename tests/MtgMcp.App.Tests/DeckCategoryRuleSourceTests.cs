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
    /// Resolves parent selectors to exact source IDs and preserves the common-v1 role mapping in place.
    /// </summary>
    [Fact]
    public async Task Preview_UsesExactSourceIdsAndCommonPresetRoles()
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

        CommonPresetCategoryRuleSource preset = new(
            "common-v1",
            "add-only",
            [
                new CategoryRoleBinding("ramp", rampCategory),
                new CategoryRoleBinding("card-draw", drawCategory),
                new CategoryRoleBinding("removal", removalCategory),
                new CategoryRoleBinding("recursion", recursionCategory),
            ]);
        InlineCategoryRuleSource equivalentInline = new(new CategoryRuleSet(
            "add-only",
            [
                new CategoryRule(rampCategory, [SourceSelector(DeckCategorizationSourceFixture.RampTagId)], [], []),
                new CategoryRule(drawCategory, [SourceSelector(DeckCategorizationSourceFixture.DrawTagId)], [], []),
                new CategoryRule(removalCategory, [SourceSelector(DeckCategorizationSourceFixture.RemovalTagId)], [], []),
                new CategoryRule(recursionCategory, [SourceSelector(DeckCategorizationSourceFixture.RecursionTagId)], [], []),
            ]));
        DeckCategoryRulesPreview presetPreview = RequireSuccess(await coordinator.PreviewAsync(
            deck.DeckId,
            deck.Revision,
            preset,
            "default",
            TestContext.Current.CancellationToken));
        DeckCategoryRulesPreview inlinePreview = RequireSuccess(await coordinator.PreviewAsync(
            deck.DeckId,
            deck.Revision,
            equivalentInline,
            "default",
            TestContext.Current.CancellationToken));

        Assert.Equal(1, presetPreview.PresetSchemaVersion);
        Assert.NotEqual("common-v1-ramp-draw-removal-recursion", presetPreview.PresetChecksum);
        Assert.Equal(
            presetPreview.ExpandedRules.Rules.Select(rule => (
                rule.CategoryId,
                rule.AllOf.Single().TagId,
                rule.AllOf.Single().IncludeDescendants)),
            inlinePreview.ExpandedRules.Rules.Select(rule => (
                rule.CategoryId,
                rule.AllOf.Single().TagId,
                rule.AllOf.Single().IncludeDescendants)));
        Assert.Equal(
            presetPreview.Decisions.Select(decision => (
                decision.EntryId,
                decision.CategoryId,
                decision.Status,
                string.Join(',', decision.MatchedTagIds))),
            inlinePreview.Decisions.Select(decision => (
                decision.EntryId,
                decision.CategoryId,
                decision.Status,
                string.Join(',', decision.MatchedTagIds))));
        Assert.Equal(requestsAfterInstall, handler.RequestCount);
    }

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
