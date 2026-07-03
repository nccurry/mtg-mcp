
namespace MtgMcp.Core;

/// <summary>
/// Builds reusable deck metric snapshots, costs, mana-base health, consistency, and Commander bracket estimates.
/// </summary>
public sealed class DeckAnalysisMetrics
{
    /// <summary>
    /// Searches live card data for Commander Game Changer names.
    /// </summary>
    private readonly ICardCatalog cardCatalog;

    /// <summary>
    /// Evaluates local card price metadata for cost analysis.
    /// </summary>
    private readonly IPriceSource priceSource;

    /// <summary>
    /// Supplies the reference date used for deterministic price evaluation.
    /// </summary>
    private readonly Func<DateOnly> currentDateProvider;

    /// <summary>
    /// Creates metrics using the current UTC date for price-sensitive evaluations.
    /// </summary>
    public DeckAnalysisMetrics(ICardCatalog cardCatalog)
        : this(cardCatalog, CatalogPriceSource.Instance, CurrentUtcDate)
    {
    }

    /// <summary>
    /// Creates metrics with an explicit price source.
    /// </summary>
    public DeckAnalysisMetrics(ICardCatalog cardCatalog, IPriceSource priceSource)
        : this(cardCatalog, priceSource, CurrentUtcDate)
    {
    }

    /// <summary>
    /// Creates metrics with an explicit date provider for deterministic tests and previews.
    /// </summary>
    internal DeckAnalysisMetrics(ICardCatalog cardCatalog, Func<DateOnly> currentDateProvider)
        : this(cardCatalog, CatalogPriceSource.Instance, currentDateProvider)
    {
    }

    /// <summary>
    /// Creates metrics with explicit price and date providers for deterministic tests and previews.
    /// </summary>
    internal DeckAnalysisMetrics(
        ICardCatalog cardCatalog,
        IPriceSource priceSource,
        Func<DateOnly> currentDateProvider)
    {
        this.cardCatalog = cardCatalog;
        this.priceSource = priceSource;
        this.currentDateProvider = currentDateProvider;
    }

