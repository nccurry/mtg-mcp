
namespace MtgMcp.Core;

/// <summary>
/// Shares deck analysis metric helpers across services.
/// </summary>
public abstract partial class DeckServiceBase
{
    /// <summary>
    /// Builds a metric snapshot for a workspace.
    /// </summary>
    protected DeckMetricSnapshot BuildMetricSnapshot(
        DeckWorkspace workspace,
        IReadOnlySet<string> gameChangers)
    {
        return new DeckMetricSnapshot
        {
            Cost = AnalyzeDeckCost(workspace),
            Validation = DeckValidator.Validate(workspace),
            Analysis = DeckAnalyzer.Analyze(workspace),
            ManaBase = AnalyzeManaBase(workspace),
            Consistency = AnalyzeDeckConsistency(workspace),
            Bracket = EstimateCommanderBracket(workspace, gameChangers)
        };
    }

    /// <summary>
    /// Analyzes deck cost from local snapshots.
    /// </summary>
    protected static DeckCostAnalysis AnalyzeDeckCost(DeckWorkspace workspace)
    {
        DeckCostAnalysis analysis = new() { WorkspaceId = workspace.Id };
        List<DeckCostDriver> drivers = [];

        foreach (DeckCard card in workspace.Cards)
        {
            int quantity = Math.Max(0, card.Quantity);
            if (quantity == 0)
            {
                continue;
            }

            decimal? price = ReadUsdPrice(GetSnapshot(card));
            bool isMaybeboard = string.Equals(card.PrimaryCategory, DeckDefaults.Maybeboard, StringComparison.OrdinalIgnoreCase);
            bool includedInDeck = IsIncluded(workspace, card);
            bool includedInPrice = IsIncludedInPrice(workspace, card);

            if (!price.HasValue)
            {
                if ((includedInDeck || isMaybeboard) && includedInPrice)
                {
                    analysis.MissingPriceCards.Add(card.Name);
                }

                continue;
            }

            decimal total = price.Value * quantity;
            if (isMaybeboard && includedInPrice)
            {
                analysis.MaybeboardTotal += total;
            }

            if (includedInDeck && includedInPrice)
            {
                analysis.IncludedTotal += total;
                analysis.PricedIncludedCards++;
                drivers.Add(new DeckCostDriver
                {
                    CardName = card.Name,
                    Category = card.PrimaryCategory,
                    Quantity = quantity,
                    UnitPrice = price.Value,
                    TotalPrice = total
                });
            }
        }

        analysis.TopCostDrivers = drivers
            .OrderByDescending(driver => driver.TotalPrice)
            .Take(10)
            .ToList();
        return analysis;
    }

    /// <summary>
    /// Analyzes mana base metrics for the workspace.
    /// </summary>
    protected static ManaBaseAnalysis AnalyzeManaBase(DeckWorkspace workspace)
    {
        ManaBaseAnalysis analysis = new() { WorkspaceId = workspace.Id };
        foreach (DeckCard card in IncludedCards(workspace))
        {
            CardSnapshot snapshot = GetSnapshot(card);
            int quantity = Math.Max(0, card.Quantity);
            CardRoleAssignment role = DeckRoleClassifier.Classify(card);
            bool isLand = role.PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase);
            bool fixesMana = snapshot.ProducedMana.Count > 1 || role.Tags.Contains(DeckTags.ManaFixing);

            if (isLand)
            {
                analysis.LandCount += quantity;
                if (LooksTapped(snapshot))
                {
                    analysis.TappedLandCount += quantity;
                }
                else
                {
                    analysis.UntappedLandCount += quantity;
                }
            }

            foreach (string color in ReadProducedMana(card))
            {
                AddCount(analysis.ProducedManaSources, color, quantity);
                if (isLand)
                {
                    AddCount(analysis.ColorSources, color, quantity);
                }
            }

            if (fixesMana)
            {
                analysis.FixingCount += quantity;
                if (!isLand)
                {
                    analysis.RampFixingCount += quantity;
                }
            }
        }

        if (analysis.LandCount < 34)
        {
            analysis.Risks.Add("Land count is low for most Commander decks.");
        }

        if (analysis.TappedLandCount >= 12)
        {
            analysis.Risks.Add("Many lands appear to enter tapped, which can slow early turns.");
        }

        if (analysis.ColorSources.Count > 1 && analysis.FixingCount < 8)
        {
            analysis.Risks.Add("Multicolor decks usually want more fixing sources.");
        }

