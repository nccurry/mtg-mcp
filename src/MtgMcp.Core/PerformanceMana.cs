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
        return TryPay(requirement, ManaValue(card), availableSources, out remainingSources);
    }

    /// <summary>
    /// Attempts to pay an already-parsed mana requirement from available sources.
    /// </summary>
    public static bool TryPay(
        PerformanceCostRequirement requirement,
        int manaValue,
        IReadOnlyList<PerformanceManaSource> availableSources,
        out List<PerformanceManaSource> remainingSources)
    {
        int requiredTotal = Math.Max(Math.Max(0, manaValue), requirement.SymbolGroups.Count);
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
            List<int> genericIndexes = [];
            for (int index = 0; index < availableSources.Count; index++)
            {
                if (!usedIndexes.Contains(index))
                {
                    genericIndexes.Add(index);
                }
            }

            genericIndexes.Sort((left, right) =>
            {
                int priorityComparison = GenericSpendPriority(availableSources[left])
                    .CompareTo(GenericSpendPriority(availableSources[right]));
                return priorityComparison != 0 ? priorityComparison : left.CompareTo(right);
            });
            if (genericIndexes.Count < genericToSpend)
            {
                return false;
            }

            for (int index = 0; index < genericToSpend; index++)
            {
                usedIndexes.Add(genericIndexes[index]);
            }
        }

        for (int index = 0; index < availableSources.Count; index++)
        {
            if (!usedIndexes.Contains(index))
            {
                remainingSources.Add(availableSources[index]);
            }
        }

        return true;
    }

    /// <summary>
    /// Checks whether an already-parsed mana requirement can be paid without consuming the source list.
    /// </summary>
    public static bool CanPay(
        PerformanceCostRequirement requirement,
        int manaValue,
        IReadOnlyList<PerformanceManaSource> availableSources)
    {
        return TryPay(requirement, manaValue, availableSources, out _);
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
            if (IsColoredSymbol(color))
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
    /// Reads produced mana with basic land, Wastes, and MDFC land-slot fallbacks.
    /// </summary>
    public static IReadOnlyList<string> ReadProducedMana(DeckCard card)
    {
        CardSnapshot snapshot = GetSnapshot(card);
        List<string> produced = [];
        foreach (string symbol in snapshot.ProducedMana)
        {
            if (IsManaSymbol(symbol))
            {
                AddDistinctSymbol(produced, symbol.ToUpperInvariant());
            }
        }

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
        AddModalDoubleFacedLandSymbols(colors, card, snapshot);
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
    /// Checks whether early-turn simulation should treat a land as unavailable when it enters.
    /// </summary>
    public static bool LooksTapped(CardSnapshot snapshot)
    {
        return LandEntryClassifier.IsTappedPressure(snapshot);
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

        List<IReadOnlyList<string>> orderedGroups = [.. symbolGroups];
        orderedGroups.Sort((left, right) =>
        {
            int sourceComparison = CountMatchingSources(left, availableSources)
                .CompareTo(CountMatchingSources(right, availableSources));
            return sourceComparison != 0 ? sourceComparison : left.Count.CompareTo(right.Count);
        });
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
        List<int> candidates = [];
        for (int index = 0; index < availableSources.Count; index++)
        {
            if (!usedIndexes.Contains(index) && availableSources[index].CanProduceAny(group))
            {
                candidates.Add(index);
            }
        }

        candidates.Sort((left, right) =>
        {
            int symbolComparison = availableSources[left].Symbols.Count
                .CompareTo(availableSources[right].Symbols.Count);
            return symbolComparison != 0 ? symbolComparison : left.CompareTo(right);
        });
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
        int coloredOptions = 0;
        int colorlessOptions = 0;
        foreach (string symbol in source.Symbols)
        {
            if (IsColoredSymbol(symbol))
            {
                coloredOptions++;
            }
            else if (symbol.Equals("C", StringComparison.OrdinalIgnoreCase))
            {
                colorlessOptions++;
            }
        }

        return (coloredOptions * 10) + colorlessOptions;
    }

    /// <summary>
    /// Counts available sources that can pay a symbol group.
    /// </summary>
    private static int CountMatchingSources(
        IReadOnlyList<string> group,
        IReadOnlyList<PerformanceManaSource> availableSources)
    {
        int count = 0;
        foreach (PerformanceManaSource source in availableSources)
        {
            if (source.CanProduceAny(group))
            {
                count++;
            }
        }

        return count;
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
        List<string> symbols = [];
        foreach (string part in parts)
        {
            if (IsManaSymbol(part))
            {
                AddDistinctSymbol(symbols, part);
            }
        }

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
            AddDistinctSymbol(colors, color);
        }
    }

    /// <summary>
    /// Infers MDFC land-face colors from color identity only when the deck has marked the card as a land slot.
    /// </summary>
    private static void AddModalDoubleFacedLandSymbols(List<string> colors, DeckCard card, CardSnapshot snapshot)
    {
        if (!IsPrimaryLandCategory(card) || !HasNonPrimaryLandFace(snapshot.TypeLine ?? ""))
        {
            return;
        }

        foreach (string color in snapshot.ColorIdentity)
        {
            if (IsColoredSymbol(color))
            {
                AddDistinctSymbol(colors, color.ToUpperInvariant());
            }
        }
    }

    /// <summary>
    /// Checks whether a symbol is one of the supported Magic color symbols.
    /// </summary>
    private static bool IsColoredSymbol(string symbol)
    {
        foreach (string coloredSymbol in ColoredSymbols)
        {
            if (coloredSymbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether a symbol is one of the supported cost or produced-mana symbols.
    /// </summary>
    private static bool IsManaSymbol(string symbol)
    {
        foreach (string manaSymbol in ManaSymbols)
        {
            if (manaSymbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Adds a mana symbol to a list once, preserving first-seen order.
    /// </summary>
    private static void AddDistinctSymbol(List<string> symbols, string symbol)
    {
        foreach (string existing in symbols)
        {
            if (existing.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        symbols.Add(symbol);
    }

    /// <summary>
    /// Checks whether the primary category represents a land slot.
    /// </summary>
    private static bool IsPrimaryLandCategory(DeckCard card)
    {
        string primaryCategory = DeckCategoryOrdering.PrimaryCategory(card);
        return primaryCategory.Equals("Land", StringComparison.OrdinalIgnoreCase)
            || primaryCategory.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether a type line has a land face behind a nonland front face.
    /// </summary>
    private static bool HasNonPrimaryLandFace(string typeLine)
    {
        string[] faces = typeLine.Split(["//"], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return faces.Length > 1
            && !faces[0].Contains("Land", StringComparison.OrdinalIgnoreCase)
            && faces.Skip(1).Any(face => face.Contains("Land", StringComparison.OrdinalIgnoreCase));
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
        foreach (string requiredSymbol in requiredSymbols)
        {
            foreach (string symbol in Symbols)
            {
                if (symbol.Equals(requiredSymbol, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
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
