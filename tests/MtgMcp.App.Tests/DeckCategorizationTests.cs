using MtgMcp.App.Decks;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Tests;

/// <summary>Verifies the App composition boundary for deterministic categorization.</summary>
public sealed class DeckCategorizationTests
{
    /// <summary>Rejects a source that cannot bind an existing local category.</summary>
    [Fact]
    public async Task Validate_InvalidInlineCategory_ReturnsInvalidInput()
    {
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        using ScryfallService scryfall = new(temporary.Path, true, "0.9.0-preview.1");
        DeckCategorizationCoordinator coordinator = new(store, scryfall);
        DeckDocument deck = RequireSuccess(await store.CreateAsync(new DeckCreateRequest("Fixture"), TestContext.Current.CancellationToken));

        OperationResult<DeckCategoryRulesPreview> result = await coordinator.ValidateAsync(
            deck.DeckId,
            new InlineCategoryRuleSource(new CategoryRuleSet("add-only", [new CategoryRule(Guid.NewGuid(), [], [], [])])),
            "cache-only",
            TestContext.Current.CancellationToken);

        Assert.Equal("invalid-category-rule-category", Assert.IsType<OperationInvalidInput>(result.Value).ReasonCode);
    }

    /// <summary>Preserves an explicit not-cached outcome when local card evidence is absent.</summary>
    [Fact]
    public async Task Preview_CacheOnlyMissingCard_ReturnsIncompleteEvidence()
    {
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        using ScryfallService scryfall = new(temporary.Path, true, "0.9.0-preview.1");
        DeckCategorizationCoordinator coordinator = new(store, scryfall);
        Guid categoryId = Guid.NewGuid();
        DeckDocument deck = RequireSuccess(await store.CreateAsync(new DeckCreateRequest(
            "Fixture",
            Entries: [new DeckEntryDraft(1, "Missing Card")],
            Categories: [new DeckCategoryDraft("Ramp", CategoryId: categoryId)]), TestContext.Current.CancellationToken));

        OperationResult<DeckCategoryRulesPreview> result = await coordinator.PreviewAsync(
            deck.DeckId,
            deck.Revision,
            new InlineCategoryRuleSource(new CategoryRuleSet("add-only", [new CategoryRule(
                categoryId, [new CategoryTagSelector("oracle", ExactSlug: "ramp")], [], [])])),
            "cache-only",
            TestContext.Current.CancellationToken);

        DeckCategoryRulesPreview preview = RequireSuccess(result);
        Assert.False(preview.IsComplete);
        Assert.Contains(preview.Decisions, value => value.Status == "unknown");
    }

    /// <summary>Rejects malformed preset role and duplicate binding requests.</summary>
    [Fact]
    public async Task Validate_InvalidPresetBindings_ReturnsInvalidInput()
    {
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        using ScryfallService scryfall = new(temporary.Path, true, "0.9.0-preview.1");
        DeckCategorizationCoordinator coordinator = new(store, scryfall);
        Guid categoryId = Guid.NewGuid();
        DeckDocument deck = RequireSuccess(await store.CreateAsync(new DeckCreateRequest(
            "Fixture", Categories: [new DeckCategoryDraft("Ramp", CategoryId: categoryId)]), TestContext.Current.CancellationToken));
        CommonPresetCategoryRuleSource source = new(
            "common-v1",
            "add-only",
            [new CategoryRoleBinding("unknown", categoryId), new CategoryRoleBinding("unknown", categoryId)]);

        OperationResult<DeckCategoryRulesPreview> result = await coordinator.ValidateAsync(
            deck.DeckId, source, "cache-only", TestContext.Current.CancellationToken);

        Assert.Equal("invalid-category-preset", Assert.IsType<OperationInvalidInput>(result.Value).ReasonCode);
    }

    /// <summary>Rejects a tampered apply token before reading or mutating the deck.</summary>
    [Fact]
    public async Task Apply_TamperedToken_ReturnsInvalidInput()
    {
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        using ScryfallService scryfall = new(temporary.Path, true, "0.9.0-preview.1");
        DeckCategorizationCoordinator coordinator = new(store, scryfall);
        DeckDocument deck = RequireSuccess(await store.CreateAsync(new DeckCreateRequest("Fixture"), TestContext.Current.CancellationToken));
        CategoryRuleSource source = new InlineCategoryRuleSource(new CategoryRuleSet("add-only", []));

        OperationResult<DeckDocument> result = await coordinator.ApplyAsync(
            deck.DeckId, deck.Revision, null, source, "cache-only", "fingerprint", "tampered", TestContext.Current.CancellationToken);

        Assert.Equal("invalid-category-apply-token", Assert.IsType<OperationInvalidInput>(result.Value).ReasonCode);
    }

    /// <summary>Builds a complete deterministic preview for a deck with no entries.</summary>
    [Fact]
    public async Task Preview_EmptyDeck_ReturnsCompleteFingerprint()
    {
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        using ScryfallService scryfall = new(temporary.Path, true, "0.9.0-preview.1");
        DeckCategorizationCoordinator coordinator = new(store, scryfall);
        Guid categoryId = Guid.NewGuid();
        DeckDocument deck = RequireSuccess(await store.CreateAsync(new DeckCreateRequest(
            "Fixture", Categories: [new DeckCategoryDraft("Ramp", CategoryId: categoryId)]), TestContext.Current.CancellationToken));

        DeckCategoryRulesPreview preview = RequireSuccess(await coordinator.PreviewAsync(
            deck.DeckId,
            deck.Revision,
            new InlineCategoryRuleSource(new CategoryRuleSet("add-only", [new CategoryRule(categoryId, [], [], [])])),
            "cache-only",
            TestContext.Current.CancellationToken));

        Assert.True(preview.IsComplete);
        Assert.NotEmpty(preview.PreviewFingerprint);
        Assert.NotEmpty(preview.ApplyToken);
    }

    /// <summary>Rejects assignment modes outside the closed categorization contract.</summary>
    [Fact]
    public async Task Validate_InvalidAssignmentMode_ReturnsInvalidInput()
    {
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        using ScryfallService scryfall = new(temporary.Path, true, "0.9.0-preview.1");
        DeckCategorizationCoordinator coordinator = new(store, scryfall);
        Guid categoryId = Guid.NewGuid();
        DeckDocument deck = RequireSuccess(await store.CreateAsync(new DeckCreateRequest(
            "Fixture", Categories: [new DeckCategoryDraft("Ramp", CategoryId: categoryId)]), TestContext.Current.CancellationToken));

        OperationResult<DeckCategoryRulesPreview> result = await coordinator.ValidateAsync(
            deck.DeckId,
            new InlineCategoryRuleSource(new CategoryRuleSet("replace-all", [new CategoryRule(categoryId, [], [], [])])),
            "cache-only",
            TestContext.Current.CancellationToken);

        Assert.Equal("invalid-category-rules", Assert.IsType<OperationInvalidInput>(result.Value).ReasonCode);
    }

    /// <summary>Extracts successful data for concise fixture assertions.</summary>
    private static T RequireSuccess<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }
}
