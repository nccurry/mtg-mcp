
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
        IReadOnlySet<string> gameChangers,
        bool gameChangerDataAvailable = true)
    {
        CommanderBracketEstimate bracket = EstimateCommanderBracket(workspace, gameChangers);
        if (!gameChangerDataAvailable)
        {
            bracket.Notes.RemoveAll(note =>
                note.Contains("Game Changer data is fetched live", StringComparison.OrdinalIgnoreCase));
            bracket.Notes.Add("Game Changer data was unavailable; this estimate excludes live Game Changer signals.");
        }

        return new DeckMetricSnapshot
        {
            Cost = AnalyzeDeckCost(workspace, maxBudget: null),
            Validation = DeckValidator.Validate(workspace),
            Analysis = DeckAnalyzer.Analyze(workspace),
            ManaBase = AnalyzeManaBase(workspace),
            Consistency = AnalyzeDeckConsistency(workspace),
            Bracket = bracket
        };
    }

    /// <summary>
    /// Analyzes deck cost from local snapshots.
    /// </summary>
    protected DeckCostAnalysis AnalyzeDeckCost(DeckWorkspace workspace, decimal? maxBudget = null)
    {
        DeckCostAnalysis analysis = new()
        {
            WorkspaceId = workspace.Id,
            MaxBudget = maxBudget
        };
        List<DeckCostDriver> drivers = [];

        foreach (DeckCard card in workspace.Cards)
        {
            int quantity = Math.Max(0, card.Quantity);
            if (quantity == 0)
            {
                continue;
            }

            CardPriceEvaluation price = EvaluateUsdPrice(GetSnapshot(card), CurrentDate());
            string primaryCategory = DeckCategoryOrdering.PrimaryCategory(card);
            bool isMaybeboard = string.Equals(primaryCategory, DeckDefaults.Maybeboard, StringComparison.OrdinalIgnoreCase);
            bool includedInDeck = IsIncluded(workspace, card);
            bool includedInPrice = IsIncludedInPrice(workspace, card);

            if (!price.PriceKnown || !price.Price.HasValue)
            {
                if ((includedInDeck || isMaybeboard) && includedInPrice)
                {
                    analysis.MissingPriceCards.Add(card.Name);
                    if (!string.IsNullOrWhiteSpace(price.SelectedPrintingReason))
                    {
                        analysis.PriceRiskNotes.Add($"{card.Name}: {price.SelectedPrintingReason}");
                    }

                    if (IsBasicLandCard(card))
                    {
                        analysis.BasicMissingPriceCards.Add(card.Name);
                    }
                    else
                    {
                        analysis.NonBasicMissingPriceCards.Add(card.Name);
                        analysis.UnresolvedMissingPriceCards.Add(card.Name);
                    }
                }

                continue;
            }

            decimal total = price.Price.Value * quantity;
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
                    Category = primaryCategory,
                    Quantity = quantity,
                    UnitPrice = price.Price.Value,
                    TotalPrice = total,
                    PriceSource = price.PriceSource,
                    PriceKnown = price.PriceKnown,
                    PrintingStatus = price.PrintingStatus,
                    SelectedPrintingReason = price.SelectedPrintingReason
                });
            }
        }

        drivers.Sort((left, right) => right.TotalPrice.CompareTo(left.TotalPrice));
        if (drivers.Count > 10)
        {
            drivers.RemoveRange(10, drivers.Count - 10);
        }

        analysis.TopCostDrivers = drivers;
        AddBudgetStatus(analysis, maxBudget);
        return analysis;
    }

    /// <summary>
    /// Analyzes mana base metrics for the workspace.
    /// </summary>
    protected static ManaBaseAnalysis AnalyzeManaBase(DeckWorkspace workspace)
    {
        ManaBaseAnalysis analysis = new() { WorkspaceId = workspace.Id };
        HashSet<string> deckColorIdentity = GetDeckColoredIdentity(workspace);
        foreach (DeckCard card in IncludedCards(workspace))
        {
            CardSnapshot snapshot = GetSnapshot(card);
            int quantity = Math.Max(0, card.Quantity);
            CardRoleAssignment role = DeckRoleClassifier.Classify(card);
            bool isLand = role.PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase);
            bool isLandSlot = IsLandSlotCategory(card);
            bool isModalDoubleFacedLand = HasNonPrimaryLandFace(snapshot.TypeLine ?? "");
            IReadOnlyList<string> producedMana = ReadProducedMana(card);
            bool fixesMana = producedMana.Count > 1 || role.Tags.Contains(DeckTags.ManaFixing);

            if (isLandSlot)
            {
                analysis.LandSlotCount += quantity;
            }

            if (isModalDoubleFacedLand)
            {
                analysis.ModalDoubleFacedLandCount += quantity;
            }

            if (isLand)
            {
                analysis.LandCount += quantity;
                if (producedMana.Count > 0)
                {
                    analysis.ManaProducingLandCount += quantity;
                }

                LandEntryTiming entryTiming = LandEntryClassifier.Classify(snapshot);
                if (entryTiming == LandEntryTiming.AlwaysTapped)
                {
                    analysis.AlwaysTappedLandCount += quantity;
                    analysis.TappedLandCount += quantity;
                }
                else if (entryTiming == LandEntryTiming.ConditionalTapped)
                {
                    analysis.ConditionalTappedLandCount += quantity;
                    analysis.TappedLandCount += quantity;
                }
                else
                {
                    analysis.UntappedLandCount += quantity;
                }
            }

            foreach (string color in producedMana)
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

        if (analysis.AlwaysTappedLandCount >= 12)
        {
            analysis.Risks.Add("Many lands appear to enter tapped, which can slow early turns.");
        }
        else if (analysis.TappedLandCount >= 12)
        {
            analysis.Risks.Add("Many lands may enter tapped unless conditions or costs are met.");
        }

        if (deckColorIdentity.Count > 1 && analysis.FixingCount < 8)
        {
            analysis.Risks.Add("Multicolor decks usually want more fixing sources.");
        }

        analysis.Notes.Add("Color source counts are inferred from cached Scryfall produced mana and simple land text heuristics.");
        analysis.Notes.Add("Tapped land count combines always-tapped and conditional-tapped lands for compatibility.");
        return analysis;
    }

    /// <summary>
    /// Adds additive budget status fields without changing historical totals.
    /// </summary>
    private static void AddBudgetStatus(DeckCostAnalysis analysis, decimal? maxBudget)
    {
        if (analysis.NonBasicMissingPriceCards.Count > 0)
        {
            analysis.PriceRiskNotes.Add("Some included or maybeboard nonbasic cards are missing cached prices, so known totals are a lower bound.");
        }

        analysis.PriceRiskStatus = GetPriceRiskStatus(analysis);
        if (!maxBudget.HasValue)
        {
            analysis.BudgetStatus = analysis.UnresolvedMissingPriceCards.Count > 0
                ? "unknown-missing-prices"
                : "not-requested";
            return;
        }

        analysis.BudgetDelta = maxBudget.Value - analysis.IncludedTotal;
        analysis.WithinKnownBudget = analysis.IncludedTotal <= maxBudget.Value;
        analysis.WithinBudget = analysis.WithinKnownBudget.Value && analysis.NonBasicMissingPriceCards.Count == 0;
        if (analysis.IncludedTotal > maxBudget.Value)
        {
            analysis.BudgetStatus = "over-budget";
            analysis.PriceRiskNotes.Add($"Known included total exceeds max budget {maxBudget.Value:0.##}.");
            return;
        }

        if (analysis.NonBasicMissingPriceCards.Count > 0)
        {
            analysis.BudgetStatus = "under-known-budget-with-price-risk";
            analysis.PriceRiskNotes.Add(
                $"Known included total is within max budget {maxBudget.Value:0.##}, "
                    + "but unresolved nonbasic missing prices still need source-backed pricing.");
            return;
        }

        analysis.BudgetStatus = analysis.IncludedTotal == maxBudget.Value
            ? "at-budget"
            : "under-budget";
    }

    /// <summary>
    /// Gets aggregate price-risk status without treating card names as price data.
    /// </summary>
    private static string GetPriceRiskStatus(DeckCostAnalysis analysis)
    {
        if (analysis.UnresolvedMissingPriceCards.Count > 0)
        {
            return "unresolved";
        }

        return analysis.LowRiskMissingPriceCards.Count > 0
            ? "low"
            : "none";
    }

    /// <summary>
    /// Checks whether a card's cached type line identifies it as a basic land, including Wastes and snow basics.
    /// </summary>
    private static bool IsBasicLandCard(DeckCard card)
    {
        string typeLine = GetSnapshot(card).TypeLine ?? "";
        return typeLine.Contains("Basic", StringComparison.OrdinalIgnoreCase)
            && typeLine.Contains("Land", StringComparison.OrdinalIgnoreCase);
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
                .SearchCardsAsync(
                    CardSearchRequest.ForPreset(CardSearchPreset.CommanderGameChangers),
                    limit: 250,
                    cancellationToken)
                .ConfigureAwait(false);
            return results
                .Select(result => result.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is HttpRequestException
            || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
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
        string primaryCategory = DeckCategoryOrdering.PrimaryCategory(card);
        DeckCategory? category = workspace.Categories.FirstOrDefault(value =>
            string.Equals(value.Name, primaryCategory, StringComparison.OrdinalIgnoreCase));
        return category?.IncludedInPrice ?? true;
    }

    /// <summary>
    /// Reads produced mana with basic land and MDFC land-slot fallbacks.
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
        AddModalDoubleFacedLandColors(colors, card, snapshot);
        return colors.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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
        return LandEntryClassifier.IsTappedPressure(snapshot);
    }

    /// <summary>
    /// Checks whether the primary category represents a land slot.
    /// </summary>
    private static bool IsLandSlotCategory(DeckCard card)
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
            && !ContainsAny(faces[0], "Land")
            && faces.Skip(1).Any(face => ContainsAny(face, "Land"));
    }

    /// <summary>
    /// Infers MDFC land-face colors from color identity only when the deck has marked the card as a land slot.
    /// </summary>
    private static void AddModalDoubleFacedLandColors(List<string> colors, DeckCard card, CardSnapshot snapshot)
    {
        if (!IsLandSlotCategory(card) || !HasNonPrimaryLandFace(snapshot.TypeLine ?? ""))
        {
            return;
        }

        foreach (string color in snapshot.ColorIdentity.Where(IsColoredMana))
        {
            colors.Add(color.ToUpperInvariant());
        }
    }

    /// <summary>
    /// Checks whether a mana symbol is one of Magic's five colors.
    /// </summary>
    private static bool IsColoredMana(string mana)
    {
        return mana.Equals("W", StringComparison.OrdinalIgnoreCase)
            || mana.Equals("U", StringComparison.OrdinalIgnoreCase)
            || mana.Equals("B", StringComparison.OrdinalIgnoreCase)
            || mana.Equals("R", StringComparison.OrdinalIgnoreCase)
            || mana.Equals("G", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the deck's colored identity from commanders when available, otherwise included cards.
    /// </summary>
    private static HashSet<string> GetDeckColoredIdentity(DeckWorkspace workspace)
    {
        HashSet<string> colors = new(StringComparer.OrdinalIgnoreCase);
        List<DeckCard> commanders = IncludedCards(workspace).Where(IsCommanderCard).ToList();
        IEnumerable<DeckCard> sourceCards = commanders.Count > 0 ? commanders : IncludedCards(workspace);

        foreach (DeckCard card in sourceCards)
        {
            foreach (string color in GetSnapshot(card).ColorIdentity.Where(IsColoredMana))
            {
                colors.Add(color);
            }
        }

        return colors;
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
