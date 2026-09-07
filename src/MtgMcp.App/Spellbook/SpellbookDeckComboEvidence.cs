using System.Text.Json.Serialization;
using MtgMcp.Spellbook;

namespace MtgMcp.App.Spellbook;

/// <summary>
/// Combines the exact local deck selection with unchanged Commander Spellbook evidence.
/// </summary>
internal sealed record SpellbookDeckComboEvidence(
    [property: JsonPropertyName("deck")] SpellbookDeckSelectionEvidence Deck,
    [property: JsonPropertyName("source")] SpellbookEvidence Source);

/// <summary>
/// Reports the exact local deck revision and entries used for one source request.
/// </summary>
internal sealed record SpellbookDeckSelectionEvidence(
    [property: JsonPropertyName("deckId")] Guid DeckId,
    [property: JsonPropertyName("revision")] long Revision,
    IReadOnlyList<SpellbookSentDeckEntry> SentEntries,
    IReadOnlyList<SpellbookSkippedDeckEntry> SkippedEntries)
{
    /// <summary>
    /// Gets an immutable snapshot of normalized entries sent to the source.
    /// </summary>
    public IReadOnlyList<SpellbookSentDeckEntry> SentEntries { get; init; } =
        Array.AsReadOnly(SentEntries.ToArray());

    /// <summary>
    /// Gets an immutable snapshot of entries omitted because their zones are unsupported by the source.
    /// </summary>
    public IReadOnlyList<SpellbookSkippedDeckEntry> SkippedEntries { get; init; } =
        Array.AsReadOnly(SkippedEntries.ToArray());
}

/// <summary>
/// Reports one normalized local entry sent in a Commander Spellbook deck request.
/// </summary>
internal sealed record SpellbookSentDeckEntry(
    [property: JsonPropertyName("zone")] string Zone,
    [property: JsonPropertyName("card")] string Card,
    [property: JsonPropertyName("quantity")] int Quantity);

/// <summary>
/// Reports one local entry omitted because Commander Spellbook accepts only commander and main zones.
/// </summary>
internal sealed record SpellbookSkippedDeckEntry(
    [property: JsonPropertyName("entryId")] Guid EntryId,
    [property: JsonPropertyName("zone")] string Zone,
    [property: JsonPropertyName("cardName")] string CardName,
    [property: JsonPropertyName("quantity")] int Quantity);

/// <summary>
/// Carries the validated provider request and local evidence before source acquisition.
/// </summary>
internal sealed record SpellbookDeckComboSelection(
    SpellbookDeckRequest Request,
    SpellbookDeckSelectionEvidence Evidence);
