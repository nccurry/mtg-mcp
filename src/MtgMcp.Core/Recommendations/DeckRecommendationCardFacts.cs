namespace MtgMcp.Core;

/// <summary>
/// Shares card fact helpers used by recommendation collaborators without tying them to the facade.
/// </summary>
internal static class DeckRecommendationCardFacts
{
    /// <summary>
    /// Creates a one-copy deck card from catalog metadata so role classifiers can score it.
    /// </summary>
    public static DeckCard CreateCandidateCard(CardInfo candidate)
    {
        DeckCard card = new()
        {
            Name = candidate.Name,
            Quantity = 1,
            PrimaryCategory = DeckDefaults.Mainboard,
            Categories = [DeckDefaults.Mainboard],
            ScryfallId = candidate.Id,
            ScryfallOracleId = candidate.OracleId
        };
        DeckServiceHelpers.ApplyCardSnapshot(card, candidate);
        return card;
    }

    /// <summary>
    /// Evaluates whether catalog card details have a usable released-printing price.
    /// </summary>
    public static CardPriceEvaluation EvaluateUsdPrice(CardInfo card)
    {
        return CardPriceEvaluator.Evaluate(card, DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime));
    }

    /// <summary>
    /// Checks whether a card is legal in a format.
    /// </summary>
    public static bool IsLegalInFormat(CardInfo card, string format)
    {
        string legalityKey = NormalizeFormat(format);
        return !card.Legalities.TryGetValue(legalityKey, out string? legality)
            || legality.Equals("legal", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Normalizes a format name to the Scryfall legality key used by recommendation filters.
    /// </summary>
    public static string NormalizeFormat(string? format)
    {
        string normalized = format?.Trim().ToLowerInvariant() ?? "";
        return normalized switch
        {
            "" => "commander",
            "edh" => "commander",
            _ => normalized
        };
    }

    /// <summary>
    /// Gets the deck color identity from commanders when present, otherwise from included cards.
    /// </summary>
    public static (bool IsKnown, HashSet<string> Colors) GetDeckColorIdentity(DeckWorkspace workspace)
    {
        HashSet<string> colors = new(StringComparer.OrdinalIgnoreCase);
        bool foundCommander = false;

        foreach (DeckCard card in workspace.Cards)
        {
            if (!IsCommanderCard(card))
            {
                continue;
            }

            foundCommander = true;
            AddColors(colors, DeckServiceHelpers.GetSnapshot(card).ColorIdentity);
        }

        if (foundCommander)
        {
            return (true, colors);
        }

        foreach (DeckCard card in DeckServiceHelpers.IncludedCards(workspace))
        {
            AddColors(colors, DeckServiceHelpers.GetSnapshot(card).ColorIdentity);
        }

        return (colors.Count > 0, colors);
    }

    /// <summary>
    /// Checks whether a candidate fits the deck color identity.
    /// </summary>
    public static bool IsInDeckColorIdentity(CardInfo candidate, bool colorIdentityKnown, HashSet<string> deckColorIdentity)
    {
        return !colorIdentityKnown
            || candidate.ColorIdentity.All(color => deckColorIdentity.Contains(color));
    }

    /// <summary>
    /// Checks whether a card is categorized as the commander.
    /// </summary>
    private static bool IsCommanderCard(DeckCard card)
    {
        return DeckCategoryOrdering.PrimaryCategory(card).Equals(
            DeckRoles.Commander,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Adds colors to a color set.
    /// </summary>
    private static void AddColors(HashSet<string> colors, IEnumerable<string> colorIdentity)
    {
        foreach (string color in colorIdentity)
        {
            if (!string.IsNullOrWhiteSpace(color))
            {
                colors.Add(color);
            }
        }
    }
}
