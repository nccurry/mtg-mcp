using System.ComponentModel;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Scryfall;
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

    /// <summary>
    /// Gets how many combos the result leaves out to stay under the size limit, or null when it lists every combo.
    /// </summary>
    [JsonPropertyName("combosOmitted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CombosOmitted { get; init; }

    /// <summary>
    /// Gets true when cards or combos were left out to stay under the size limit.
    /// </summary>
    [JsonPropertyName("truncated")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Truncated { get; init; }

    /// <summary>
    /// Gets what to change to see what was left out, or null when nothing was.
    /// </summary>
    [JsonPropertyName("narrow")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Narrow { get; init; }
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
    /// Sends the request to Commander Spellbook.
    /// </summary>
    private readonly SpellbookService service;

    /// <summary>
    /// Creates the tool around the resolver and the service.
    /// </summary>
    internal SpellbookBracketTools(SpellbookService service, SpellbookDeckInputResolver resolver)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
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
        "provider's bracket label is removed. When the scryfall toolset is on, use card_list_query with the tags " +
        "field to count tutors." +
        UntrustedTextNotes.Spellbook +
        " Example: {\"deckId\":\"0193e97a-ef95-7700-8000-000000000001\",\"expectedRevision\":7}.")]
    internal async Task<OperationResult<SpellbookBracketFactsResult>> GetFactsAsync(
        [Description("Local deck identifier.")] Guid deckId,
        [Description("Exact local deck revision to read before the source request.")] long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        OperationResult<SpellbookResolvedDeck> resolved = await resolver.ResolveWithDeckAsync(
            deckId,
            expectedRevision,
            cancellationToken).ConfigureAwait(false);
        if (resolved is not OperationSuccess<SpellbookResolvedDeck> chosen)
        {
            return OperationFailure.Forward<SpellbookResolvedDeck, SpellbookBracketFactsResult>(resolved);
        }

        OperationResult<SpellbookBracketFacts> facts = await service.EstimateBracketFactsAsync(
            chosen.Data.Selection.Request,
            cancellationToken).ConfigureAwait(false);
        if (facts is not OperationSuccess<SpellbookBracketFacts> success)
        {
            return OperationFailure.Forward<SpellbookBracketFacts, SpellbookBracketFactsResult>(facts);
        }

        return new OperationSuccess<SpellbookBracketFactsResult>(Present(
            chosen.Data.Selection.Evidence,
            success.Data,
            chosen.Data.Deck));
    }

    /// <summary>
    /// Builds the result: each card with the entries that hold it, cut to the size limit. Cards with no flag go first,
    /// then combos from the end of the list, and the size is measured again after each cut.
    /// </summary>
    private static SpellbookBracketFactsResult Present(
        SpellbookDeckSelectionEvidence evidence,
        SpellbookBracketFacts facts,
        DeckDocument deck)
    {
        List<DeckEntry> sent = [.. deck.Entries.Where(entry => entry.Zone is "commander" or "main")];
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
                [.. sent.Where(entry => HoldsCard(entry, card)).Select(entry => entry.EntryId)])),
        ];
        SpellbookBracketFactsResult full = new(evidence, facts.Evidence, cards, facts.Combos);
        int limit = ResultSizeLimit.MaximumBytes - ResultSizeLimit.EnvelopeReserveBytes;
        if (ResultSize.Measure(full) <= limit)
        {
            return full;
        }

        List<SpellbookBracketCardView> flagged =
        [
            .. cards.Where(card => card.GameChanger || card.MassLandDenial || card.ExtraTurn || card.Banned),
        ];
        SpellbookBracketFactsResult trimmed = full with
        {
            Cards = flagged,
            UnflaggedCardsOmitted = cards.Count - flagged.Count,
            Truncated = true,
            Narrow = "Cards with no flag were left out to stay under the size limit.",
        };
        if (ResultSize.Measure(trimmed) <= limit)
        {
            return trimmed;
        }

        int fixedBytes = ResultSize.Measure(trimmed with { Combos = [] });
        List<int> sizes = [.. facts.Combos.Select(combo => ResultSize.Measure(combo) + 1)];
        int keep = ResultSizeLimit.CountFitting(sizes, fixedBytes);
        return trimmed with
        {
            Combos = [.. facts.Combos.Take(keep)],
            CombosOmitted = facts.Combos.Count - keep,
            Narrow = "Cards with no flag and some combos were left out to stay under the size limit.",
        };
    }

    /// <summary>
    /// Reports whether a local entry holds the card Commander Spellbook classified: by Oracle ID when both sides have
    /// one, otherwise by name with the front face of a two-faced card matching either spelling.
    /// </summary>
    private static bool HoldsCard(DeckEntry entry, SpellbookBracketCard card)
    {
        if (entry.OracleId is Guid held && card.OracleId is Guid classified)
        {
            return held == classified;
        }

        return FrontFace(entry.CardName) == FrontFace(card.Name);
    }

    /// <summary>
    /// Normalizes a card name and keeps only the front face of a two-faced name.
    /// </summary>
    private static string FrontFace(string name)
    {
        string normalized = ScryfallCardSets.NormalizeName(name);
        int slashes = normalized.IndexOf(" // ", StringComparison.Ordinal);
        return slashes < 0 ? normalized : normalized[..slashes];
    }
}
