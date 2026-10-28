using System.Text.Json.Serialization;
using MtgMcp.App.CardLists;
using MtgMcp.App.Scryfall;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Decks;

/// <summary>
/// Carries the structural validation of a deck as <c>local_deck_validate</c> returns it. With a rules value it also
/// carries the rule facts; without one its JSON matches the plain structural report.
/// </summary>
internal sealed record DeckValidationView(
    Guid DeckId,
    long Revision,
    bool IsStructurallyValid,
    IReadOnlyList<DeckValidationIssue> Issues)
{
    /// <summary>
    /// Gets the Commander rule facts, or null when the caller asked for none.
    /// </summary>
    [JsonPropertyName("rules")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CommanderRuleFacts? Rules { get; init; }
}

/// <summary>
/// Builds the <c>local_deck_validate</c> result: structural checks, and with <c>rules: "commander"</c>, the Commander
/// rule facts worked out from installed Scryfall card data.
/// </summary>
internal sealed class DeckRuleCoordinator
{
    /// <summary>
    /// Names the rule set that is available.
    /// </summary>
    internal const string CommanderRuleSet = "commander";

    /// <summary>
    /// Reads stored decks.
    /// </summary>
    private readonly SqliteDeckStore store;

    /// <summary>
    /// Reads installed card data.
    /// </summary>
    private readonly ScryfallService scryfall;

    /// <summary>
    /// Creates the coordinator around the deck store and the Scryfall service.
    /// </summary>
    internal DeckRuleCoordinator(SqliteDeckStore store, ScryfallService scryfall)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(scryfall);
        this.store = store;
        this.scryfall = scryfall;
    }

    /// <summary>
    /// Validates one deck, and with a rules value adds the rule facts.
    /// </summary>
    internal async Task<OperationResult<DeckValidationView>> ValidateAsync(
        Guid deckId,
        string? rules,
        CancellationToken cancellationToken)
    {
        if (rules is not null && rules != CommanderRuleSet)
        {
            return new OperationInvalidInput(
                "invalid-rules",
                $"rules must be {CommanderRuleSet}, or omitted for structural checks only.",
                "rules");
        }

        OperationResult<DeckValidationReport> report = await store.ValidateAsync(deckId, cancellationToken)
            .ConfigureAwait(false);
        if (report is not OperationSuccess<DeckValidationReport> structural)
        {
            return OperationFailure.Forward<DeckValidationReport, DeckValidationView>(report);
        }

        DeckValidationView view = new(
            structural.Data.DeckId,
            structural.Data.Revision,
            structural.Data.IsStructurallyValid,
            structural.Data.Issues);
        if (rules is null)
        {
            return new OperationSuccess<DeckValidationView>(view);
        }

        OperationResult<DeckDocument> loaded = await store.GetAsync(deckId, cancellationToken).ConfigureAwait(false);
        if (loaded is not OperationSuccess<DeckDocument> deck)
        {
            return OperationFailure.Forward<DeckDocument, DeckValidationView>(loaded);
        }

        OperationResult<ScryfallInstalledListCards> cards = await ScryfallToolExecution.RunAsync(() =>
            scryfall.ReadInstalledListCardsAsync(
                [.. deck.Data.Entries.Select(entry => CardListResolver.LookupFor(
                    entry.PrintingId, entry.SetCode, entry.CollectorNumber, entry.OracleId))],
                includeRaw: false,
                includeFacts: false,
                cancellationToken)).ConfigureAwait(false);
        if (cards is not OperationSuccess<ScryfallInstalledListCards> read)
        {
            return OperationFailure.Forward<ScryfallInstalledListCards, DeckValidationView>(cards);
        }

        if (!read.Data.Installed)
        {
            return new OperationNotCached(
                "card-data-not-installed",
                "Scryfall card data is not installed, so the rule facts cannot be worked out. Run " +
                "scryfall_card_data_sync to install it, then repeat the request.");
        }

        List<RuleEntry> entries = [];
        for (int index = 0; index < deck.Data.Entries.Count; index++)
        {
            DeckEntry entry = deck.Data.Entries[index];
            ScryfallCard? card = read.Data.Cards[index]?.Card;
            string? reason = card is not null
                ? null
                : CardListResolver.LookupFor(entry.PrintingId, entry.SetCode, entry.CollectorNumber,
                    entry.OracleId) is null
                    ? "no-scryfall-identity"
                    : "not-in-card-data";
            entries.Add(new RuleEntry(entry, card, reason));
        }

        return new OperationSuccess<DeckValidationView>(view with { Rules = CommanderRules.Evaluate(entries) });
    }

}
