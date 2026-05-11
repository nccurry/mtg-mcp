using System.Text.Json;

namespace MtgMcp.Core;

/// <summary>
/// Provides deck workflow primitive behavior.
/// </summary>
public sealed partial class DeckWorkspaceService
{
    /// <summary>
    /// Analyzes deck cost from locally cached card snapshots.
    /// </summary>
    public async Task<DeckCostAnalysis> AnalyzeDeckCostAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        return AnalyzeDeckCost(workspace);
    }

    /// <summary>
    /// Previews a deck edit plan without mutating local or remote state.
    /// </summary>
    public async Task<DeckPlanPreviewResult> PreviewDeckPlanAsync(
        string planId,
        bool resolveAddedCards,
        CancellationToken cancellationToken)
    {
        DeckEditPlan plan = await GetDeckPlanAsync(planId, cancellationToken).ConfigureAwait(false);
        DeckWorkspace workspace = await LoadWorkspaceAsync(plan.WorkspaceId, cancellationToken).ConfigureAwait(false);
        DeckWorkspace preview = CloneWorkspace(workspace);
        IReadOnlySet<string> gameChangers = await FetchGameChangerNamesAsync(cancellationToken).ConfigureAwait(false);
        List<string> warnings = [];

        foreach (DeckEditOperation operation in plan.Operations)
        {
            await ApplyPreviewOperationAsync(preview, operation, resolveAddedCards, warnings, cancellationToken)
                .ConfigureAwait(false);
        }

        return new DeckPlanPreviewResult
        {
            PlanId = plan.PlanId,
            WorkspaceId = plan.WorkspaceId,
            ResolveAddedCards = resolveAddedCards,
            Before = BuildMetricSnapshot(workspace, gameChangers),
            After = BuildMetricSnapshot(preview, gameChangers),
            Warnings = warnings
        };
    }

    /// <summary>
    /// Estimates the Commander bracket for a workspace.
    /// </summary>
    public async Task<CommanderBracketEstimate> EstimateCommanderBracketAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        IReadOnlySet<string> gameChangers = await FetchGameChangerNamesAsync(cancellationToken).ConfigureAwait(false);
        return EstimateCommanderBracket(workspace, gameChangers);
    }

    /// <summary>
    /// Analyzes the deck mana base.
    /// </summary>
    public async Task<ManaBaseAnalysis> AnalyzeManaBaseAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        return AnalyzeManaBase(workspace);
    }

    /// <summary>
    /// Analyzes deck consistency.
    /// </summary>
    public async Task<DeckConsistencyAnalysis> AnalyzeDeckConsistencyAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        return AnalyzeDeckConsistency(workspace);
    }

    /// <summary>
    /// Builds a metric snapshot for a workspace.
    /// </summary>
    private DeckMetricSnapshot BuildMetricSnapshot(
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
    private static DeckCostAnalysis AnalyzeDeckCost(DeckWorkspace workspace)
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
            string primaryCategory = DeckCategoryOrdering.PrimaryCategory(card);
            bool isMaybeboard = primaryCategory.Equals(DeckDefaults.Maybeboard, StringComparison.OrdinalIgnoreCase);
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
                    Category = primaryCategory,
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
    private static ManaBaseAnalysis AnalyzeManaBase(DeckWorkspace workspace)
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
    private static DeckConsistencyAnalysis AnalyzeDeckConsistency(DeckWorkspace workspace)
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
    private static CommanderBracketEstimate EstimateCommanderBracket(
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
    private async Task<IReadOnlySet<string>> FetchGameChangerNamesAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<CardSearchResult> results = await cardCatalog
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
    /// Applies an edit operation to a cloned preview workspace.
    /// </summary>
    private async Task ApplyPreviewOperationAsync(
        DeckWorkspace workspace,
        DeckEditOperation operation,
        bool resolveAddedCards,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        switch (operation.Operation)
        {
            case DeckEditOperations.AddCard:
                await AddPreviewCardAsync(
                    workspace,
                    Require(operation.CardName, "cardName"),
                    operation.Quantity ?? 1,
                    operation.Category ?? DeckDefaults.Mainboard,
                    resolveAddedCards,
                    warnings,
                    cancellationToken).ConfigureAwait(false);
                break;
            case DeckEditOperations.RemoveCard:
                RemovePreviewCard(workspace, Require(operation.CardName, "cardName"), operation.Quantity ?? 1, operation.Category, warnings);
                break;
            case DeckEditOperations.SetCardQuantity:
                SetPreviewCardQuantity(workspace, Require(operation.CardName, "cardName"), operation.Quantity ?? 1, operation.Category, warnings);
                break;
            case DeckEditOperations.MoveCard:
                MovePreviewCard(workspace, Require(operation.CardName, "cardName"), Require(operation.ToCategory, "toCategory"), operation.FromCategory, warnings);
                break;
            case DeckEditOperations.AddCardCategory:
                AddPreviewCardCategory(workspace, Require(operation.CardName, "cardName"), Require(operation.Category, "category"), warnings);
                break;
            case DeckEditOperations.RemoveCardCategory:
                RemovePreviewCardCategory(workspace, Require(operation.CardName, "cardName"), Require(operation.Category, "category"), warnings);
                break;
            case DeckEditOperations.SetPrimaryCardCategory:
                SetPreviewPrimaryCardCategory(workspace, Require(operation.CardName, "cardName"), Require(operation.Category, "category"), warnings);
                break;
            case DeckEditOperations.CreateCategory:
                DeckCategory category = EnsureCategory(workspace, Require(operation.Category, "category"));
                category.IncludedInDeck = operation.IncludedInDeck ?? true;
                category.IncludedInPrice = operation.IncludedInPrice ?? true;
                break;
            case DeckEditOperations.RenameCategory:
                RenamePreviewCategory(workspace, Require(operation.FromCategory, "fromCategory"), Require(operation.ToCategory, "toCategory"), warnings);
                break;
            case DeckEditOperations.DeleteCategory:
                DeletePreviewCategory(workspace, Require(operation.Category, "category"), operation.ToCategory ?? DeckDefaults.Mainboard);
                break;
            case DeckEditOperations.UpdateDeckMetadata:
                workspace.Name = string.IsNullOrWhiteSpace(operation.Name) ? workspace.Name : operation.Name;
                workspace.Format = string.IsNullOrWhiteSpace(operation.Format) ? workspace.Format : operation.Format;
                workspace.Description = operation.Description ?? workspace.Description;
                break;
            default:
                warnings.Add($"Preview skipped unsupported operation '{operation.Operation}'.");
                break;
        }
    }

    /// <summary>
    /// Adds a preview card.
    /// </summary>
    private async Task AddPreviewCardAsync(
        DeckWorkspace workspace,
        string cardName,
        int quantity,
        string category,
        bool resolveAddedCards,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        string normalizedCategory = NormalizeCategoryName(category);
        EnsureCategory(workspace, normalizedCategory);
        DeckCard? existing = FindCard(workspace, cardName, normalizedCategory);
        if (existing is not null)
        {
            existing.Quantity += Math.Max(1, quantity);
            return;
        }

        CardInfo? cardInfo = resolveAddedCards
            ? await cardCatalog.GetCardAsync(cardName, cancellationToken).ConfigureAwait(false)
            : null;
        DeckCard card = new()
        {
            Name = cardInfo?.Name ?? cardName.Trim(),
            Quantity = Math.Max(1, quantity),
            PrimaryCategory = normalizedCategory,
            Categories = [normalizedCategory],
            ScryfallId = cardInfo?.Id,
            ScryfallOracleId = cardInfo?.OracleId
        };

        if (cardInfo is not null)
        {
            ApplyCardSnapshot(card, cardInfo);
        }
        else if (resolveAddedCards)
        {
            warnings.Add($"Could not resolve added card '{cardName}' for preview metrics.");
        }

        DeckCategoryOrdering.Normalize(card, normalizedCategory);
        workspace.Cards.Add(card);
    }

    /// <summary>
    /// Removes a preview card.
    /// </summary>
    private static void RemovePreviewCard(
        DeckWorkspace workspace,
        string cardName,
        int quantity,
        string? category,
        List<string> warnings)
    {
        DeckCard? card = FindCard(workspace, cardName, category);
        if (card is null)
        {
            warnings.Add($"Preview could not remove missing card '{cardName}'.");
            return;
        }

        int amount = Math.Max(1, quantity);
        if (card.Quantity <= amount)
        {
            workspace.Cards.Remove(card);
        }
        else
        {
            card.Quantity -= amount;
        }
    }

    /// <summary>
    /// Sets a preview card quantity.
    /// </summary>
    private static void SetPreviewCardQuantity(
        DeckWorkspace workspace,
        string cardName,
        int quantity,
        string? category,
        List<string> warnings)
    {
        DeckCard? card = FindCard(workspace, cardName, category);
        if (card is null)
        {
            warnings.Add($"Preview could not set quantity for missing card '{cardName}'.");
            return;
        }

        if (quantity <= 0)
        {
            workspace.Cards.Remove(card);
            return;
        }

        card.Quantity = quantity;
    }

    /// <summary>
    /// Moves a preview card.
    /// </summary>
    private static void MovePreviewCard(
        DeckWorkspace workspace,
        string cardName,
        string toCategory,
        string? fromCategory,
        List<string> warnings)
    {
        DeckCard? card = FindCard(workspace, cardName, fromCategory);
        if (card is null)
        {
            warnings.Add($"Preview could not move missing card '{cardName}'.");
            return;
        }

        string normalizedCategory = NormalizeCategoryName(toCategory);
        EnsureCategory(workspace, normalizedCategory);
        DeckCategoryOrdering.SetPrimary(card, normalizedCategory);
    }

    /// <summary>
    /// Adds a category to a preview card.
    /// </summary>
    private static void AddPreviewCardCategory(
        DeckWorkspace workspace,
        string cardName,
        string category,
        List<string> warnings)
    {
        DeckCard? card = FindCard(workspace, cardName, category: null);
        if (card is null)
        {
            warnings.Add($"Preview could not add a category to missing card '{cardName}'.");
            return;
        }

        string normalizedCategory = NormalizeCategoryName(category);
        EnsureCategory(workspace, normalizedCategory);
        DeckCategoryOrdering.AddSecondary(card, normalizedCategory);
    }

    /// <summary>
    /// Removes a category from a preview card.
    /// </summary>
    private static void RemovePreviewCardCategory(
        DeckWorkspace workspace,
        string cardName,
        string category,
        List<string> warnings)
    {
        DeckCard? card = FindCard(workspace, cardName, category: null);
        if (card is null)
        {
            warnings.Add($"Preview could not remove a category from missing card '{cardName}'.");
            return;
        }

        string normalizedCategory = NormalizeCategoryName(category);
        DeckCategoryOrdering.Remove(card, normalizedCategory);
        EnsureCategory(workspace, card.PrimaryCategory);
    }

    /// <summary>
    /// Sets a preview card primary category.
    /// </summary>
    private static void SetPreviewPrimaryCardCategory(
        DeckWorkspace workspace,
        string cardName,
        string category,
        List<string> warnings)
    {
        DeckCard? card = FindCard(workspace, cardName, category: null);
        if (card is null)
        {
            warnings.Add($"Preview could not set a primary category for missing card '{cardName}'.");
            return;
        }

        string normalizedCategory = NormalizeCategoryName(category);
        EnsureCategory(workspace, normalizedCategory);
        DeckCategoryOrdering.SetPrimary(card, normalizedCategory);
    }

    /// <summary>
    /// Renames a preview category.
    /// </summary>
    private static void RenamePreviewCategory(
        DeckWorkspace workspace,
        string fromCategory,
        string toCategory,
        List<string> warnings)
    {
        DeckCategory? category = workspace.Categories.FirstOrDefault(value =>
            value.Name.Equals(fromCategory, StringComparison.OrdinalIgnoreCase));
        if (category is null)
        {
            warnings.Add($"Preview could not rename missing category '{fromCategory}'.");
            return;
        }

        string normalizedNewName = NormalizeCategoryName(toCategory);
        string previousName = category.Name;
        category.Name = normalizedNewName;
        foreach (DeckCard card in workspace.Cards)
        {
            DeckCategoryOrdering.Replace(card, previousName, normalizedNewName);
        }
    }

    /// <summary>
    /// Deletes a preview category.
    /// </summary>
    private static void DeletePreviewCategory(
        DeckWorkspace workspace,
        string categoryName,
        string replacementCategory)
    {
        string replacement = NormalizeCategoryName(replacementCategory);
        EnsureCategory(workspace, replacement);
        workspace.Categories.RemoveAll(category =>
            category.Name.Equals(categoryName, StringComparison.OrdinalIgnoreCase));

        foreach (DeckCard card in workspace.Cards)
        {
            bool wasPrimary = DeckCategoryOrdering.PrimaryCategory(card).Equals(
                categoryName,
                StringComparison.OrdinalIgnoreCase);
            bool removedFromCard =
                card.Categories.RemoveAll(value =>
                    value.Equals(categoryName, StringComparison.OrdinalIgnoreCase)) > 0;
            if (wasPrimary)
            {
                DeckCategoryOrdering.SetPrimary(card, replacement);
            }
            else if (removedFromCard)
            {
                DeckCategoryOrdering.AddSecondary(card, replacement);
            }
        }
    }

    /// <summary>
    /// Clones a deck workspace for preview calculations.
    /// </summary>
    private static DeckWorkspace CloneWorkspace(DeckWorkspace workspace)
    {
        string json = JsonSerializer.Serialize(workspace);
        return JsonSerializer.Deserialize<DeckWorkspace>(json)
            ?? throw new InvalidOperationException("Unable to clone deck workspace for preview.");
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
    /// Reads produced mana with basic land fallbacks.
    /// </summary>
    private static IReadOnlyList<string> ReadProducedMana(DeckCard card)
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
    private static bool LooksTapped(CardSnapshot snapshot)
    {
        string oracleText = snapshot.OracleText ?? "";
        return oracleText.Contains("enters tapped", StringComparison.OrdinalIgnoreCase)
            || oracleText.Contains("enters the battlefield tapped", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether a card is fast mana.
    /// </summary>
    private static bool IsFastMana(DeckCard card)
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
    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }
}
