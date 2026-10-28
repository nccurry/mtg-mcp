using MtgMcp.Scryfall;

namespace MtgMcp.App.Scryfall;

/// <summary>
/// Chooses the exact Scryfall identity of a deck or provider entry, for every tool that reads installed card data.
/// </summary>
internal static class ScryfallEntryLookup
{
    /// <summary>
    /// Chooses the exact Scryfall identity of an entry: its printing, then its set and collector number, then its
    /// Oracle ID. An entry with none has no exact identity.
    /// </summary>
    internal static ScryfallCardLookup? For(
        Guid? printingId,
        string? setCode,
        string? collectorNumber,
        Guid? oracleId)
    {
        if (printingId is Guid printing)
        {
            return new ScryfallCardLookup("scryfall-id", printing.ToString("D"));
        }

        if (!string.IsNullOrWhiteSpace(setCode) && !string.IsNullOrWhiteSpace(collectorNumber))
        {
            return new ScryfallCardLookup("printing", null, setCode, collectorNumber);
        }

        return oracleId is Guid oracle ? new ScryfallCardLookup("oracle-id", oracle.ToString("D")) : null;
    }
}
