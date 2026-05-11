namespace MtgMcp.Core;

/// <summary>
/// Provides deck intelligence workspace behavior.
/// </summary>
public sealed partial class DeckWorkspaceService
{
    /// <summary>
    /// Normalizes deck card metadata.
    /// </summary>
    public async Task<DeckNormalizationResult> NormalizeDeckCardsAsync(
        string workspaceId,
        string scope,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        string normalizedScope = string.IsNullOrWhiteSpace(scope) ? "all" : scope.Trim().ToLowerInvariant();
        DeckNormalizationResult result = await NormalizeWorkspaceCardsAsync(workspace, normalizedScope, cancellationToken)
            .ConfigureAwait(false);

        await repository.SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Normalizes workspace cards.
    /// </summary>
    private async Task<DeckNormalizationResult> NormalizeWorkspaceCardsAsync(
        DeckWorkspace workspace,
        string normalizedScope,
        CancellationToken cancellationToken)
    {
        List<DeckCard> targetCards = workspace.Cards
            .Where(card => ShouldNormalize(card, workspace, normalizedScope))
            .ToList();

        IReadOnlyDictionary<string, CardInfo> cardsByName = await cardCatalog
            .GetCardsByNamesAsync(targetCards.Select(card => card.Name).ToList(), cancellationToken)
            .ConfigureAwait(false);

        List<string> missingCards = [];
        int updatedCards = 0;
        foreach (DeckCard card in targetCards)
        {
            if (!cardsByName.TryGetValue(card.Name, out CardInfo? cardInfo))
            {
                missingCards.Add(card.Name);
                continue;
            }

            card.ScryfallId = cardInfo.Id;
            card.ScryfallOracleId = cardInfo.OracleId;
            ApplyCardSnapshot(card, cardInfo);
            updatedCards++;
        }

        return new DeckNormalizationResult
        {
            WorkspaceId = workspace.Id,
            Scope = normalizedScope,
            RequestedCards = targetCards.Count,
            UpdatedCards = updatedCards,
            MissingCards = missingCards,
            Workspace = workspace
        };
    }

    /// <summary>
    /// Summarizes the deck plan.
    /// </summary>
    public async Task<DeckPlanSummary> SummarizeDeckPlanAsync(
        string workspaceId,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        DeckIntent? intent = DeckIntentText.Extract(workspace.Description, workspace.Id).Intent;
        DeckPlanSummary summary = new()
        {
            WorkspaceId = workspace.Id,
            Name = workspace.Name,
            Format = workspace.Format,
            Intent = intent,
            Persistence = DeckPersistence.For(workspace),
            IncludedCards = IncludedCards(workspace).Sum(card => Math.Max(0, card.Quantity)),
            MaybeboardCards = workspace.Cards
                .Where(card => DeckCategoryOrdering.PrimaryCategory(card).Equals(DeckDefaults.Maybeboard, StringComparison.OrdinalIgnoreCase))
                .Sum(card => Math.Max(0, card.Quantity))
        };

        foreach (DeckCard card in IncludedCards(workspace))
        {
            CardRoleAssignment assignment = DeckRoleClassifier.Classify(card);
            AddCount(summary.RoleCounts, assignment.PrimaryRole, card.Quantity);
            foreach (string tag in assignment.Tags)
            {
                AddCount(summary.TagCounts, tag, card.Quantity);
            }

            if (assignment.PrimaryRole == DeckRoles.Commander)
            {
                summary.Commanders.Add(card.Name);
            }
        }

        foreach (DeckCategory category in workspace.Categories)
        {
            string suggestedRole = SuggestRoleForCategory(workspace, category.Name);
            summary.CategoryMap[category.Name] = suggestedRole;
        }

        AddSummaryNotes(summary, intent);
        return summary;
    }

    /// <summary>
    /// Analyzes draw odds for deck targets.
    /// </summary>
    public async Task<DeckOddsAnalysis> AnalyzeDrawOddsAsync(
        string workspaceId,
        string? targets,
        int turn,
        int openingHandSize,
        int simulations,
        int seed,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        DeckIntent? intent = DeckIntentText.Extract(workspace.Description, workspace.Id).Intent;
        List<string> requestedTargets = ParseTargets(targets, intent);
        return DeckStatistics.AnalyzeDrawOdds(
            workspace,
            requestedTargets,
            Math.Max(1, turn),
            Math.Clamp(openingHandSize, 1, 20),
            simulations,
            seed);
    }

    /// <summary>
    /// Suggests deck categories.
    /// </summary>
    public async Task<CategoryPlanResult> SuggestDeckCategoriesAsync(
        string workspaceId,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        List<CategorySuggestion> suggestions = [];
        DeckEditPlan plan = CreatePlan(workspace, "Category cleanup plan", "category-cleanup");
        plan.Rationale = "Groups cards into the standard role taxonomy while preserving existing deck contents until the plan is applied.";

        foreach (string role in DeckRoles.Primary)
        {
            if (role is DeckRoles.Commander or DeckRoles.Maybeboard)
            {
                continue;
            }

            if (!workspace.Categories.Any(category => category.Name.Equals(role, StringComparison.OrdinalIgnoreCase)))
            {
                plan.Operations.Add(new DeckEditOperation
                {
                    Operation = DeckEditOperations.CreateCategory,
                    Category = role,
                    IncludedInDeck = true,
                    IncludedInPrice = true,
                    Rationale = $"Create standard role category {role}."
                });
            }
        }

        foreach (DeckCard card in workspace.Cards)
        {
            CardRoleAssignment assignment = DeckRoleClassifier.Classify(card);
            suggestions.Add(new CategorySuggestion
            {
                CardName = card.Name,
                CurrentPrimaryCategory = DeckCategoryOrdering.PrimaryCategory(card),
                SuggestedPrimaryRole = assignment.PrimaryRole,
                Tags = assignment.Tags,
                Confidence = assignment.Confidence
            });

            if (assignment.PrimaryRole is DeckRoles.Commander or DeckRoles.Maybeboard)
            {
                continue;
            }

            string primaryCategory = DeckCategoryOrdering.PrimaryCategory(card);
            if (!string.Equals(primaryCategory, assignment.PrimaryRole, StringComparison.OrdinalIgnoreCase)
                && assignment.Confidence >= 0.55)
            {
                plan.Operations.Add(new DeckEditOperation
                {
                    Operation = DeckEditOperations.MoveCard,
                    CardName = card.Name,
                    FromCategory = primaryCategory,
                    ToCategory = assignment.PrimaryRole,
                    Rationale = $"Classified as {assignment.PrimaryRole} with {assignment.Confidence:0.00} confidence."
                });
            }
        }

        plan.Confidence = suggestions.Count == 0 ? 0 : suggestions.Average(suggestion => suggestion.Confidence);
        await RequirePlanRepository().SaveAsync(plan, cancellationToken).ConfigureAwait(false);

        return new CategoryPlanResult { Plan = plan, Suggestions = suggestions };
    }

    /// <summary>
    /// Lists deck edit plans.
    /// </summary>
    public Task<IReadOnlyList<DeckEditPlan>> ListDeckPlansAsync(
        string? workspaceId,
        CancellationToken cancellationToken
    )
    {
        return RequirePlanRepository().ListAsync(workspaceId, cancellationToken);
    }

    /// <summary>
    /// Gets a deck edit plan.
    /// </summary>
    public async Task<DeckEditPlan> GetDeckPlanAsync(
        string planId,
        CancellationToken cancellationToken
    )
    {
        return await RequirePlanRepository().GetAsync(planId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Deck edit plan '{planId}' was not found.");
    }

    /// <summary>
    /// Deletes a deck edit plan.
    /// </summary>
    public Task DeleteDeckPlanAsync(string planId, CancellationToken cancellationToken)
    {
        return RequirePlanRepository().DeleteAsync(planId, cancellationToken);
    }

    /// <summary>
    /// Applies a deck edit plan.
    /// </summary>
    public async Task<DeckEditPlanApplyResult> ApplyDeckPlanAsync(
        string planId,
        bool createCheckpoint,
        string? checkpointName,
        CancellationToken cancellationToken)
    {
        IDeckPlanRepository plans = RequirePlanRepository();
        DeckEditPlan plan = await GetDeckPlanAsync(planId, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(plan.Status)
            && !plan.Status.Equals(DeckEditPlanStatus.Draft, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Deck edit plan '{plan.PlanId}' has already been applied.");
        }

        DeckWorkspace workspace = await LoadWorkspaceAsync(plan.WorkspaceId, cancellationToken).ConfigureAwait(false);
        string? checkpointId = null;

        if (workspace.Mode == WorkspaceMode.Archidekt && workspace.WriteBack && plan.Operations.Count > 1)
        {
            if (!createCheckpoint)
            {
                throw new InvalidOperationException("Applying a multi-edit plan to an Archidekt writeback workspace requires a checkpoint.");
            }

            DeckCheckpoint checkpoint = await CheckpointDeckAsync(
                workspace.Id,
                string.IsNullOrWhiteSpace(checkpointName) ? $"Before {plan.Name}" : checkpointName,
                $"Created before applying plan {plan.PlanId}.",
                cancellationToken).ConfigureAwait(false);
            checkpointId = checkpoint.Id;
        }

        List<string> messages = [];
        foreach (DeckEditOperation operation in plan.Operations)
        {
            DeckChangeResult? result = await ApplyOperationAsync(plan.WorkspaceId, operation, cancellationToken).ConfigureAwait(false);
            if (result is not null)
            {
                messages.Add(result.Message);
            }
        }

        DeckWorkspace updatedWorkspace = await LoadWorkspaceAsync(plan.WorkspaceId, cancellationToken).ConfigureAwait(false);
        plan.Status = DeckEditPlanStatus.Applied;
        plan.AppliedAt = DateTimeOffset.UtcNow;
        plan.CheckpointId = checkpointId;
        await plans.SaveAsync(plan, cancellationToken).ConfigureAwait(false);

        return new DeckEditPlanApplyResult
        {
            PlanId = plan.PlanId,
            WorkspaceId = plan.WorkspaceId,
            Persistence = DeckPersistence.For(updatedWorkspace),
            CheckpointId = checkpointId,
            AppliedOperations = plan.Operations.Count,
            Messages = messages,
            Workspace = updatedWorkspace
        };
    }

    /// <summary>
    /// Applies one deck edit step.
    /// </summary>
    private async Task<DeckChangeResult?> ApplyOperationAsync(
        string workspaceId,
        DeckEditOperation operation,
        CancellationToken cancellationToken)
    {
        return operation.Operation switch
        {
            DeckEditOperations.AddCard => await AddCardAsync(
                workspaceId,
                Require(operation.CardName, "cardName"),
                operation.Quantity ?? 1,
                operation.Category ?? DeckDefaults.Mainboard,
                cancellationToken).ConfigureAwait(false),
            DeckEditOperations.RemoveCard => await RemoveCardAsync(
                workspaceId,
                Require(operation.CardName, "cardName"),
                operation.Quantity ?? 1,
                operation.Category,
                cancellationToken).ConfigureAwait(false),
            DeckEditOperations.SetCardQuantity => await SetCardQuantityAsync(
                workspaceId,
                Require(operation.CardName, "cardName"),
                operation.Quantity ?? 1,
                operation.Category,
                cancellationToken).ConfigureAwait(false),
            DeckEditOperations.MoveCard => await MoveCardAsync(
                workspaceId,
                Require(operation.CardName, "cardName"),
                Require(operation.ToCategory, "toCategory"),
                operation.FromCategory,
                cancellationToken).ConfigureAwait(false),
            DeckEditOperations.AddCardCategory => await AddCardCategoryAsync(
                workspaceId,
                Require(operation.CardName, "cardName"),
                Require(operation.Category, "category"),
                cancellationToken).ConfigureAwait(false),
            DeckEditOperations.RemoveCardCategory => await RemoveCardCategoryAsync(
                workspaceId,
                Require(operation.CardName, "cardName"),
                Require(operation.Category, "category"),
                cancellationToken).ConfigureAwait(false),
            DeckEditOperations.SetPrimaryCardCategory => await SetPrimaryCardCategoryAsync(
                workspaceId,
                Require(operation.CardName, "cardName"),
                Require(operation.Category, "category"),
                cancellationToken).ConfigureAwait(false),
            DeckEditOperations.CreateCategory => await CreateCategoryAsync(
                workspaceId,
                Require(operation.Category, "category"),
                operation.IncludedInDeck ?? true,
                operation.IncludedInPrice ?? true,
                cancellationToken).ConfigureAwait(false),
            DeckEditOperations.RenameCategory => await RenameCategoryAsync(
                workspaceId,
                Require(operation.FromCategory, "fromCategory"),
                Require(operation.ToCategory, "toCategory"),
                cancellationToken).ConfigureAwait(false),
            DeckEditOperations.DeleteCategory => await DeleteCategoryAsync(
                workspaceId,
                Require(operation.Category, "category"),
                operation.ToCategory ?? DeckDefaults.Mainboard,
                cancellationToken).ConfigureAwait(false),
            DeckEditOperations.UpdateDeckMetadata => await UpdateDeckMetadataAsync(
                workspaceId,
                operation.Name,
                operation.Format,
                operation.Description,
                cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unknown deck edit operation '{operation.Operation}'.")
        };
    }

    /// <summary>
    /// Determines whether a card should be normalized.
    /// </summary>
    private static bool ShouldNormalize(DeckCard card, DeckWorkspace workspace, string scope)
    {
        return scope switch
        {
            "all" => true,
            "included" => IsIncluded(workspace, card),
            "maybeboard" => DeckCategoryOrdering.PrimaryCategory(card).Equals(DeckDefaults.Maybeboard, StringComparison.OrdinalIgnoreCase),
            "missing" => string.IsNullOrWhiteSpace(GetSnapshot(card).TypeLine)
                || string.IsNullOrWhiteSpace(GetSnapshot(card).OracleText)
                || GetSnapshot(card).Prices.Count == 0,
            _ => true
        };
    }

    /// <summary>
    /// Enumerates included workspace cards.
    /// </summary>
    private static IEnumerable<DeckCard> IncludedCards(DeckWorkspace workspace)
    {
        return DeckCategoryInclusion.IncludedCards(workspace);
    }

    /// <summary>
    /// Determines whether a card is included in the deck.
    /// </summary>
    private static bool IsIncluded(DeckWorkspace workspace, DeckCard card)
    {
        return DeckCategoryInclusion.IsIncludedInDeck(workspace, card);
    }

    /// <summary>
    /// Parses draw odds targets.
    /// </summary>
    private static List<string> ParseTargets(string? targets, DeckIntent? intent)
    {
        if (string.IsNullOrWhiteSpace(targets))
        {
            if (intent?.Targets.Count > 0)
            {
                return intent.Targets.Keys
                    .Where(target => DeckRoles.Primary.Contains(target, StringComparer.OrdinalIgnoreCase)
                        || DeckTags.Secondary.Contains(target, StringComparer.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return
            [
                DeckRoles.Lands,
                DeckRoles.Ramp,
                DeckRoles.Draw,
                DeckRoles.Interaction,
                DeckRoles.BoardWipes,
                DeckTags.Discard
            ];
        }

        return targets
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(target => !string.IsNullOrWhiteSpace(target))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Adds summary notes.
    /// </summary>
    private static void AddSummaryNotes(DeckPlanSummary summary, DeckIntent? intent)
    {
        int lands = Count(summary.RoleCounts, DeckRoles.Lands);
        int ramp = Count(summary.RoleCounts, DeckRoles.Ramp);
        int draw = Count(summary.RoleCounts, DeckRoles.Draw);
        int interaction = Count(summary.RoleCounts, DeckRoles.Interaction) + Count(summary.RoleCounts, DeckRoles.BoardWipes);
        int landTarget = TargetMinimum(intent, DeckRoles.Lands, 35);
        int rampTarget = TargetMinimum(intent, DeckRoles.Ramp, 8);
        int drawTarget = TargetMinimum(intent, DeckRoles.Draw, 8);
        int interactionTarget = TargetMinimum(intent, DeckRoles.Interaction, 8);

        if (intent is not null)
        {
            summary.IntentNotes.Add("Summary thresholds are using the deck intent stored in the description.");
            if (!string.IsNullOrWhiteSpace(intent.Archetype))
            {
                summary.IntentNotes.Add($"Intent archetype: {intent.Archetype}.");
            }
        }

        if (lands >= landTarget)
        {
            summary.Strengths.Add("Land count looks healthy for Commander.");
        }
        else
        {
            summary.Risks.Add("Land count may be low for a Commander deck.");
        }

        if (ramp >= rampTarget)
        {
            summary.Strengths.Add("Ramp density is in a strong range.");
        }
        else
        {
            summary.Risks.Add("Ramp count may be light.");
        }

        if (draw >= drawTarget)
        {
            summary.Strengths.Add("Card draw appears well represented.");
        }
        else
        {
            summary.Risks.Add("Card draw may need reinforcement.");
        }

        if (interaction < interactionTarget)
        {
            summary.Risks.Add("Interaction and board wipe density may be low.");
        }

        summary.NextSteps.Add("Run analyze_draw_odds for lands, ramp, draw, discard, interaction, and board wipes.");
        summary.NextSteps.Add("Run suggest_deck_categories before applying category changes.");
    }

    /// <summary>
    /// Reads the minimum target for a role.
    /// </summary>
    private static int TargetMinimum(DeckIntent? intent, string role, int fallback)
    {
        return intent?.Targets.TryGetValue(role, out DeckIntentTarget? target) == true
            ? target.Minimum ?? fallback
            : fallback;
    }

    /// <summary>
    /// Suggests a role for a category.
    /// </summary>
    private static string SuggestRoleForCategory(DeckWorkspace workspace, string category)
    {
        Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);
        foreach (DeckCard card in workspace.Cards.Where(card => (card.Categories ?? []).Any(value => value.Equals(category, StringComparison.OrdinalIgnoreCase))))
        {
            CardRoleAssignment assignment = DeckRoleClassifier.Classify(card);
            AddCount(counts, assignment.PrimaryRole, card.Quantity);
        }

        return counts.OrderByDescending(pair => pair.Value).FirstOrDefault().Key ?? DeckRoles.Utility;
    }

    /// <summary>
    /// Creates a deck edit plan.
    /// </summary>
    private static DeckEditPlan CreatePlan(DeckWorkspace workspace, string name, string kind)
    {
        return new DeckEditPlan
        {
            WorkspaceId = workspace.Id,
            Name = name,
            Kind = kind,
            Persistence = DeckPersistence.For(workspace)
        };
    }

    /// <summary>
    /// Gets a card snapshot safely.
    /// </summary>
    private static CardSnapshot GetSnapshot(DeckCard card)
    {
        return card.Snapshot ?? new CardSnapshot();
    }

    /// <summary>
    /// Adds a quantity to a count dictionary.
    /// </summary>
    private static void AddCount(Dictionary<string, int> counts, string key, int quantity)
    {
        counts[key] = counts.GetValueOrDefault(key) + Math.Max(0, quantity);
    }

    /// <summary>
    /// Gets a count value.
    /// </summary>
    private static int Count(Dictionary<string, int> counts, string key)
    {
        return counts.TryGetValue(key, out int count) ? count : 0;
    }

    /// <summary>
    /// Requires an operation value.
    /// </summary>
    private static string Require(string? value, string name)
    {
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Deck edit operation is missing required field '{name}'.");
    }

    /// <summary>
    /// Requires the plan repository.
    /// </summary>
    private IDeckPlanRepository RequirePlanRepository()
    {
        return planRepository ?? throw new InvalidOperationException("Deck edit plan persistence is not configured.");
    }
}
