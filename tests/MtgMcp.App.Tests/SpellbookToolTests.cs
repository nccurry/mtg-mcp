using MtgMcp.App.Spellbook;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;
using MtgMcp.Spellbook;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies local deck selection and no-decision input handling for Commander Spellbook tools.
/// </summary>
public sealed class SpellbookToolTests
{
    /// <summary>
    /// Verifies supported zones are grouped and sent exactly while other zones remain visible as skipped.
    /// </summary>
    [Fact]
    public async Task DeckResolver_GroupsSupportedZonesAndReportsSkippedEntries()
    {
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        DeckDocument deck = RequireSuccess(await store.CreateAsync(
            new DeckCreateRequest(
                "Spellbook selection",
                Entries:
                [
                    new DeckEntryDraft(1, "Commander Card", Zone: "commander"),
                    new DeckEntryDraft(1, "Main Card", Zone: "main"),
                    new DeckEntryDraft(2, "Main Card", Zone: "main"),
                    new DeckEntryDraft(1, "Sideboard Card", Zone: "sideboard"),
                ]),
            TestContext.Current.CancellationToken));
        SpellbookDeckInputResolver resolver = new(store);

        SpellbookDeckComboSelection selection = RequireSuccess(await resolver.ResolveAsync(
            deck.DeckId,
            deck.Revision,
            TestContext.Current.CancellationToken));

        Assert.Equal(
            [new SpellbookDeckEntry("Commander Card", 1)],
            selection.Request.Commanders);
        Assert.Equal(
            [new SpellbookDeckEntry("Main Card", 3)],
            selection.Request.Main);
        Assert.Equal(
            [
                new SpellbookSentDeckEntry("commander", "Commander Card", 1),
                new SpellbookSentDeckEntry("main", "Main Card", 3),
            ],
            selection.Evidence.SentEntries);
        SpellbookSkippedDeckEntry skipped = Assert.Single(selection.Evidence.SkippedEntries);
        Assert.Equal("sideboard", skipped.Zone);
        Assert.Equal("Sideboard Card", skipped.CardName);
        Assert.Equal(1, skipped.Quantity);
    }

    /// <summary>
    /// Verifies stale, missing, and unsupported-zone deck selections fail before a source request starts.
    /// </summary>
    [Fact]
    public async Task DeckResolver_InvalidDeckSelectionsRemainStructured()
    {
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        DeckDocument sideboardOnly = RequireSuccess(await store.CreateAsync(
            new DeckCreateRequest(
                "Skipped only",
                Entries: [new DeckEntryDraft(1, "Sideboard Card", Zone: "sideboard")]),
            TestContext.Current.CancellationToken));
        SpellbookDeckInputResolver resolver = new(store);

        OperationResult<SpellbookDeckComboSelection> stale = await resolver.ResolveAsync(
            sideboardOnly.DeckId,
            sideboardOnly.Revision + 1,
            TestContext.Current.CancellationToken);
        OperationResult<SpellbookDeckComboSelection> empty = await resolver.ResolveAsync(
            sideboardOnly.DeckId,
            sideboardOnly.Revision,
            TestContext.Current.CancellationToken);
        OperationResult<SpellbookDeckComboSelection> missing = await resolver.ResolveAsync(
            Guid.CreateVersion7(),
            1,
            TestContext.Current.CancellationToken);

        Assert.Equal("deck-revision-conflict", Assert.IsType<OperationConflict>(stale.Value).ReasonCode);
        Assert.Equal(
            "invalid-spellbook-deck-selection",
            Assert.IsType<OperationInvalidInput>(empty.Value).ReasonCode);
        Assert.Equal("deck-not-found", Assert.IsType<OperationNotFound>(missing.Value).ReasonCode);
    }

    /// <summary>
    /// Verifies the source's fixed commander-row limit is checked from the local selection.
    /// </summary>
    [Fact]
    public async Task DeckResolver_TooManyCommanderEntries_FailsBeforeSourceWork()
    {
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        DeckEntryDraft[] entries = Enumerable.Range(0, SpellbookDeckRequestLimits.MaximumCommanderEntries + 1)
            .Select(index => new DeckEntryDraft(1, $"Commander {index}", Zone: "commander"))
            .ToArray();
        DeckDocument deck = RequireSuccess(await store.CreateAsync(
            new DeckCreateRequest("Too many commanders", Entries: entries),
            TestContext.Current.CancellationToken));
        SpellbookDeckInputResolver resolver = new(store);

        OperationResult<SpellbookDeckComboSelection> result = await resolver.ResolveAsync(
            deck.DeckId,
            deck.Revision,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "invalid-spellbook-deck-selection",
            Assert.IsType<OperationInvalidInput>(result.Value).ReasonCode);
    }

