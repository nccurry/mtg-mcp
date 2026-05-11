namespace MtgMcp.Core;

/// <summary>
/// Provides Commander metagame comparison behavior.
/// </summary>
public sealed partial class DeckRecommendationService : DeckServiceBase
{
    /// <summary>
    /// Compares a deck with optional Commander metagame data.
    /// </summary>
    public async Task<CommanderMetaReport> CompareToCommanderMetaAsync(
        string workspaceId,
        int limit,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        DeckIntent? intent = DeckIntentText.Extract(workspace.Description, workspace.Id).Intent;
        CommanderMetaQuery query = new()
        {
            Commander = intent?.Commander ?? FindCommanderName(workspace),
            Theme = intent?.Archetype,
            Format = workspace.Format,
            Limit = Math.Clamp(limit, 1, 100)
        };
        CommanderMetaReport report;
        bool providerFallback = false;
        if (CommanderMetaProvider is null)
        {
            report = BuildHeuristicMetaReport(workspace, query);
        }
        else
        {
            try
            {
                report = await CommanderMetaProvider.GetCommanderMetaAsync(query, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (!IsCancellation(exception))
            {
                report = BuildHeuristicMetaReport(workspace, query);
                providerFallback = true;
                report.Notes.Add($"Commander meta provider failed; using built-in heuristic fallback. {exception.GetType().Name}: {exception.Message}");
            }
        }

        report.WorkspaceId = workspace.Id;
        report.Commander ??= query.Commander;
        report.Theme ??= query.Theme;
        HashSet<string> existing = workspace.Cards.Select(card => card.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        report.IncludedPopularCards = report.PopularCards
            .Where(card => existing.Contains(card.Name))
            .Take(query.Limit)
            .ToList();
        report.MissingPopularCards = report.PopularCards
            .Where(card => !existing.Contains(card.Name))
            .Take(query.Limit)
            .ToList();
        if (CommanderMetaProvider is null)
        {
            report.Notes.Add("No Commander meta provider is configured; using built-in staple heuristics.");
        }
        else if (providerFallback)
        {
            report.Source = "built-in commander staple heuristics after provider fallback";
        }

        return report;
    }

    /// <summary>
    /// Creates a plan for popular cards missing from a deck.
    /// </summary>
    public async Task<GoalPackagePlanResult> FindMissingPopularCardsAsync(
        string workspaceId,
        int limit,
        decimal? maxPrice,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        CommanderMetaReport report = await CompareToCommanderMetaAsync(workspaceId, Math.Clamp(limit, 1, 25), cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyDictionary<string, CardInfo> cards = await CardCatalog
            .GetCardsByNamesAsync(report.MissingPopularCards.Select(card => card.Name).ToList(), cancellationToken)
            .ConfigureAwait(false);
        (bool colorKnown, HashSet<string> colors) = GetDeckColorIdentity(workspace);
        DeckEditPlan plan = CreatePlan(workspace, "Missing popular cards plan", "missing-popular-cards");
        List<GoalCardSuggestion> suggestions = [];

        foreach (CommanderMetaCard metaCard in report.MissingPopularCards)
        {
            if (!cards.TryGetValue(metaCard.Name, out CardInfo? card)
                || !IsLegalInFormat(card, workspace.Format)
                || !IsInDeckColorIdentity(card, colorKnown, colors)
                || (maxPrice.HasValue && ReadUsdPrice(card).GetValueOrDefault(decimal.MaxValue) > maxPrice.Value))
            {
                continue;
            }

            DeckCard candidate = CreateCandidateCard(card);
            CardRoleAssignment role = DeckRoleClassifier.Classify(candidate);
            suggestions.Add(new GoalCardSuggestion
            {
                CardName = card.Name,
                Role = role.PrimaryRole,
                Tags = role.Tags,
                FitScore = Math.Clamp(0.60 + metaCard.InclusionRate + metaCard.SynergyScore, 0, 1),
                Price = ReadUsdPrice(card),
                Rationale = $"{card.Name} is a popular {metaCard.Category} candidate from {report.Source}."
            });
            plan.Operations.Add(CreateAddOperation(card, role.PrimaryRole, $"Add popular card from {report.Source}: {metaCard.Category}."));
            if (plan.Operations.Count >= Math.Clamp(limit, 1, 25))
            {
                break;
            }
        }

        plan.Rationale = "Adds high-context popular cards missing from the current commander or theme profile.";
        plan.Confidence = suggestions.Count == 0 ? 0 : suggestions.Average(suggestion => suggestion.FitScore);
        if (suggestions.Count == 0)
        {
            plan.Warnings.Add("No missing popular cards met legality, color identity, and price filters.");
        }

        await RequirePlanRepository().SaveAsync(plan, cancellationToken).ConfigureAwait(false);
        return new GoalPackagePlanResult
        {
            Plan = plan,
            Goal = "missing popular cards",
            Strategy = "commander-meta",
            Suggestions = suggestions
        };
    }

    /// <summary>
    /// Builds a heuristic staple report when no external provider is configured.
    /// </summary>
    private static CommanderMetaReport BuildHeuristicMetaReport(DeckWorkspace workspace, CommanderMetaQuery query)
    {
        CommanderMetaReport report = new()
        {
            WorkspaceId = workspace.Id,
            Commander = query.Commander,
            Theme = query.Theme,
            Source = "built-in commander staple heuristics"
        };
        report.PopularCards.AddRange(
        [
            MetaCard("Sol Ring", "staple", 0.90, 0.10),
            MetaCard("Arcane Signet", "staple", 0.80, 0.10),
            MetaCard("Command Tower", "mana base", 0.80, 0.10),
            MetaCard("Lightning Greaves", "protection", 0.45, 0.10),
            MetaCard("Swiftfoot Boots", "protection", 0.40, 0.10),
            MetaCard("Beast Within", "interaction", 0.35, 0.10),
            MetaCard("Generous Gift", "interaction", 0.35, 0.10),
            MetaCard("Swords to Plowshares", "interaction", 0.35, 0.10),
            MetaCard("Chaos Warp", "interaction", 0.30, 0.10),
            MetaCard("Heroic Intervention", "protection", 0.30, 0.10),
            MetaCard("Skullclamp", "engine", 0.25, 0.15),
            MetaCard("Bojuka Bog", "graveyard hate", 0.25, 0.10)
        ]);
        report.PopularCards = report.PopularCards.Take(Math.Clamp(query.Limit, 1, 100)).ToList();
        return report;
    }

    /// <summary>
    /// Creates a Commander meta card.
    /// </summary>
    private static CommanderMetaCard MetaCard(string name, string category, double inclusionRate, double synergyScore)
    {
        return new CommanderMetaCard
        {
            Name = name,
            Category = category,
            InclusionRate = inclusionRate,
            SynergyScore = synergyScore,
            Source = "built-in"
        };
    }

}

