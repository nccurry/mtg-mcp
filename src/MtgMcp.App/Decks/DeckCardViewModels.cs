using System.Text.Json.Serialization;
using MtgMcp.Core.Decks;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Decks;

/// <summary>
/// Describes one deck entry, with the card fields and oracle tags the caller asked for.
/// </summary>
internal sealed record DeckEntryView(
    Guid EntryId,
    int Quantity,
    string CardName,
    Guid? OracleId,
    Guid? PrintingId,
    string? SetCode,
    string? CollectorNumber,
    string Language,
    string Finish,
    string Zone,
    int SortOrder,
    [property: JsonPropertyName("cardData"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CardData = null,
    [property: JsonPropertyName("card"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ScryfallConciseCard? Card = null,
    [property: JsonPropertyName("tags"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? Tags = null)
{
    /// <summary>
    /// Copies a stored entry without card data.
    /// </summary>
    internal static DeckEntryView From(DeckEntry entry)
    {
        return new DeckEntryView(
            entry.EntryId,
            entry.Quantity,
            entry.CardName,
            entry.OracleId,
            entry.PrintingId,
            entry.SetCode,
            entry.CollectorNumber,
            entry.Language,
            entry.Finish,
            entry.Zone,
            entry.SortOrder);
    }
}

/// <summary>
/// Carries a deck as <c>local_deck_get</c> returns it. Without an include list it holds the whole deck and its
/// JSON matches the stored deck; with one it holds a page of entries, the categories, and the assignments of
/// those entries.
/// </summary>
internal sealed record DeckDocumentView(
    Guid DeckId,
    string Name,
    string Description,
    string Format,
    long Revision,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<DeckEntryView> Entries,
    IReadOnlyList<DeckCategory> Categories,
    IReadOnlyList<DeckCategoryAssignment> CategoryAssignments,
    IReadOnlyList<DeckProviderBinding> ProviderBindings)
{
    /// <summary>
    /// Gets the number of entries in the whole deck, set only when the result is a page.
    /// </summary>
    [JsonPropertyName("totalEntries")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TotalEntries { get; init; }

    /// <summary>
    /// Gets the cursor that reads the next page of entries, or null on the last page or when the result is not a page.
    /// </summary>
    [JsonPropertyName("nextCursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NextCursor { get; init; }

    /// <summary>
    /// Gets the reasons some requested card data is missing, or null when there are none.
    /// </summary>
    [JsonPropertyName("limits")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Limits { get; init; }

    /// <summary>
    /// Gets true when the page holds fewer entries than requested because of the result size limit.
    /// </summary>
    [JsonPropertyName("truncated")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Truncated { get; init; }

    /// <summary>
    /// Gets what to change to read a truncated page in smaller pieces.
    /// </summary>
    [JsonPropertyName("narrow")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Narrow { get; init; }
}