    /// <summary>
    /// Verifies the source's fixed main-deck row limit is checked from the local selection.
    /// </summary>
    [Fact]
    public async Task DeckResolver_TooManyMainEntries_FailsBeforeSourceWork()
    {
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        DeckEntryDraft[] entries = Enumerable.Range(0, SpellbookDeckRequestLimits.MaximumMainEntries + 1)
            .Select(index => new DeckEntryDraft(1, $"Main {index}", Zone: "main"))
            .ToArray();
        DeckDocument deck = RequireSuccess(await store.CreateAsync(
            new DeckCreateRequest("Too many main entries", Entries: entries),
            TestContext.Current.CancellationToken));
        SpellbookDeckInputResolver resolver = new(store);

        OperationResult<SpellbookDeckComboSelection> result = await resolver.ResolveAsync(
            deck.DeckId,
            deck.Revision,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "invalid-spellbook-deck-selection",
            Assert.IsType<OperationInvalidInput>(result.Value).ReasonCode);
    }

    /// <summary>
    /// Verifies local source-entry validation rejects a long card name and a grouped quantity overflow.
    /// </summary>
    [Fact]
    public async Task DeckResolver_InvalidSourceEntryLimits_FailBeforeSourceWork()
    {
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        DeckDocument longName = RequireSuccess(await store.CreateAsync(
            new DeckCreateRequest(
                "Long card name",
                Entries:
                [
                    new DeckEntryDraft(
                        1,
                        new string('x', SpellbookDeckRequestLimits.MaximumCardNameLength + 1),
                        Zone: "main"),
                ]),
            TestContext.Current.CancellationToken));
        DeckDocument overflow = RequireSuccess(await store.CreateAsync(
            new DeckCreateRequest(
                "Quantity overflow",
                Entries:
                [
                    new DeckEntryDraft(int.MaxValue, "Repeated card", Zone: "main"),
                    new DeckEntryDraft(1, "Repeated card", Zone: "main"),
                ]),
            TestContext.Current.CancellationToken));
        SpellbookDeckInputResolver resolver = new(store);

        OperationResult<SpellbookDeckComboSelection> longNameResult = await resolver.ResolveAsync(
            longName.DeckId,
            longName.Revision,
            TestContext.Current.CancellationToken);
        OperationResult<SpellbookDeckComboSelection> overflowResult = await resolver.ResolveAsync(
            overflow.DeckId,
            overflow.Revision,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "invalid-spellbook-deck-selection",
            Assert.IsType<OperationInvalidInput>(longNameResult.Value).ReasonCode);
        Assert.Equal(
            "invalid-spellbook-deck-selection",
            Assert.IsType<OperationInvalidInput>(overflowResult.Value).ReasonCode);
    }

    /// <summary>
    /// Verifies invalid public inputs fail locally rather than calling Commander Spellbook.
    /// </summary>
    [Fact]
    public async Task ReadTools_InvalidInputs_ReturnStructuredFailuresWithoutSourceWork()
    {
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        using SpellbookService service = new(
            SpellbookOptions.CreateDefault(temporary.Path),
            "0.9.0-preview.1");
        SpellbookReadTools tools = new(service, new SpellbookDeckInputResolver(store));

        OperationResult<SpellbookEvidence> search = await tools.SearchVariantsAsync(
            string.Empty,
            cancellationToken: TestContext.Current.CancellationToken);
        OperationResult<SpellbookEvidence> variant = await tools.GetVariantAsync(
            string.Empty,
            TestContext.Current.CancellationToken);
        OperationResult<SpellbookDeckComboEvidence> deck = await tools.FindDeckCombosAsync(
            Guid.Empty,
            0,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("invalid-source-query", Assert.IsType<OperationInvalidInput>(search.Value).ReasonCode);
        Assert.Equal("invalid-variant-id", Assert.IsType<OperationInvalidInput>(variant.Value).ReasonCode);
        Assert.Equal("invalid-spellbook-deck-selection", Assert.IsType<OperationInvalidInput>(deck.Value).ReasonCode);
        Assert.False(File.Exists(Path.Combine(temporary.Path, "spellbook.db")));
    }

    /// <summary>
    /// Extracts successful data while preserving useful failure output.
    /// </summary>
    private static T RequireSuccess<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }
}
