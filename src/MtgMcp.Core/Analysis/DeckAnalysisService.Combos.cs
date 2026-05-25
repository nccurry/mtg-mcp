namespace MtgMcp.Core;

/// <summary>
/// Provides combo catalog and pressure behavior.
/// </summary>
public sealed partial class DeckAnalysisService : DeckServiceBase
{
    /// <summary>
    /// Finds combos that are already present in a deck.
    /// </summary>
    public async Task<DeckComboReport> FindDeckCombosAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        DeckComboReport report = await BuildComboReportAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        return FilterComboReport(report, includeCombos: true, includeNearMisses: false);
    }

    /// <summary>
    /// Finds combo near misses in a deck.
    /// </summary>
    public async Task<DeckComboReport> FindNearMissCombosAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        DeckComboReport report = await BuildComboReportAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        return FilterComboReport(report, includeCombos: false, includeNearMisses: true);
    }

    /// <summary>
    /// Estimates combo pressure for a deck.
    /// </summary>
    public async Task<ComboPressureEstimate> EstimateComboPressureAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        DeckComboReport report = await BuildComboReportAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        return report.Pressure;
    }

    /// <summary>
    /// Builds combo analysis from an external catalog or local heuristics.
    /// </summary>
    private async Task<DeckComboReport> BuildComboReportAsync(string workspaceId, CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        ComboCatalogQuery query = new()
        {
            CardNames = IncludedCards(workspace).Select(card => card.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Commander = FindCommanderName(workspace),
            Format = workspace.Format
        };
        DeckComboReport report;
        bool providerFallback = false;
        if (comboCatalog is null)
        {
            report = BuildHeuristicComboReport(workspace);
        }
        else
        {
            try
            {
                report = await comboCatalog.FindCombosAsync(query, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (!IsCancellation(exception))
            {
                report = BuildHeuristicComboReport(workspace);
                providerFallback = true;
                report.Notes.Add($"Combo catalog failed; using local combo-tag heuristics. {exception.GetType().Name}: {exception.Message}");
            }
        }

        report.WorkspaceId = workspace.Id;
        report.Pressure = BuildComboPressure(workspace, report);
        if (comboCatalog is null)
        {
            report.Notes.Add("No combo catalog is configured; using local combo-tag heuristics.");
        }
        else if (providerFallback)
        {
            report.Notes.Add("External combo catalog was unavailable for this run.");
        }

        return report;
    }

    /// <summary>
    /// Copies a combo report while selecting completed combos or near misses.
    /// </summary>
    private static DeckComboReport FilterComboReport(
        DeckComboReport report,
        bool includeCombos,
        bool includeNearMisses)
    {
        return new DeckComboReport
        {
            WorkspaceId = report.WorkspaceId,
            Combos = includeCombos ? report.Combos.ToList() : [],
            NearMisses = includeNearMisses ? report.NearMisses.ToList() : [],
            Pressure = report.Pressure,
            Notes = report.Notes.ToList()
        };
    }

    /// <summary>
    /// Builds local combo detection from tags and known two-card patterns.
    /// </summary>
    private static DeckComboReport BuildHeuristicComboReport(DeckWorkspace workspace)
    {
        DeckComboReport report = new() { WorkspaceId = workspace.Id };
        List<DeckCard> comboCards = IncludedCards(workspace)
            .Where(card => DeckRoleClassifier.Classify(card).Tags.Any(tag => tag is DeckTags.ComboPiece or DeckTags.ComboEnabler))
            .ToList();
        int distinctComboCards = comboCards.Select(card => card.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (distinctComboCards >= 2)
        {
            report.Combos.Add(new DeckCombo
            {
                Name = "Heuristic combo shell",
                Cards = comboCards.Take(4).Select(card => card.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                WinRoute = "assembled engine or loop",
                Kind = "combo",
                Confidence = 0.55,
                Rationale = "Multiple cards are tagged as combo pieces or enablers."
            });
        }

        AddKnownCombo(report, workspace, "Exquisite Blood", "Sanguine Bond", "life-drain loop");
        AddKnownCombo(report, workspace, "Heliod, Sun-Crowned", "Walking Ballista", "damage loop");
        AddKnownCombo(report, workspace, "Kiki-Jiki, Mirror Breaker", "Zealous Conscripts", "infinite creatures");
        if (comboCards.Count == 1)
        {
            report.NearMisses.Add(new DeckCombo
            {
                Name = "Single combo-piece near miss",
                Cards = [comboCards[0].Name],
                MissingCards = ["supporting combo piece"],
                WinRoute = "unknown",
                Kind = "near-miss",
                Confidence = 0.35,
                Rationale = "One card is tagged as a combo piece or enabler."
            });
        }

        return report;
    }

    /// <summary>
    /// Adds a known combo or near miss.
    /// </summary>
    private static void AddKnownCombo(DeckComboReport report, DeckWorkspace workspace, string first, string second, string route)
    {
        HashSet<string> names = workspace.Cards.Select(card => card.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool hasFirst = names.Contains(first);
        bool hasSecond = names.Contains(second);
        if (hasFirst && hasSecond)
        {
            report.Combos.Add(new DeckCombo
            {
                Name = $"{first} + {second}",
                Cards = [first, second],
                WinRoute = route,
                Kind = "known-combo",
                Confidence = 0.85,
                Rationale = "Known two-card combo detected from local pattern data."
            });
        }
        else if (hasFirst || hasSecond)
        {
            report.NearMisses.Add(new DeckCombo
            {
                Name = $"{first} + {second}",
                Cards = [hasFirst ? first : second],
                MissingCards = [hasFirst ? second : first],
                WinRoute = route,
                Kind = "known-near-miss",
                Confidence = 0.65,
                Rationale = "One card from a known two-card combo is present."
            });
        }
    }

    /// <summary>
    /// Builds combo pressure from combo findings and deck tags.
    /// </summary>
    private static ComboPressureEstimate BuildComboPressure(DeckWorkspace workspace, DeckComboReport report)
    {
        int tutorCount = IncludedCards(workspace).Count(card => DeckRoleClassifier.Classify(card).PrimaryRole.Equals(DeckRoles.Tutors, StringComparison.OrdinalIgnoreCase));
        int comboTagCount = IncludedCards(workspace)
            .Where(card => DeckRoleClassifier.Classify(card).Tags.Contains(DeckTags.ComboPiece))
            .Sum(card => Math.Max(0, card.Quantity));
        double score = Math.Clamp((report.Combos.Count * 0.30) + (report.NearMisses.Count * 0.12) + (tutorCount * 0.06) + (comboTagCount * 0.08), 0, 1);
        ComboPressureEstimate pressure = new()
        {
            WorkspaceId = workspace.Id,
            Score = score,
            Level = score >= 0.65 ? "high" : score >= 0.30 ? "medium" : "low"
        };
        if (report.Combos.Count > 0)
        {
            pressure.Signals.Add($"{report.Combos.Count} completed combo candidate(s).");
        }

        if (report.NearMisses.Count > 0)
        {
            pressure.Signals.Add($"{report.NearMisses.Count} combo near miss(es).");
        }

        if (tutorCount >= 3)
        {
            pressure.Signals.Add("Tutor density increases combo consistency.");
        }

        pressure.Notes.Add("Pressure is heuristic unless a dedicated combo catalog is configured.");
        return pressure;
    }

}
