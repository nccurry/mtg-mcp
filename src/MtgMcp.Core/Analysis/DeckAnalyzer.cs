namespace MtgMcp.Core;

/// <summary>
/// Provides deck analyzer behavior.
/// </summary>
public sealed class DeckAnalyzer
{
    /// <summary>
    /// Stores the legacy type line key.
    /// </summary>
    private const string LegacyTypeLineKey = "typeLine";

    /// <summary>
    /// Stores the legacy mana value key.
    /// </summary>
    private const string LegacyManaValueKey = "manaValue";

    /// <summary>
    /// Stores the legacy color identity key.
    /// </summary>
    private const string LegacyColorIdentityKey = "colorIdentity";

    /// <summary>
    /// Analyzes the workspace.
    /// </summary>
    public static DeckAnalysis Analyze(DeckWorkspace workspace)
    {
        DeckAnalysis analysis = new();
        HashSet<string> includedCategories = workspace
            .Categories.Where(category => category.IncludedInDeck)
            .Select(category => category.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (DeckCard card in workspace.Cards)
        {
            analysis.TotalCards += card.Quantity;
            Increment(analysis.CategoryCounts, card.PrimaryCategory, card.Quantity);
            CardRoleAssignment role = DeckRoleClassifier.Classify(card);
            Increment(analysis.RoleCounts, role.PrimaryRole, card.Quantity);
            foreach (string tag in role.Tags)
            {
                Increment(analysis.TagCounts, tag, card.Quantity);
            }

            if (includedCategories.Contains(card.PrimaryCategory))
            {
                analysis.IncludedCards += card.Quantity;
            }

            string? typeLine = GetTypeLine(card);
            if (string.IsNullOrWhiteSpace(typeLine))
            {
                analysis.Notes.Add(
                    $"{card.Name} has not been normalized with Scryfall card metadata."
                );
                continue;
            }

            CountType(analysis, typeLine, card.Quantity);
            CountManaCurve(analysis, card, card.Quantity);
            CountColorIdentity(analysis, card, card.Quantity);
        }

        return analysis;
    }

    /// <summary>
    /// Counts the type.
    /// </summary>
    private static void CountType(DeckAnalysis analysis, string typeLine, int quantity)
    {
        string[] knownTypes =
        [
            "Creature",
            "Instant",
            "Sorcery",
            "Artifact",
            "Enchantment",
            "Planeswalker",
            "Battle",
            "Land",
        ];
        foreach (string knownType in knownTypes)
        {
            if (typeLine.Contains(knownType, StringComparison.OrdinalIgnoreCase))
            {
                Increment(analysis.TypeCounts, knownType, quantity);
            }
        }
    }

    /// <summary>
    /// Counts the mana curve.
    /// </summary>
    private static void CountManaCurve(DeckAnalysis analysis, DeckCard card, int quantity)
    {
        double? manaValue = GetManaValue(card);
        if (manaValue.HasValue)
        {
            string bucket = Math.Min(7, (int)Math.Floor(manaValue.Value))
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (bucket == "7")
            {
                bucket = "7+";
            }

            Increment(analysis.ManaCurve, bucket, quantity);
        }
    }

    /// <summary>
    /// Counts the color identity.
    /// </summary>
    private static void CountColorIdentity(DeckAnalysis analysis, DeckCard card, int quantity)
    {
        IReadOnlyList<string> colors = GetColorIdentity(card);
        if (colors.Count == 0)
        {
            Increment(analysis.ColorIdentityCounts, "Colorless", quantity);
            return;
        }

        foreach (string color in colors)
        {
            Increment(analysis.ColorIdentityCounts, color, quantity);
        }
    }

    /// <summary>
    /// Gets the type line.
    /// </summary>
    private static string? GetTypeLine(DeckCard card)
    {
        CardSnapshot? snapshot = card.Snapshot;
        if (!string.IsNullOrWhiteSpace(snapshot?.TypeLine))
        {
            return snapshot.TypeLine;
        }

        return card.Metadata.TryGetValue(LegacyTypeLineKey, out string? typeLine) ? typeLine : null;
    }

    /// <summary>
    /// Gets the mana value.
    /// </summary>
    private static double? GetManaValue(DeckCard card)
    {
        CardSnapshot? snapshot = card.Snapshot;
        if (snapshot?.ManaValue is double snapshotManaValue)
        {
            return snapshotManaValue;
        }

        return
            card.Metadata.TryGetValue(LegacyManaValueKey, out string? manaValueText)
            && double.TryParse(
                manaValueText,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double manaValue
            )
            ? manaValue
            : null;
    }

    /// <summary>
    /// Gets the color identity.
    /// </summary>
    private static IReadOnlyList<string> GetColorIdentity(DeckCard card)
    {
        if (card.Snapshot?.ColorIdentity is { Count: > 0 } colorIdentity)
        {
            return colorIdentity;
        }

        if (
            !card.Metadata.TryGetValue(LegacyColorIdentityKey, out string? colors)
            || string.IsNullOrWhiteSpace(colors)
        )
        {
            return [];
        }

        return colors.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
    }

    /// <summary>
    /// Increments a counted analysis bucket.
    /// </summary>
    private static void Increment(Dictionary<string, int> values, string key, int amount)
    {
        values.TryGetValue(key, out int current);
        values[key] = current + amount;
    }
}