    /// <summary>
    /// Builds a metric snapshot for a workspace.
    /// </summary>
    public DeckMetricSnapshot BuildMetricSnapshot(
        DeckWorkspace workspace,
        IReadOnlySet<string> gameChangers,
        bool gameChangerDataAvailable = true,
        string? gameChangerNote = null)
    {
        CommanderBracketEstimate bracket = EstimateCommanderBracket(workspace, gameChangers);
        if (!string.IsNullOrWhiteSpace(gameChangerNote))
        {
            bracket.Notes.RemoveAll(note =>
                note.Contains("Game Changer data is fetched live", StringComparison.OrdinalIgnoreCase));
            bracket.Notes.Add(gameChangerNote);
        }
        else if (!gameChangerDataAvailable)
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
    public DeckCostAnalysis AnalyzeDeckCost(DeckWorkspace workspace, decimal? maxBudget = null)
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

            CardPriceEvaluation price = priceSource.Evaluate(GetSnapshot(card), CurrentDate());
            string primaryCategory = DeckCategoryOrdering.PrimaryCategory(card);
            bool isMaybeboard = string.Equals(primaryCategory, DeckDefaults.Maybeboard, StringComparison.OrdinalIgnoreCase);
            bool includedInDeck = IsIncluded(workspace, card);
            bool includedInPrice = IsIncludedInPrice(workspace, card);

            if (!price.PriceKnown || !price.Price.HasValue)
            {
                TrackMissingPrice(analysis, card, price, includedInDeck || isMaybeboard, includedInPrice);

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
    /// Records one relevant card whose source snapshot does not provide a usable price.
    /// </summary>
    private static void TrackMissingPrice(
        DeckCostAnalysis analysis,
        DeckCard card,
        CardPriceEvaluation price,
        bool relevantToDeck,
        bool includedInPrice)
    {
        if (!relevantToDeck || !includedInPrice)
        {
            return;
        }

        analysis.MissingPriceCards.Add(card.Name);
        if (!string.IsNullOrWhiteSpace(price.SelectedPrintingReason))
        {
            analysis.PriceRiskNotes.Add($"{card.Name}: {price.SelectedPrintingReason}");
        }

        if (IsBasicLandCard(card))
        {
            analysis.BasicMissingPriceCards.Add(card.Name);
            return;
        }

        analysis.NonBasicMissingPriceCards.Add(card.Name);
        analysis.UnresolvedMissingPriceCards.Add(card.Name);
    }

    /// <summary>
    /// Analyzes mana base metrics for the workspace.
    /// </summary>
    public ManaBaseAnalysis AnalyzeManaBase(DeckWorkspace workspace)
    {
        ManaBaseAnalysis analysis = new() { WorkspaceId = workspace.Id };
        HashSet<string> deckColorIdentity = GetDeckColoredIdentity(workspace);
        bool filteredAnyColorForDisplay = false;
        foreach (DeckCard card in IncludedCards(workspace))
        {
            CardSnapshot snapshot = GetSnapshot(card);
            int quantity = Math.Max(0, card.Quantity);
            if (quantity == 0)
            {
                continue;
            }

            CardRoleAssignment role = DeckRoleClassifier.Classify(card);
            bool isLand = role.PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase);
            bool isLandSlot = IsLandSlotCategory(card);
            bool isModalDoubleFacedLand = HasNonPrimaryLandFace(snapshot.TypeLine ?? "");
            IReadOnlyList<string> producedMana = ReadProducedMana(card);
            IReadOnlyList<string> displayProducedMana = FilterProducedManaForDeckColorSummary(
                producedMana,
                deckColorIdentity);
            bool fixesMana = producedMana.Count > 1 || role.Tags.Contains(DeckTags.ManaFixing);
            filteredAnyColorForDisplay |= displayProducedMana.Count != producedMana
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

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
                    analysis.TappedLandContributors.Add(
                        BuildTappedLandContributor(card, snapshot, quantity, producedMana, entryTiming, isModalDoubleFacedLand));
                }
                else if (entryTiming == LandEntryTiming.ConditionalTapped)
                {
                    analysis.ConditionalTappedLandCount += quantity;
                    analysis.TappedLandCount += quantity;
                    analysis.TappedLandContributors.Add(
                        BuildTappedLandContributor(card, snapshot, quantity, producedMana, entryTiming, isModalDoubleFacedLand));
                }
                else
                {
                    analysis.UntappedLandCount += quantity;
                }
            }

            foreach (string color in displayProducedMana)
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

        TrimTappedLandContributors(analysis.TappedLandContributors);

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
        if (filteredAnyColorForDisplay)
        {
            analysis.Notes.Add("Any-color sources are filtered to deck color identity in color-source summaries; fixing counts still use full any-color capability.");
        }

        analysis.Notes.Add("Tapped land count combines always-tapped and conditional-tapped lands for compatibility.");
        if (analysis.TappedLandContributors.Count > 0)
        {
            analysis.Notes.Add("Tapped land contributors identify the lands to prioritize for same-color untapped replacement searches.");
        }

        return analysis;
    }

    /// <summary>
    /// Builds one tapped-land contributor row from a classified land.
    /// </summary>
    private TappedLandContributor BuildTappedLandContributor(
        DeckCard card,
        CardSnapshot snapshot,
        int quantity,
        IReadOnlyList<string> producedMana,
        LandEntryTiming entryTiming,
        bool isModalDoubleFacedLand)
    {
        return new TappedLandContributor
        {
            CardName = card.Name,
            Quantity = quantity,
            Timing = TappedLandTiming(entryTiming),
            ProducedMana = producedMana.ToList(),
            Reason = TappedLandReason(snapshot, entryTiming, isModalDoubleFacedLand),
            ScryfallUri = snapshot.ScryfallUri
        };
    }

    /// <summary>
    /// Sorts tapped-land contributors into a bounded, high-signal list.
    /// </summary>
    private void TrimTappedLandContributors(List<TappedLandContributor> contributors)
    {
        contributors.Sort(CompareTappedLandContributors);
        if (contributors.Count > 10)
        {
            contributors.RemoveRange(10, contributors.Count - 10);
        }
    }