        analysis.Notes.Add("Color source counts are inferred from cached Scryfall produced mana and simple land text heuristics.");
        return analysis;
    }

    /// <summary>
    /// Analyzes consistency metrics for the workspace.
    /// </summary>
    protected static DeckConsistencyAnalysis AnalyzeDeckConsistency(DeckWorkspace workspace)
    {
        List<DeckCard> included = IncludedCards(workspace).ToList();
        DeckConsistencyAnalysis analysis = new()
        {
            WorkspaceId = workspace.Id,
            DeckSize = included.Sum(card => Math.Max(0, card.Quantity))
        };

        foreach (DeckCard card in included)
        {
            int quantity = Math.Max(0, card.Quantity);
            CardRoleAssignment role = DeckRoleClassifier.Classify(card);
            if (role.PrimaryRole.Equals(DeckRoles.Ramp, StringComparison.OrdinalIgnoreCase))
            {
                analysis.RampCount += quantity;
            }

            if (role.PrimaryRole.Equals(DeckRoles.Draw, StringComparison.OrdinalIgnoreCase))
            {
                analysis.DrawCount += quantity;
            }

            if (role.PrimaryRole.Equals(DeckRoles.Tutors, StringComparison.OrdinalIgnoreCase))
            {
                analysis.TutorCount += quantity;
            }

            if (role.Tags.Contains(DeckTags.CardSelection))
            {
                analysis.CardSelectionCount += quantity;
            }

            double manaValue = GetSnapshot(card).ManaValue ?? 0;
            if (!role.PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase) && manaValue <= 2)
            {
                analysis.LowCurveNonlandCount += quantity;
            }
        }

        analysis.KeyOdds = DeckStatistics.AnalyzeDrawOdds(
            workspace,
            [DeckRoles.Lands, DeckRoles.Ramp, DeckRoles.Draw, DeckRoles.Tutors, DeckTags.CardSelection],
            turn: 3,
            openingHandSize: 7,
            simulations: 1_000,
            seed: 1337);

        if (analysis.RampCount < 8)
        {
            analysis.Risks.Add("Ramp density may be low.");
        }

        if (analysis.DrawCount < 8)
        {
            analysis.Risks.Add("Card draw density may be low.");
        }

        if (analysis.LowCurveNonlandCount < 12)
        {
            analysis.Risks.Add("Low-curve nonland density may be light.");
        }

        analysis.Notes.Add("Consistency estimates use role classification and cached card snapshots.");
        return analysis;
    }

    /// <summary>
    /// Estimates Commander bracket from live Game Changers and deck heuristics.
    /// </summary>
    protected static CommanderBracketEstimate EstimateCommanderBracket(
        DeckWorkspace workspace,
        IReadOnlySet<string> gameChangers)
    {
        CommanderBracketEstimate estimate = new() { WorkspaceId = workspace.Id };
        foreach (DeckCard card in IncludedCards(workspace))
        {
            CardSnapshot snapshot = GetSnapshot(card);
            CardRoleAssignment role = DeckRoleClassifier.Classify(card);
            string text = $"{card.Name} {snapshot.TypeLine} {snapshot.OracleText}";

            if (gameChangers.Contains(card.Name))
            {
                estimate.GameChangers.Add(card.Name);
                AddSignal(estimate, card.Name, "game-changer", 3, 3, "Listed by Scryfall as a Commander Game Changer.");
            }

            if (IsFastMana(card))
            {
                AddSignal(estimate, card.Name, "fast-mana", 3, 3, "Fast mana pushes decks toward higher-power tables.");
            }

            if (role.PrimaryRole.Equals(DeckRoles.Tutors, StringComparison.OrdinalIgnoreCase))
            {
                AddSignal(estimate, card.Name, "tutor", 2, 2, "Tutors increase consistency.");
            }

            if (role.Tags.Contains(DeckTags.Stax))
            {
                AddSignal(estimate, card.Name, "stax", 3, 3, "Stax effects can create high-pressure games.");
            }

            if (role.Tags.Contains(DeckTags.ComboPiece))
            {
                AddSignal(estimate, card.Name, "combo", 2, 3, "Combo pieces can raise deck speed and ceiling.");
            }

            if (ContainsAny(text, "extra turn", "takes an extra turn"))
            {
                AddSignal(estimate, card.Name, "extra-turn", 3, 4, "Extra turn effects are strong bracket pressure.");
            }

            if (ContainsAny(text, "destroy all lands", "each player sacrifices all lands", "lands don't untap"))
            {
                AddSignal(estimate, card.Name, "mass-land-denial", 4, 4, "Mass land denial is high-impact table pressure.");
            }
        }

        estimate.GameChangers = estimate.GameChangers
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        estimate.GameChangerCount = estimate.GameChangers.Count;

        int tutorCount = estimate.Signals.Count(signal => signal.Signal.Equals("tutor", StringComparison.OrdinalIgnoreCase));
        if (tutorCount >= 5)
        {
            AddSignal(estimate, "", "high-tutor-density", 4, 4, "Five or more tutors suggest a highly consistent deck.");
        }
        else if (tutorCount >= 3)
        {
            AddSignal(estimate, "", "moderate-tutor-density", 3, 3, "Three or more tutors suggest above-casual consistency.");
        }

        if (estimate.GameChangerCount >= 3)
        {
            AddSignal(estimate, "", "multiple-game-changers", 4, 4, "Multiple Game Changers usually push a deck up.");
        }

        estimate.BracketFloor = estimate.Signals.Count == 0
            ? 1
            : Math.Clamp(estimate.Signals.Max(signal => signal.SuggestedBracket), 1, 4);
        estimate.EstimatedBracket = estimate.BracketFloor;
        estimate.Confidence = estimate.Signals.Count == 0
            ? 0.35
            : Math.Clamp(0.45 + (estimate.Signals.Count * 0.07), 0.45, 0.90);
        estimate.Notes.Add("Commander bracket output is an advisory estimate for pregame discussion, not an official determination.");
        estimate.Notes.Add("Game Changer data is fetched live from Scryfall using is:game-changer.");
        return estimate;
    }

    /// <summary>
    /// Fetches live Game Changer names from Scryfall.
    /// </summary>
    protected async Task<IReadOnlySet<string>> FetchGameChangerNamesAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<CardSearchResult> results = await CardCatalog
                .SearchCardsAsync("is:game-changer", limit: 250, cancellationToken)
                .ConfigureAwait(false);
            return results
                .Select(result => result.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException(
                "Unable to fetch live Commander Game Changer data from Scryfall.",
                exception);
        }
    }

    /// <summary>
    /// Checks whether a card price category is included.
    /// </summary>
    protected static bool IsIncludedInPrice(DeckWorkspace workspace, DeckCard card)
    {
        DeckCategory? category = workspace.Categories.FirstOrDefault(value =>
            string.Equals(value.Name, card.PrimaryCategory, StringComparison.OrdinalIgnoreCase));
        return category?.IncludedInPrice ?? true;
    }

    /// <summary>
    /// Reads produced mana with basic land fallbacks.
    /// </summary>
    protected static IReadOnlyList<string> ReadProducedMana(DeckCard card)
    {
        CardSnapshot snapshot = GetSnapshot(card);
        if (snapshot.ProducedMana.Count > 0)
        {
            return snapshot.ProducedMana;
        }

        string text = $"{card.Name} {snapshot.TypeLine} {snapshot.OracleText}";
        List<string> colors = [];
        AddBasicLandColor(colors, text, "Plains", "W");
        AddBasicLandColor(colors, text, "Island", "U");
        AddBasicLandColor(colors, text, "Swamp", "B");
        AddBasicLandColor(colors, text, "Mountain", "R");
        AddBasicLandColor(colors, text, "Forest", "G");
        return colors;
    }

    /// <summary>
    /// Adds a basic land color fallback.
    /// </summary>
    protected static void AddBasicLandColor(List<string> colors, string text, string landName, string color)
    {
        if (text.Contains(landName, StringComparison.OrdinalIgnoreCase))
        {
            colors.Add(color);
        }
    }

    /// <summary>
    /// Checks whether a land appears to enter tapped.
    /// </summary>
    protected static bool LooksTapped(CardSnapshot snapshot)
    {
        string oracleText = snapshot.OracleText ?? "";
        return oracleText.Contains("enters tapped", StringComparison.OrdinalIgnoreCase)
            || oracleText.Contains("enters the battlefield tapped", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether a card is fast mana.
    /// </summary>
    protected static bool IsFastMana(DeckCard card)
    {
        string[] fastManaNames =
        [
            "Mana Crypt",
            "Jeweled Lotus",
            "Mana Vault",
            "Grim Monolith",
            "Chrome Mox",
            "Mox Diamond",
            "Mox Opal",
            "Lotus Petal",
            "Ancient Tomb"
        ];

        if (fastManaNames.Any(name => card.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        CardSnapshot snapshot = GetSnapshot(card);
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        return role.PrimaryRole.Equals(DeckRoles.Ramp, StringComparison.OrdinalIgnoreCase)
            && (snapshot.ManaValue ?? 99) <= 1
            && !ContainsAny(snapshot.TypeLine ?? "", "Land");
    }

    /// <summary>
    /// Adds a bracket signal.
    /// </summary>
    protected static void AddSignal(
        CommanderBracketEstimate estimate,
        string cardName,
        string signal,
        int severity,
        int suggestedBracket,
        string rationale)
    {
        estimate.Signals.Add(new BracketSignal
        {
            CardName = cardName,
            Signal = signal,
            Severity = severity,
            SuggestedBracket = suggestedBracket,
            Rationale = rationale
        });
    }

    /// <summary>
    /// Checks whether text contains any needles.
    /// </summary>
    protected static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }
}
