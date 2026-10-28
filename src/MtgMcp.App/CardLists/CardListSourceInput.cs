using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MtgMcp.App.CardLists;

/// <summary>
/// Defines the closed set of card list sources that <c>card_lists_compare</c>, <c>card_list_query</c>, and
/// <c>stats_castability</c> accept. The agent passes a reference to a list, never the list itself.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ScryfallSnapshotSourceInput), "scryfall-snapshot")]
[JsonDerivedType(typeof(CardsSourceInput), "cards")]
[JsonDerivedType(typeof(DeckSourceInput), "deck")]
[JsonDerivedType(typeof(DeckBackupSourceInput), "deck-backup")]
[JsonDerivedType(typeof(ArchidektDeckSourceInput), "archidekt-deck")]
[JsonDerivedType(typeof(ArchidektSnapshotSourceInput), "archidekt-snapshot")]
[JsonDerivedType(typeof(CompareSourceInput), "compare")]
internal abstract record CardListSourceInput;

/// <summary>
/// Reads every card of one saved Scryfall search.
/// </summary>
internal sealed record ScryfallSnapshotSourceInput(
    [property: Description("The snapshotId that scryfall_search returned for the saved search.")]
    Guid SnapshotId)
    : CardListSourceInput;

/// <summary>
/// Reads a short list of cards by Scryfall printing ID or Oracle ID.
/// </summary>
internal sealed record CardsSourceInput(
    [property: Description("One through 150 Scryfall IDs or Oracle IDs (UUIDs). A request holds at most 300 in total."),
     MinLength(1), MaxLength(150)]
    IReadOnlyList<Guid> Ids)
    : CardListSourceInput;

/// <summary>
/// Reads the entries of one local deck.
/// </summary>
internal sealed record DeckSourceInput(
    [property: Description("Stable local deck UUID.")]
    Guid DeckId,
    [property: Description(
        "Optional exact deck revision. A different current revision returns conflict with the current one. " +
        "Omit it to use the current revision; the result then names the revision that was read.")]
    long? ExpectedRevision = null,
    [property: Description("Optional zone names to read, such as main and commander. Omit to read every zone.")]
    IReadOnlyList<string>? Zones = null)
    : CardListSourceInput;

/// <summary>
/// Reads the entries of one local deck as it was in a local backup.
/// </summary>
internal sealed record DeckBackupSourceInput(
    [property: Description("Backup ID from local_deck_backup_list.")]
    Guid BackupId,
    [property: Description("Stable local deck UUID of the deck inside the backup.")]
    Guid DeckId,
    [property: Description("Optional zone names to read. Omit to read every zone.")]
    IReadOnlyList<string>? Zones = null)
    : CardListSourceInput;

/// <summary>
/// Reads the entries of one Archidekt deck live from Archidekt.
/// </summary>
internal sealed record ArchidektDeckSourceInput(
    [property: Description("Exact Archidekt deck identifier, as text.")]
    string ArchidektDeckId,
    [property: Description("Optional zone names to read. Omit to read every zone.")]
    IReadOnlyList<string>? Zones = null)
    : CardListSourceInput;

/// <summary>
/// Reads the entries of one saved Archidekt snapshot.
/// </summary>
internal sealed record ArchidektSnapshotSourceInput(
    [property: Description("Exact Archidekt deck identifier, as text.")]
    string ArchidektDeckId,
    [property: Description("Exact Archidekt snapshot identifier, as text.")]
    string SnapshotId,
    [property: Description("Optional zone names to read. Omit to read every zone.")]
    IReadOnlyList<string>? Zones = null)
    : CardListSourceInput;

/// <summary>
/// Reads the result of another comparison, so operations can be chained, such as (A union B) minus a deck.
/// </summary>
internal sealed record CompareSourceInput(
    [property: Description("The left list of the nested comparison.")]
    CardListSourceInput Left,
    [property: Description("The right list of the nested comparison.")]
    CardListSourceInput Right,
    [property: Description("Set operation: union, intersection, difference (left minus right), or " +
        "symmetric-difference."),
     AllowedValues("union", "intersection", "difference", "symmetric-difference")]
    string Operation,
    [property: Description("What makes two cards the same card: oracle (default), printing, or name."),
     AllowedValues("oracle", "printing", "name")]
    string MatchBy = "oracle")
    : CardListSourceInput;
