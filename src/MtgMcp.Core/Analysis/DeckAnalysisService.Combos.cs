using System.Reflection;
using System.Text.Json;

namespace MtgMcp.Core;

/// <summary>
/// Provides combo catalog, route classification, and pressure behavior.
/// </summary>
public sealed partial class DeckAnalysisService : DeckServiceBase
{
    /// <summary>
    /// Identifies the embedded local combo pattern dataset.
    /// </summary>
    private const string LocalComboPatternResourceName = "MtgMcp.Core.LocalCombos.json";

    /// <summary>
    /// Reads local combo pattern JSON using web-style property names.
    /// </summary>
    private static readonly JsonSerializerOptions LocalComboPatternJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Lazily loads the bounded local combo pattern dataset.
    /// </summary>
    private static readonly Lazy<IReadOnlyList<LocalComboPattern>> LocalComboPatterns = new(LoadLocalComboPatterns);

    /// <summary>
    /// Finds combos that are already present in a deck.
    /// </summary>
    public async Task<DeckComboReport> FindDeckCombosAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        DeckComboReport report = await BuildComboReportAsync(
            workspaceId,
            includeHeuristics: true,
            refresh: false,
            cancellationToken).ConfigureAwait(false);
        return FilterComboReport(report, includeCombos: true, includeNearMisses: false);
    }

    /// <summary>
    /// Finds combo near misses in a deck.
    /// </summary>
    public async Task<DeckComboReport> FindNearMissCombosAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        DeckComboReport report = await BuildComboReportAsync(
            workspaceId,
            includeHeuristics: true,
            refresh: false,
            cancellationToken).ConfigureAwait(false);
        return FilterComboReport(report, includeCombos: false, includeNearMisses: true);
    }

    /// <summary>
    /// Estimates combo pressure for a deck.
    /// </summary>
    public async Task<ComboPressureEstimate> EstimateComboPressureAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        DeckComboReport report = await BuildComboReportAsync(
            workspaceId,
            includeHeuristics: true,
            refresh: false,
            cancellationToken).ConfigureAwait(false);
        return report.Pressure;
    }

    /// <summary>
    /// Analyzes completed combos, near misses, pressure, and route labels in one report.
    /// </summary>
    public async Task<DeckComboReport> AnalyzeCombosAsync(
        string workspaceId,
        bool includeNearMisses,
        bool includeHeuristics,
        bool refresh,
        CancellationToken cancellationToken)
    {
        DeckComboReport report = await BuildComboReportAsync(
            workspaceId,
            includeHeuristics,
            refresh,
            cancellationToken).ConfigureAwait(false);
        return FilterComboReport(report, includeCombos: true, includeNearMisses);
    }

    /// <summary>
    /// Finds combo catalog evidence containing one card.
    /// </summary>
    public async Task<ComboEvidenceSearchResult> SearchCombosByCardAsync(
        string cardNameOrId,
        string format,
        string? commanderName,
        bool strictColorIdentity,
        int limit,
        bool refresh,
        CancellationToken cancellationToken)
    {
        ComboEvidenceSearchResult result = new()
        {
            StrictColorIdentity = strictColorIdentity,
            Commander = string.IsNullOrWhiteSpace(commanderName) ? null : commanderName.Trim()
        };
        if (comboCatalog is null)
        {
            result.CardName = cardNameOrId;
            result.Notes.Add("No combo catalog is configured; combo search returned no catalog evidence.");
            return result;
        }

        CardInfo? card = await CardCatalog.GetCardAsync(cardNameOrId, cancellationToken).ConfigureAwait(false);
        string normalizedCardName = card?.Name ?? cardNameOrId.Trim();
        result.CardName = normalizedCardName;
        IReadOnlyList<ComboEvidence> combos = await comboCatalog.SearchCombosByCardAsync(
            new ComboCardSearchQuery
            {
                CardName = normalizedCardName,
                Format = string.IsNullOrWhiteSpace(format) ? "commander" : format.Trim().ToLowerInvariant(),
                Limit = Math.Clamp(limit, 1, 100),
                Refresh = refresh
            },
            cancellationToken).ConfigureAwait(false);
        List<ComboEvidence> enriched = [];
        foreach (ComboEvidence combo in combos)
        {
            await EnsureComboColorIdentityAsync(combo, cancellationToken).ConfigureAwait(false);
            EnsureRouteClassification(combo);
            enriched.Add(combo);
        }

        if (strictColorIdentity && !string.IsNullOrWhiteSpace(commanderName))
        {
            CardInfo? commander = await CardCatalog.GetCardAsync(commanderName, cancellationToken).ConfigureAwait(false);
            HashSet<string> commanderColors = (commander?.ColorIdentity ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
            result.Commander = commander?.Name ?? commanderName;
            result.Combos = enriched
                .Where(combo => IsColorSubset(combo.ColorIdentity, commanderColors))
                .Take(Math.Clamp(limit, 1, 100))
                .ToList();
            result.Notes.Add("Combos were filtered to Commander color identity using Scryfall card facts.");
        }
        else
        {
            result.Combos = enriched.Take(Math.Clamp(limit, 1, 100)).ToList();
            if (strictColorIdentity)
            {
                result.Notes.Add("strictColorIdentity was requested but no commanderName was supplied, so no commander color filter was applied.");
            }
        }

        result.Notes.Add("Commander Spellbook rows are catalog evidence, not formal proof that a line works in every possible game state.");
        return result;
    }

    /// <summary>
    /// Gets raw-preserving combo catalog details for one combo id.
    /// </summary>
    public async Task<ComboEvidence?> GetComboDetailsAsync(
        string comboId,
        bool refresh,
        CancellationToken cancellationToken)
    {
        if (comboCatalog is null)
        {
            return null;
        }

        ComboEvidence? evidence = await comboCatalog.GetComboDetailsAsync(
            new ComboDetailsQuery { ComboId = comboId, Refresh = refresh },
            cancellationToken).ConfigureAwait(false);
        if (evidence is not null)
        {
            await EnsureComboColorIdentityAsync(evidence, cancellationToken).ConfigureAwait(false);
            EnsureRouteClassification(evidence);
        }

        return evidence;
    }

    /// <summary>
    /// Classifies cards, combo ids, produced features, or a workspace into win routes.
    /// </summary>
    public async Task<WinRouteClassificationResult> ClassifyWinRoutesAsync(
        IReadOnlyList<string>? cardNames,
        string? workspaceId,
        string? comboId,
        IReadOnlyList<string>? producedFeatures,
        string format,
        CancellationToken cancellationToken)
    {
        int inputs = CountProvided(cardNames) + CountProvided(workspaceId) + CountProvided(comboId) + CountProvided(producedFeatures);
        if (inputs != 1)
        {
            throw new ArgumentException("Provide exactly one of cardNames, workspaceId, comboId, or producedFeatures.");
        }

        WinRouteClassificationResult result = new();
        if (CountProvided(producedFeatures) == 1)
        {
            result.InputKind = "produced-features";
            result.Classifications.Add(WinRouteClassifier.ClassifyProducedFeatures("produced features", producedFeatures!));
            return result;
        }

        if (!string.IsNullOrWhiteSpace(comboId))
        {
            result.InputKind = "combo-id";
            ComboEvidence? details = await GetComboDetailsAsync(comboId, refresh: false, cancellationToken).ConfigureAwait(false);
            if (details is null)
            {
                result.Notes.Add("No combo catalog details were available for the requested combo id.");
                return result;
            }

            result.Classifications.AddRange(details.RouteClassifications);
            return result;
        }

        if (!string.IsNullOrWhiteSpace(workspaceId))
        {
            DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
            result.InputKind = "workspace";
            result.WorkspaceId = workspace.Id;
            foreach (DeckCard deckCard in DeckServiceHelpers.IncludedCards(workspace))
            {
                result.Classifications.Add(WinRouteClassifier.ClassifyCard(CreateCardInfoFromDeckCard(deckCard, format)));
            }

            result.Classifications = result.Classifications
                .Where(classification => classification.RouteTypes.Count > 0)
                .ToList();
            return result;
        }

        result.InputKind = "card-names";
        IReadOnlyDictionary<string, CardInfo> cards = await CardCatalog.GetCardsByNamesAsync(
            cardNames!
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            cancellationToken).ConfigureAwait(false);
        foreach (CardInfo cardInfo in cards.Values)
        {
            result.Classifications.Add(WinRouteClassifier.ClassifyCard(cardInfo));
        }

        return result;
    }

    /// <summary>
    /// Builds combo analysis from an external catalog or local heuristics.
    /// </summary>
    private async Task<DeckComboReport> BuildComboReportAsync(
        string workspaceId,
        bool includeHeuristics,
        bool refresh,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        ComboCatalogQuery query = new()
        {
            CardNames = DeckServiceHelpers.IncludedCards(workspace).Select(card => card.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Commander = FindCommanderName(workspace),
            Format = workspace.Format,
            Refresh = refresh
        };
        DeckComboReport report;
        bool providerFallback = false;
        if (comboCatalog is null)
        {
            report = includeHeuristics
                ? BuildHeuristicComboReport(workspace)
                : new DeckComboReport { WorkspaceId = workspace.Id };
        }
        else
        {
            try
            {
                report = await comboCatalog.FindCombosAsync(query, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (!IsCancellation(exception))
            {
                report = includeHeuristics
                    ? BuildHeuristicComboReport(workspace)
                    : new DeckComboReport { WorkspaceId = workspace.Id };
                providerFallback = true;
                report.Notes.Add(includeHeuristics
                    ? $"Combo catalog failed; using local combo-tag heuristics. {exception.GetType().Name}: {exception.Message}"
                    : $"Combo catalog failed and heuristics were disabled. {exception.GetType().Name}: {exception.Message}");
            }
        }

        AddRouteClassifications(report);
        report.WorkspaceId = workspace.Id;
        report.Pressure = BuildComboPressure(workspace, report);
        if (comboCatalog is null)
        {
            report.Notes.Add(includeHeuristics
                ? "No combo catalog is configured; using local combo-tag heuristics."
                : "No combo catalog is configured and local combo heuristics were disabled.");
        }
        else if (providerFallback)
        {
            report.Notes.Add("External combo catalog was unavailable for this run.");
        }

        if (report.Combos.Any(combo => combo.Source.Equals("commander-spellbook", StringComparison.OrdinalIgnoreCase))
            || report.NearMisses.Any(combo => combo.Source.Equals("commander-spellbook", StringComparison.OrdinalIgnoreCase)))
        {
            report.Notes.Add("Commander Spellbook rows are catalog evidence, not formal proof that a line works in every possible game state.");
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
        List<DeckCard> comboCards = DeckServiceHelpers.IncludedCards(workspace)
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
                Kind = "heuristic-signal",
                Confidence = 0.55,
                Source = "local-heuristic",
                Rationale = "Multiple cards are tagged as combo pieces or enablers.",
                Metadata = new SourceEvidenceMetadata
                {
                    Source = "mtg-mcp",
                    SourceKind = "heuristic-signal",
                    CacheStatus = "local",
                    Confidence = 0.55,
                    Deterministic = true,
                    Notes = ["This is a heuristic signal, not confirmed catalog combo evidence."]
                }
            });
        }

        AddLocalComboPatterns(report, workspace);
        if (comboCards.Count == 1)
        {
            report.NearMisses.Add(new DeckCombo
            {
                Name = "Single combo-piece near miss",
                Cards = [comboCards[0].Name],
                MissingCards = ["supporting combo piece"],
                WinRoute = "unknown",
                Kind = "heuristic-near-miss",
                Confidence = 0.35,
                Source = "local-heuristic",
                Rationale = "One card is tagged as a combo piece or enabler.",
                Metadata = new SourceEvidenceMetadata
                {
                    Source = "mtg-mcp",
                    SourceKind = "heuristic-signal",
                    CacheStatus = "local",
                    Confidence = 0.35,
                    Deterministic = true,
                    Notes = ["This is a heuristic signal, not confirmed catalog combo evidence."]
                }
            });
        }

        return report;
    }

    /// <summary>
    /// Adds known local combo dataset matches and near misses.
    /// </summary>
    private static void AddLocalComboPatterns(DeckComboReport report, DeckWorkspace workspace)
    {
        HashSet<string> names = DeckServiceHelpers.IncludedCards(workspace)
            .Select(card => card.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (LocalComboPattern pattern in LocalComboPatterns.Value)
        {
            List<string> present = [];
            List<string> missing = [];
            foreach (string card in pattern.Cards)
            {
                if (names.Contains(card))
                {
                    present.Add(card);
                }
                else
                {
                    missing.Add(card);
                }
            }

            if (present.Count == pattern.Cards.Count)
            {
                report.Combos.Add(CreateLocalCombo(pattern, present, missing, complete: true));
            }
            else if (present.Count > 0)
            {
                report.NearMisses.Add(CreateLocalCombo(pattern, present, missing, complete: false));
            }
        }
    }

    /// <summary>
    /// Converts one local dataset row into MCP-facing combo evidence.
    /// </summary>
    private static DeckCombo CreateLocalCombo(
        LocalComboPattern pattern,
        List<string> present,
        List<string> missing,
        bool complete)
    {
        double confidence = complete ? pattern.Confidence : Math.Min(pattern.Confidence, 0.65);
        return new DeckCombo
        {
            Name = pattern.Name,
            Cards = present,
            MissingCards = missing,
            ProducedFeatures = pattern.ProducedFeatures.ToList(),
            WinRoute = pattern.WinRoute,
            Kind = complete ? "local-pattern" : "local-near-miss",
            Confidence = confidence,
            Source = "local-pattern",
            Rationale = complete
                ? pattern.Rationale
                : "One or more cards from a known local combo pattern are present.",
            SourceUri = pattern.SourceUri,
            Metadata = new SourceEvidenceMetadata
            {
                Source = "local-pattern",
                SourceKind = complete ? "local-combo-pattern" : "local-combo-near-miss",
                SourceUri = pattern.SourceUri,
                RetrievedAt = DateTimeOffset.UnixEpoch,
                CacheStatus = "local",
                Confidence = confidence,
                Deterministic = true,
                Notes = ["Local combo patterns are checked into docs/reference/local-combos.json; catalog evidence remains preferred."]
            }
        };
    }

    /// <summary>
    /// Loads the local combo pattern dataset from the embedded resource.
    /// </summary>
    private static IReadOnlyList<LocalComboPattern> LoadLocalComboPatterns()
    {
        Assembly assembly = typeof(DeckAnalysisService).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(LocalComboPatternResourceName)
            ?? throw new InvalidOperationException("Embedded local combo pattern data is missing.");
        List<LocalComboPattern>? patterns = JsonSerializer.Deserialize<List<LocalComboPattern>>(
            stream,
            LocalComboPatternJsonOptions);
        return patterns?
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern.Name) && pattern.Cards.Count >= 2)
            .ToList() ?? [];
    }

    /// <summary>
    /// Builds combo pressure from combo findings and deck tags.
    /// </summary>
    private static ComboPressureEstimate BuildComboPressure(DeckWorkspace workspace, DeckComboReport report)
    {
        int tutorCount = DeckServiceHelpers.IncludedCards(workspace).Count(card => DeckRoleClassifier.Classify(card).PrimaryRole.Equals(DeckRoles.Tutors, StringComparison.OrdinalIgnoreCase));
        int comboTagCount = DeckServiceHelpers.IncludedCards(workspace)
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

    /// <summary>
    /// Adds route labels to all combo rows that do not already carry them.
    /// </summary>
    private static void AddRouteClassifications(DeckComboReport report)
    {
        foreach (DeckCombo combo in report.Combos.Concat(report.NearMisses))
        {
            if (combo.RouteLabels.Count > 0)
            {
                continue;
            }

            WinRouteClassification classification = WinRouteClassifier.ClassifyProducedFeatures(
                combo.Name,
                combo.ProducedFeatures.Count == 0 ? [combo.WinRoute] : combo.ProducedFeatures,
                combo.Metadata);
            combo.RouteLabels = classification.RouteTypes;
            combo.Terminal = classification.Terminal;
            combo.NeedsPayoff = classification.NeedsPayoff;
            combo.PayoffKindsNeeded = classification.PayoffKindsNeeded;
        }
    }

    /// <summary>
    /// Ensures combo evidence has route classifications.
    /// </summary>
    private static void EnsureRouteClassification(ComboEvidence evidence)
    {
        if (evidence.RouteClassifications.Count == 0)
        {
            evidence.RouteClassifications.Add(WinRouteClassifier.ClassifyProducedFeatures(
                string.IsNullOrWhiteSpace(evidence.ComboId) ? string.Join(" + ", evidence.Cards) : evidence.ComboId,
                evidence.ProducedFeatures,
                evidence.Metadata));
        }
    }

    /// <summary>
    /// Enriches combo color identity from Scryfall card facts when the catalog omits it.
    /// </summary>
    private async Task EnsureComboColorIdentityAsync(ComboEvidence evidence, CancellationToken cancellationToken)
    {
        if (evidence.ColorIdentity.Count > 0 || evidence.Cards.Count == 0)
        {
            return;
        }

        IReadOnlyDictionary<string, CardInfo> cards = await CardCatalog.GetCardsByNamesAsync(evidence.Cards, cancellationToken)
            .ConfigureAwait(false);
        evidence.ColorIdentity = cards.Values
            .SelectMany(card => card.ColorIdentity)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Checks whether a combo color identity fits within a commander color identity.
    /// </summary>
    private static bool IsColorSubset(IReadOnlyList<string> comboColors, HashSet<string> commanderColors)
    {
        return comboColors.All(color => commanderColors.Contains(color));
    }

    /// <summary>
    /// Counts whether a string input was provided.
    /// </summary>
    private static int CountProvided(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? 0 : 1;
    }

    /// <summary>
    /// Counts whether a list input was provided.
    /// </summary>
    private static int CountProvided(IReadOnlyList<string>? values)
    {
        return values is null || values.All(string.IsNullOrWhiteSpace) ? 0 : 1;
    }

    /// <summary>
    /// Builds card facts from a workspace card snapshot.
    /// </summary>
    private static CardInfo CreateCardInfoFromDeckCard(DeckCard card, string format)
    {
        CardSnapshot snapshot = card.Snapshot ?? new CardSnapshot();
        Dictionary<string, string> legalities = new(snapshot.Legalities, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(format) && !legalities.ContainsKey(format))
        {
            legalities[format] = "unknown";
        }

        return new CardInfo
        {
            Name = card.Name,
            ManaCost = snapshot.ManaCost,
            ManaValue = snapshot.ManaValue,
            TypeLine = snapshot.TypeLine,
            OracleText = snapshot.OracleText,
            Set = snapshot.Set,
            ReleasedAt = snapshot.ReleasedAt,
            ScryfallUri = snapshot.ScryfallUri,
            EdhrecRank = snapshot.EdhrecRank,
            ColorIdentity = snapshot.ColorIdentity.ToList(),
            Keywords = snapshot.Keywords.ToList(),
            ProducedMana = snapshot.ProducedMana.ToList(),
            Legalities = legalities,
            Prices = new Dictionary<string, string>(snapshot.Prices, StringComparer.OrdinalIgnoreCase)
        };
    }

    /// <summary>
    /// Represents one checked-in local combo pattern row.
    /// </summary>
    private sealed class LocalComboPattern
    {
        /// <summary>
        /// Display name used for reports and dedupe-visible evidence.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Exact card names required to complete the pattern.
        /// </summary>
        public List<string> Cards { get; set; } = [];

        /// <summary>
        /// Compact win route summary shown to clients.
        /// </summary>
        public string WinRoute { get; set; } = "";

        /// <summary>
        /// Deterministic feature phrases used by the route classifier.
        /// </summary>
        public List<string> ProducedFeatures { get; set; } = [];

        /// <summary>
        /// Confidence assigned to completed local-pattern matches.
        /// </summary>
        public double Confidence { get; set; } = 0.80;

        /// <summary>
        /// Report-facing explanation for a completed pattern match.
        /// </summary>
        public string Rationale { get; set; } = "";

        /// <summary>
        /// Optional attribution URI for the pattern source.
        /// </summary>
        public string? SourceUri { get; set; }
    }
}
