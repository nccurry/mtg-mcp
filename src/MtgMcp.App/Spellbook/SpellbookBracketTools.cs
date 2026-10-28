using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;
using MtgMcp.Spellbook;

namespace MtgMcp.App.Spellbook;

/// <summary>
/// Carries the Commander Spellbook flags of one card together with the local deck entries that hold it.
/// </summary>
internal sealed record SpellbookBracketCardView(
    string Name,
    Guid? OracleId,
    int Quantity,
    bool GameChanger,
    bool MassLandDenial,
    bool ExtraTurn,
    bool Banned,
    IReadOnlyList<Guid> EntryIds);

/// <summary>
/// Returns the bracket-related facts of one local deck. It names no bracket and carries no provider label.
/// </summary>
internal sealed record SpellbookBracketFactsResult(
    [property: JsonPropertyName("deck")] SpellbookDeckSelectionEvidence Deck,
    [property: JsonPropertyName("evidence")] SpellbookFactsEvidence Evidence,
    [property: JsonPropertyName("cards")] IReadOnlyList<SpellbookBracketCardView> Cards,
    [property: JsonPropertyName("combos")] IReadOnlyList<SpellbookBracketCombo> Combos)
{
    /// <summary>
    /// Gets how many cards without any flag the result leaves out to stay under the size limit, or null when it
    /// lists every card.
    /// </summary>
    [JsonPropertyName("unflaggedCardsOmitted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? UnflaggedCardsOmitted { get; init; }
}

/// <summary>
/// Exposes the bracket facts tool: factual flags and combo details for a local deck, and no bracket.
/// </summary>
internal sealed class SpellbookBracketTools
{
    /// <summary>
    /// Reads the deck and the entries to send.
    /// </summary>
    private readonly SpellbookDeckInputResolver resolver;

    /// <summary>
    /// Reads the deck again to find the entries that hold each card.
    /// </summary>
    private readonly SqliteDeckStore store;

    /// <summary>
    /// Sends the request to Commander Spellbook.
    /// </summary>
    private readonly SpellbookService service;

    /// <summary>
    /// Creates the tool around the resolver, the deck store, and the service.
    /// </summary>
    internal SpellbookBracketTools(
        SpellbookService service,
        SpellbookDeckInputResolver resolver,
        SqliteDeckStore store)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Reads Commander Spellbook's bracket-related facts for one local deck revision.
    /// </summary>
    [McpServerTool(
        Name = "spellbook_deck_bracket_facts_get",
        Title = "Get Commander Spellbook Bracket Facts",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description(
        "Commander Spellbook: sends only commander and main entries from one exact local deck revision to Commander " +
        "Spellbook and returns facts only: per card, whether it is a Game Changer, mass land denial, an extra-turn " +
        "card, or banned, with the deck entry IDs; and per combo found, its cards, whether it is definitely or " +
        "arguably a two-card combo, its speed, and the mana it needs. It never states a bracket or a verdict; the " +
        "provider's bracket label is removed. Use card_list_query with the tags field to count tutors." +
        UntrustedTextNotes.Spellbook +
        " Example: {\"deckId\":\"0193e97a-ef95-7700-8000-000000000001\",\"expectedRevision\":7}.")]
    internal async Task<OperationResult<SpellbookBracketFactsResult>> GetFactsAsync(
        [Description("Local deck identifier.")] Guid deckId,
        [Description("Exact local deck revision to read before the source request.")] long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        OperationResult<SpellbookDeckComboSelection> selection = await resolver.ResolveAsync(
            deckId,
            expectedRevision,
            cancellationToken).ConfigureAwait(false);
        if (selection is not OperationSuccess<SpellbookDeckComboSelection> chosen)
        {
            return OperationFailure.Forward<SpellbookDeckComboSelection, SpellbookBracketFactsResult>(selection);
        }

        OperationResult<DeckDocument> loaded = await store.GetAsync(deckId, cancellationToken).ConfigureAwait(false);
        if (loaded is not OperationSuccess<DeckDocument> deck)
        {
            return OperationFailure.Forward<DeckDocument, SpellbookBracketFactsResult>(loaded);
        }

        if (deck.Data.Revision != expectedRevision)
        {
            return new OperationConflict(
                "deck-revision-conflict",
                "The local deck revision changed; load it again before reading bracket facts.",
                new ConflictState(Revision: deck.Data.Revision));
        }

        OperationResult<SpellbookBracketFacts> facts = await service.EstimateBracketFactsAsync(
            chosen.Data.Request,
            cancellationToken).ConfigureAwait(false);
        if (facts is not OperationSuccess<SpellbookBracketFacts> success)
        {
            return OperationFailure.Forward<SpellbookBracketFacts, SpellbookBracketFactsResult>(facts);
        }

        return new OperationSuccess<SpellbookBracketFactsResult>(Present(chosen.Data.Evidence, success.Data,
            deck.Data));
    }

    /// <summary>
    /// Builds the result: each card with the entries that hold it, cut to the size limit by leaving out cards with no
    /// flag.
    /// </summary>
    private static SpellbookBracketFactsResult Present(
        SpellbookDeckSelectionEvidence evidence,
        SpellbookBracketFacts facts,
        DeckDocument deck)
    {
        List<SpellbookBracketCardView> cards =
        [
            .. facts.Cards.Select(card => new SpellbookBracketCardView(
                card.Name,
                card.OracleId,
                card.Quantity,
                card.GameChanger,
                card.MassLandDenial,
                card.ExtraTurn,
                card.Banned,
                [.. deck.Entries
                    .Where(entry => entry.Zone is "commander" or "main" &&
                        string.Equals(entry.CardName, card.Name, StringComparison.OrdinalIgnoreCase))
                    .Select(entry => entry.EntryId)])),
        ];
        SpellbookBracketFactsResult full = new(evidence, facts.Evidence, cards, facts.Combos);
        int bytes = JsonSerializer.SerializeToUtf8Bytes(full, ToolCallPolicy.SerializerOptions).Length;
        if (bytes <= ResultSizeLimit.MaximumBytes - ResultSizeLimit.EnvelopeReserveBytes)
        {
            return full;
        }

        List<SpellbookBracketCardView> flagged =
        [
            .. cards.Where(card => card.GameChanger || card.MassLandDenial || card.ExtraTurn || card.Banned),
        ];
        return full with { Cards = flagged, UnflaggedCardsOmitted = cards.Count - flagged.Count };
    }
}