    /// <summary>
    /// Compares tapped-land contributors by severity, quantity, then name.
    /// </summary>
    private int CompareTappedLandContributors(TappedLandContributor left, TappedLandContributor right)
    {
        int timing = TappedLandTimingRank(left.Timing).CompareTo(TappedLandTimingRank(right.Timing));
        if (timing != 0)
        {
            return timing;
        }

        int quantity = right.Quantity.CompareTo(left.Quantity);
        return quantity != 0
            ? quantity
            : string.Compare(left.CardName, right.CardName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the stable timing label for tapped-land output.
    /// </summary>
    private string TappedLandTiming(LandEntryTiming entryTiming)
    {
        return entryTiming == LandEntryTiming.AlwaysTapped
            ? "alwaysTapped"
            : "conditionalTapped";
    }

    /// <summary>
    /// Gets the sort rank for tapped-land timing labels.
    /// </summary>
    private static int TappedLandTimingRank(string timing)
    {
        return timing.Equals("alwaysTapped", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    }

    /// <summary>
    /// Explains why a land was assigned to a tapped timing bucket.
    /// </summary>
    private static string TappedLandReason(
        CardSnapshot snapshot,
        LandEntryTiming entryTiming,
        bool isModalDoubleFacedLand)
    {
        if (entryTiming == LandEntryTiming.ConditionalTapped)
        {
            return "Cached oracle text has a tapped-unless condition or optional cost.";
        }

        return isModalDoubleFacedLand
            ? "Cached type line has a nonland front face with a land back face."
            : "Cached oracle text says this land enters tapped.";
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
    public DeckConsistencyAnalysis AnalyzeDeckConsistency(DeckWorkspace workspace)
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
            foreach (string functionalRole in role.FunctionalRoles)
            {
                AddCount(analysis.FunctionalRoleCounts, functionalRole, quantity);
            }

            if (HasFunctionalRole(role, DeckRoles.Ramp))
            {
                analysis.RampCount += quantity;
            }

            if (HasFunctionalRole(role, DeckRoles.Draw))
            {
                analysis.DrawCount += quantity;
            }

            if (HasFunctionalRole(role, DeckRoles.Tutors))
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
    /// Checks whether a role assignment has an additive functional role.
    /// </summary>
    private static bool HasFunctionalRole(CardRoleAssignment role, string target)
    {
        foreach (string functionalRole in role.FunctionalRoles)
        {
            if (functionalRole.Equals(target, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Estimates Commander bracket from live Game Changers and deck heuristics.
    /// </summary>
    public CommanderBracketEstimate EstimateCommanderBracket(
        DeckWorkspace workspace,
        IReadOnlySet<string> gameChangers)
    {
        CommanderBracketEstimate estimate = new() { WorkspaceId = workspace.Id };
        int fastManaCount = 0;
        int tutorCount = 0;
        int comboCount = 0;
        int staxCount = 0;
        int extraTurnCount = 0;
        int massLandDenialCount = 0;
        foreach (DeckCard card in IncludedCards(workspace))
        {
            int quantity = Math.Max(1, card.Quantity);
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
                fastManaCount += quantity;
                AddSignal(estimate, card.Name, "fast-mana", 3, 3, "Fast mana pushes decks toward higher-power tables.");
            }

            if (role.PrimaryRole.Equals(DeckRoles.Tutors, StringComparison.OrdinalIgnoreCase))
            {
                tutorCount += quantity;
                AddSignal(estimate, card.Name, "tutor", 2, 2, "Tutors increase consistency.");
            }

            if (role.Tags.Contains(DeckTags.Stax))
            {
                staxCount += quantity;
                AddSignal(estimate, card.Name, "stax", 3, 3, "Stax effects can create high-pressure games.");
            }

            if (role.Tags.Contains(DeckTags.ComboPiece))
            {
                comboCount += quantity;
                AddSignal(estimate, card.Name, "combo", 2, 3, "Combo pieces can raise deck speed and ceiling.");
            }

            if (ContainsAny(text, "extra turn", "takes an extra turn"))
            {
                extraTurnCount += quantity;
                AddSignal(estimate, card.Name, "extra-turn", 3, 4, "Extra turn effects are strong bracket pressure.");
            }

            if (ContainsAny(text, "destroy all lands", "each player sacrifices all lands", "lands don't untap"))
            {
                massLandDenialCount += quantity;
                AddSignal(estimate, card.Name, "mass-land-denial", 4, 4, "Mass land denial is high-impact table pressure.");
            }
        }

        estimate.GameChangers = estimate.GameChangers
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        estimate.GameChangerCount = estimate.GameChangers.Count;

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

        int densityBracket = EstimateBracketFromDensity(
            estimate.GameChangerCount,
            fastManaCount,
            tutorCount,
            comboCount,
            staxCount,
            extraTurnCount,
            massLandDenialCount);
        estimate.BracketFloor = EstimateBracketFloor(
            estimate.GameChangerCount,
            fastManaCount,
            tutorCount,
            comboCount,
            staxCount,
            extraTurnCount,
            massLandDenialCount);
        estimate.EstimatedBracket = Math.Clamp(Math.Max(estimate.BracketFloor, densityBracket), 1, 4);
        if (densityBracket >= 3)
        {
            AddSignal(
                estimate,
                "",
                $"density-bracket-{densityBracket}",
                densityBracket,
                densityBracket,
                "Combined density of fast mana, tutors, combo, stax, extra turns, Game Changers, and land denial raises the estimate.");
        }

        estimate.Confidence = estimate.Signals.Count == 0
            ? 0.35
            : Math.Clamp(0.45 + (estimate.Signals.Count * 0.05) + ((estimate.EstimatedBracket - 1) * 0.05), 0.45, 0.90);
        estimate.Notes.Add("Commander bracket output is an advisory estimate for pregame discussion, not an official determination.");
        estimate.Notes.Add("The model uses signal density; one high-severity card is pressure evidence, not by itself a formal bracket assignment.");
        estimate.Notes.Add("Game Changer data is fetched live from Scryfall using is:game-changer.");
        return estimate;
    }

    /// <summary>
    /// Estimates bracket from combined signal density instead of the single largest signal.
    /// </summary>
    private static int EstimateBracketFromDensity(
        int gameChangerCount,
        int fastManaCount,
        int tutorCount,
        int comboCount,
        int staxCount,
        int extraTurnCount,
        int massLandDenialCount)
    {
        double score = 0;
        score += Math.Min(gameChangerCount, 4) * 1.00;
        score += Math.Min(fastManaCount, 8) * 0.15;
        score += Math.Min(tutorCount, 8) * 0.12;
        score += Math.Min(comboCount, 8) * 0.10;
        score += Math.Min(staxCount, 5) * 0.18;
        score += Math.Min(extraTurnCount, 4) * 0.35;
        score += Math.Min(massLandDenialCount, 4) * 0.45;

        if (fastManaCount >= 6 && tutorCount >= 3)
        {
            score += 0.35;
        }

        if (tutorCount >= 4 && comboCount >= 4)
        {
            score += 0.35;
        }

        if (gameChangerCount >= 2)
        {
            score += 0.50;
        }

        if (staxCount + extraTurnCount + massLandDenialCount >= 2)
        {
            score += 0.35;
        }

        if (score >= 2.80)
        {
            return 4;
        }

        if (score >= 1.50)
        {
            return 3;
        }

        return score >= 0.50 ? 2 : 1;
    }

    /// <summary>
    /// Computes a conservative floor from strong single signals and dense combinations.
    /// </summary>
    private static int EstimateBracketFloor(
        int gameChangerCount,
        int fastManaCount,
        int tutorCount,
        int comboCount,
        int staxCount,
        int extraTurnCount,
        int massLandDenialCount)
    {
        if (gameChangerCount >= 3
            || tutorCount >= 5
            || massLandDenialCount >= 2
            || (fastManaCount >= 5 && tutorCount >= 3)
            || (comboCount >= 5 && tutorCount >= 3))
        {
            return 4;
        }

        if (gameChangerCount > 0
            || fastManaCount > 0
            || staxCount > 0
            || extraTurnCount > 0
            || massLandDenialCount > 0
            || tutorCount >= 3
            || comboCount >= 3)
        {
            return 3;
        }

        return tutorCount > 0 || comboCount > 0 ? 2 : 1;
    }

    /// <summary>
    /// Fetches live Game Changer names from Scryfall.
    /// </summary>
    public async Task<IReadOnlySet<string>> FetchGameChangerNamesAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<CardSearchResult> results = await cardCatalog
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
    private static bool IsIncludedInPrice(DeckWorkspace workspace, DeckCard card)
    {
        string primaryCategory = DeckCategoryOrdering.PrimaryCategory(card);
        DeckCategory? category = workspace.Categories.FirstOrDefault(value =>
            string.Equals(value.Name, primaryCategory, StringComparison.OrdinalIgnoreCase));
        return category?.IncludedInPrice ?? true;
    }

    /// <summary>
    /// Reads produced mana with basic land and MDFC land-slot fallbacks.
    /// </summary>
    public static IReadOnlyList<string> ReadProducedMana(DeckCard card)
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
    /// Filters any-color source displays to the deck color identity without changing internal fixing logic.
    /// </summary>
    private static IReadOnlyList<string> FilterProducedManaForDeckColorSummary(
        IReadOnlyList<string> producedMana,
        IReadOnlySet<string> deckColorIdentity)
    {
        List<string> distinct = producedMana
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (deckColorIdentity.Count == 0 || !ContainsAllColoredMana(distinct))
        {
            return distinct;
        }

        List<string> filtered = [];
        foreach (string symbol in distinct)
        {
            if (symbol.Equals("C", StringComparison.OrdinalIgnoreCase)
                || deckColorIdentity.Contains(symbol))
            {
                filtered.Add(symbol);
            }
        }

        return filtered;
    }

    /// <summary>
    /// Checks whether a produced-mana list represents all five colored mana symbols.
    /// </summary>
    private static bool ContainsAllColoredMana(IReadOnlyList<string> producedMana)
    {
        return producedMana.Contains("W", StringComparer.OrdinalIgnoreCase)
            && producedMana.Contains("U", StringComparer.OrdinalIgnoreCase)
            && producedMana.Contains("B", StringComparer.OrdinalIgnoreCase)
            && producedMana.Contains("R", StringComparer.OrdinalIgnoreCase)
            && producedMana.Contains("G", StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Adds a basic land color fallback.
    /// </summary>
    private static void AddBasicLandColor(List<string> colors, string text, string landName, string color)
    {
        if (text.Contains(landName, StringComparison.OrdinalIgnoreCase))
        {
            colors.Add(color);
        }
    }

    /// <summary>
    /// Checks whether a land appears to enter tapped.
    /// </summary>
    public static bool LooksTapped(CardSnapshot snapshot)
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
    public static bool IsFastMana(DeckCard card)
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
    private static void AddSignal(
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
    public static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Enumerates cards that contribute to the active deck.
    /// </summary>
    private static IEnumerable<DeckCard> IncludedCards(DeckWorkspace workspace)
    {
        return DeckCategoryInclusion.IncludedCards(workspace);
    }

    /// <summary>
    /// Checks whether a card contributes to the active deck.
    /// </summary>
    private static bool IsIncluded(DeckWorkspace workspace, DeckCard card)
    {
        return DeckCategoryInclusion.IsIncludedInDeck(workspace, card);
    }

    /// <summary>
    /// Reads cached card metadata while tolerating legacy null snapshots.
    /// </summary>
    private static CardSnapshot GetSnapshot(DeckCard card)
    {
        return card.Snapshot ?? new CardSnapshot();
    }

    /// <summary>
    /// Adds a positive quantity to a case-insensitive count dictionary.
    /// </summary>
    private static void AddCount(Dictionary<string, int> counts, string key, int quantity)
    {
        if (string.IsNullOrWhiteSpace(key) || quantity <= 0)
        {
            return;
        }

        counts.TryGetValue(key, out int current);
        counts[key] = current + quantity;
    }

    /// <summary>
    /// Checks whether a card occupies the command-zone commander category.
    /// </summary>
    private static bool IsCommanderCard(DeckCard card)
    {
        return DeckCategoryOrdering.PrimaryCategory(card).Equals(
            DeckRoles.Commander,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads the reference date for price-sensitive metrics.
    /// </summary>
    private DateOnly CurrentDate()
    {
        return currentDateProvider();
    }

    /// <summary>
    /// Gets today's date in UTC for normal runtime metric evaluation.
    /// </summary>
    private static DateOnly CurrentUtcDate()
    {
        return DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
    }
}
