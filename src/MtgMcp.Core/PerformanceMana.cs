namespace MtgMcp.Core;

/// <summary>
/// Models mana symbols, produced mana options, and one-source-per-payment spending for Stats Lab.
/// </summary>
internal static class PerformanceMana
{
    /// <summary>
    /// Stores the five Magic color symbols.
    /// </summary>
    public static readonly IReadOnlyList<string> ColoredSymbols = ["W", "U", "B", "R", "G"];

    /// <summary>
    /// Stores mana symbols that can appear on produced mana or explicit cost requirements.
    /// </summary>
    private static readonly IReadOnlyList<string> ManaSymbols = ["W", "U", "B", "R", "G", "C"];

    /// <summary>
    /// Attempts to pay a card's cached mana value and colored or colorless requirements from available sources.
    /// </summary>
    public static bool TryPay(
        DeckCard card,
        IReadOnlyList<PerformanceManaSource> availableSources,
        out List<PerformanceManaSource> remainingSources)
    {
        PerformanceCostRequirement requirement = BuildCostRequirement(card);
        int requiredTotal = Math.Max(ManaValue(card), requirement.SymbolGroups.Count);
        remainingSources = [];
        if (requiredTotal > availableSources.Count)
        {
            return false;
        }

        if (!TryChooseRequiredSources(requirement.SymbolGroups, availableSources, out HashSet<int> usedIndexes))
        {
            return false;
        }

        int genericToSpend = requiredTotal - usedIndexes.Count;
        if (genericToSpend > 0)
        {
            List<int> genericIndexes = availableSources
                .Select((source, index) => (source, index))
                .Where(item => !usedIndexes.Contains(item.index))
                .OrderBy(item => GenericSpendPriority(item.source))
                .ThenBy(item => item.index)
                .Take(genericToSpend)
                .Select(item => item.index)
                .ToList();
            if (genericIndexes.Count < genericToSpend)
            {
                return false;
            }

            foreach (int index in genericIndexes)
            {
                usedIndexes.Add(index);
            }
        }

        remainingSources = availableSources
            .Where((_, index) => !usedIndexes.Contains(index))
            .ToList();
        return true;
    }

    /// <summary>
    /// Checks whether a card can be paid without consuming the provided source list.
    /// </summary>
    public static bool CanPay(DeckCard card, IReadOnlyList<PerformanceManaSource> availableSources)
    {
        return TryPay(card, availableSources, out _);
    }

    /// <summary>
    /// Checks whether the available sources satisfy only the colored or colorless portion of a parsed cost.
    /// </summary>
    public static bool CanSatisfyRequirement(
        PerformanceCostRequirement requirement,
        IReadOnlyList<PerformanceManaSource> availableSources)
    {
        return TryChooseRequiredSources(requirement.SymbolGroups, availableSources, out _);
    }

    /// <summary>
    /// Parses colored, colorless, and hybrid requirements from mana cost, falling back to color identity.
    /// </summary>
    public static PerformanceCostRequirement BuildCostRequirement(DeckCard card)
    {
        CardSnapshot snapshot = GetSnapshot(card);
        PerformanceCostRequirement requirement = new();
        string? manaCost = snapshot.ManaCost;
        if (!string.IsNullOrWhiteSpace(manaCost))
        {
            foreach (string symbol in ExtractManaSymbols(manaCost))
            {
                AddManaSymbolRequirement(requirement, symbol);
            }

            return requirement;
        }

        foreach (string color in snapshot.ColorIdentity)
        {
            if (ColoredSymbols.Contains(color, StringComparer.OrdinalIgnoreCase))
            {
                requirement.SymbolGroups.Add([color]);
            }
        }

        return requirement;
    }

    /// <summary>
    /// Reads a nonnegative integer mana value from a card snapshot.
    /// </summary>
    public static int ManaValue(DeckCard card)
    {
        return Math.Max(0, (int)Math.Ceiling(GetSnapshot(card).ManaValue ?? 0));
    }

