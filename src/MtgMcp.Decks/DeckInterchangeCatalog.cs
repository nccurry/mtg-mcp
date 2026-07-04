using MtgMcp.Core.Decks;

namespace MtgMcp.Decks;

/// <summary>
/// Defines the complete, ordered manual interchange surface without provider discovery.
/// </summary>
internal static class DeckInterchangeCatalog
{
    /// <summary>
    /// Identifies the lossless native JSON contract.
    /// </summary>
    internal const string Native = "mtg-mcp-json-v1";

    /// <summary>
    /// Identifies the provider-neutral quantity and name text contract.
    /// </summary>
    internal const string Generic = "generic-text-v1";

    /// <summary>
    /// Identifies the manually pasted Archidekt contract.
    /// </summary>
    internal const string Archidekt = "archidekt-text-v1";

    /// <summary>
    /// Identifies the manually pasted Moxfield Bulk Edit contract.
    /// </summary>
    internal const string Moxfield = "moxfield-bulk-edit-v1";

    /// <summary>
    /// Gets every format in stable public order.
    /// </summary>
    internal static IReadOnlyList<DeckInterchangeFormat> All { get; } =
        Array.AsReadOnly<DeckInterchangeFormat>(
        [
            new DeckInterchangeFormat(
                Native,
                "mtg-mcp Native JSON v1",
                true,
                true,
                true,
                "available",
                "Use for lossless backup, restore, and transfer between mtg-mcp installations.",
                []),
            new DeckInterchangeFormat(
                Generic,
                "Generic Deck Text",
                true,
                true,
                false,
                "available",
                "Paste sectioned quantity/name lines; verify unresolved card names before provider use.",
                ["Functional categories and local lifecycle metadata require the native companion artifact."]),
            new DeckInterchangeFormat(
                Archidekt,
                "Archidekt Manual Import Text",
                true,
                true,
                false,
                "available",
                "Paste deck.archidekt.txt into Archidekt's manual importer and retain the companions.",
                [
                    "Exact quantities, names, printings, and one primary category passed the current UI acceptance.",
                    "Zones, distinct same-print finishes, and secondary categories remain companion-only.",
                ]),
            new DeckInterchangeFormat(
                Moxfield,
                "Moxfield Bulk Edit Text",
                true,
                true,
                false,
                "available",
                "Paste deck.moxfield.txt into Moxfield Bulk Edit, review it, and retain the companions.",
                [
                    "Moxfield does not publish a stable machine-readable Bulk Edit contract.",
                    "Exact printings, finish markers, and local tags passed the current UI acceptance; zones remain companion-only.",
                    "The explicit global-tag option remains unverified and is reported as companion-only.",
                ]),
        ]);

    /// <summary>
    /// Finds one exact lowercase format identifier.
    /// </summary>
    internal static DeckInterchangeFormat? Find(string? formatId)
    {
        return All.FirstOrDefault(value => string.Equals(
            value.FormatId,
            formatId?.Trim(),
            StringComparison.Ordinal));
    }
}
