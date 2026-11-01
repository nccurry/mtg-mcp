namespace MtgMcp.Core.Decks;

/// <summary>
/// Holds the rules every new deck entry must meet, so a caller can find out before a write whether the local deck
/// store would refuse the entry.
/// </summary>
public static class DeckEntryDraftRules
{
    /// <summary>
    /// Returns the first reason the store would refuse the draft, or null when the draft is acceptable.
    /// </summary>
    public static string? FirstProblem(DeckEntryDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.Quantity <= 0)
        {
            return "Entry quantity must be positive.";
        }

        if (draft.EntryId == Guid.Empty)
        {
            return "The entry ID is invalid.";
        }

        if (string.IsNullOrWhiteSpace(draft.CardName))
        {
            return "Card name is required.";
        }

        if (draft.OracleId == Guid.Empty)
        {
            return "The Oracle ID is invalid.";
        }

        if (draft.PrintingId == Guid.Empty)
        {
            return "The printing ID is invalid.";
        }

        return FirstBlank(draft);
    }

    /// <summary>
    /// Returns the reason for the first required text field of the draft that is blank, or null when none is.
    /// </summary>
    private static string? FirstBlank(DeckEntryDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.Language))
        {
            return "Language is required.";
        }

        if (string.IsNullOrWhiteSpace(draft.Finish))
        {
            return "Finish is required.";
        }

        return string.IsNullOrWhiteSpace(draft.Zone) ? "Zone is required." : null;
    }
}