    /// <summary>
    /// Reads produced mana with basic land and Wastes name fallbacks.
    /// </summary>
    public static IReadOnlyList<string> ReadProducedMana(DeckCard card)
    {
        CardSnapshot snapshot = GetSnapshot(card);
        List<string> produced = snapshot.ProducedMana
            .Where(symbol => ManaSymbols.Contains(symbol, StringComparer.OrdinalIgnoreCase))
            .Select(symbol => symbol.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (produced.Count > 0)
        {
            return produced;
        }

        string text = $"{card.Name} {snapshot.TypeLine} {snapshot.OracleText}";
        List<string> colors = [];
        AddBasicLandSymbol(colors, text, "Plains", "W");
        AddBasicLandSymbol(colors, text, "Island", "U");
        AddBasicLandSymbol(colors, text, "Swamp", "B");
        AddBasicLandSymbol(colors, text, "Mountain", "R");
        AddBasicLandSymbol(colors, text, "Forest", "G");
        AddBasicLandSymbol(colors, text, "Wastes", "C");
        return colors;
    }

    /// <summary>
    /// Gets a card snapshot while tolerating legacy null data.
    /// </summary>
    public static CardSnapshot GetSnapshot(DeckCard card)
    {
        return card.Snapshot ?? new CardSnapshot();
    }

    /// <summary>
    /// Checks whether a land appears to enter tapped.
    /// </summary>
    public static bool LooksTapped(CardSnapshot snapshot)
    {
        string oracleText = snapshot.OracleText ?? "";
        return oracleText.Contains("enters tapped", StringComparison.OrdinalIgnoreCase)
            || oracleText.Contains("enters the battlefield tapped", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Finds exclusive sources for each fixed or flexible symbol group.
    /// </summary>
    private static bool TryChooseRequiredSources(
        IReadOnlyList<IReadOnlyList<string>> symbolGroups,
        IReadOnlyList<PerformanceManaSource> availableSources,
        out HashSet<int> usedIndexes)
    {
        usedIndexes = [];
        if (symbolGroups.Count > availableSources.Count)
        {
            return false;
        }

        List<IReadOnlyList<string>> orderedGroups = symbolGroups
            .OrderBy(group => availableSources.Count(source => source.CanProduceAny(group)))
            .ThenBy(group => group.Count)
            .ToList();
        return TryAssignGroup(0, orderedGroups, availableSources, usedIndexes);
    }

    /// <summary>
    /// Recursively assigns one unused source to each required symbol group.
    /// </summary>
    private static bool TryAssignGroup(
        int groupIndex,
        IReadOnlyList<IReadOnlyList<string>> symbolGroups,
        IReadOnlyList<PerformanceManaSource> availableSources,
        HashSet<int> usedIndexes)
    {
        if (groupIndex >= symbolGroups.Count)
        {
            return true;
        }

        IReadOnlyList<string> group = symbolGroups[groupIndex];
        List<int> candidates = availableSources
            .Select((source, index) => (source, index))
            .Where(item => !usedIndexes.Contains(item.index) && item.source.CanProduceAny(group))
            .OrderBy(item => item.source.Symbols.Count)
            .ThenBy(item => item.index)
            .Select(item => item.index)
            .ToList();
        foreach (int candidate in candidates)
        {
            usedIndexes.Add(candidate);
            if (TryAssignGroup(groupIndex + 1, symbolGroups, availableSources, usedIndexes))
            {
                return true;
            }

            usedIndexes.Remove(candidate);
        }

        return false;
    }

    /// <summary>
    /// Prefers spending less color-flexible sources on generic mana.
    /// </summary>
    private static int GenericSpendPriority(PerformanceManaSource source)
    {
        int coloredOptions = source.Symbols.Count(symbol =>
            ColoredSymbols.Contains(symbol, StringComparer.OrdinalIgnoreCase));
        int colorlessOptions = source.Symbols.Count(symbol =>
            symbol.Equals("C", StringComparison.OrdinalIgnoreCase));
        return (coloredOptions * 10) + colorlessOptions;
    }

    /// <summary>
    /// Extracts individual non-generic mana symbols from a cached mana cost string.
    /// </summary>
    private static List<string> ExtractManaSymbols(string manaCost)
    {
        List<string> symbols = [];
        int index = 0;
        while (index < manaCost.Length)
        {
            int open = manaCost.IndexOf('{', index);
            if (open < 0)
            {
                break;
            }

            int close = manaCost.IndexOf('}', open + 1);
            if (close < 0)
            {
                break;
            }

            string symbol = manaCost[(open + 1)..close].Trim().ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(symbol)
                && !symbol.Equals("X", StringComparison.OrdinalIgnoreCase)
                && !int.TryParse(symbol, out _))
            {
                symbols.Add(symbol);
            }

            index = close + 1;
        }

        if (symbols.Count == 0)
        {
            foreach (string symbol in ColoredSymbols)
            {
                int count = manaCost.Count(character => char.ToUpperInvariant(character) == symbol[0]);
                for (int copy = 0; copy < count; copy++)
                {
                    symbols.Add(symbol);
                }
            }
        }

        return symbols;
    }

    /// <summary>
    /// Adds one parsed mana symbol as a fixed or flexible requirement.
    /// </summary>
    private static void AddManaSymbolRequirement(
        PerformanceCostRequirement requirement,
        string symbol)
    {
        string[] parts = symbol
            .Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        List<string> symbols = parts
            .Where(part => ManaSymbols.Contains(part, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (parts.Length == 1 && symbols.Count == 1)
        {
            requirement.SymbolGroups.Add([symbols[0]]);
            return;
        }

        bool canBePaidWithoutSpecificSymbol = parts.Any(part =>
            part.Equals("P", StringComparison.OrdinalIgnoreCase)
            || int.TryParse(part, out _));
        if (symbols.Count > 0 && !canBePaidWithoutSpecificSymbol)
        {
            requirement.SymbolGroups.Add(symbols);
        }
    }

    /// <summary>
    /// Adds a produced mana fallback when a basic land name appears in card text.
    /// </summary>
    private static void AddBasicLandSymbol(List<string> colors, string text, string landName, string color)
    {
        if (text.Contains(landName, StringComparison.OrdinalIgnoreCase))
        {
            colors.Add(color);
        }
    }
}

/// <summary>
/// Represents one available mana source and the mana symbols it can choose to produce.
/// </summary>
internal sealed class PerformanceManaSource
{
    /// <summary>
    /// Creates a mana source from produced mana symbols.
    /// </summary>
    public PerformanceManaSource(IEnumerable<string> symbols)
    {
        Symbols = symbols
            .Select(symbol => symbol.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Gets the mana symbols this source can produce.
    /// </summary>
    public IReadOnlyList<string> Symbols { get; }

    /// <summary>
    /// Checks whether this source can produce at least one requested symbol.
    /// </summary>
    public bool CanProduceAny(IEnumerable<string> requiredSymbols)
    {
        return requiredSymbols.Any(symbol => Symbols.Contains(symbol, StringComparer.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Stores parsed mana symbol groups that each require one exclusive source.
/// </summary>
internal sealed class PerformanceCostRequirement
{
    /// <summary>
    /// Gets fixed or flexible symbol groups required by the cost.
    /// </summary>
    public List<IReadOnlyList<string>> SymbolGroups { get; } = [];
}
