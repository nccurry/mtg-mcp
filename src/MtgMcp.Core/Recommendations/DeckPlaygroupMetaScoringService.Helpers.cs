using System.Text.Json;

namespace MtgMcp.Core;

/// <summary>
/// Contains shared local-meta scoring helpers and workspace loading internals.
/// </summary>
public sealed partial class DeckPlaygroupMetaScoringService
{
    /// <summary>
    /// Creates a preview workspace with one candidate added to the included deck.
    /// </summary>
    private static DeckWorkspace WorkspaceWithAddedCandidate(DeckWorkspace workspace, CardInfo card)
    {
        DeckWorkspace clone = CloneWorkspace(workspace);
        DeckServiceHelpers.EnsureCategory(clone, DeckDefaults.Mainboard);
        DeckCard candidate = DeckRecommendationCardFacts.CreateCandidateCard(card);
        candidate.PrimaryCategory = DeckDefaults.Mainboard;
        candidate.Categories = [DeckDefaults.Mainboard];
        clone.Cards.Add(candidate);
        return clone;
    }

    /// <summary>
    /// Converts an existing workspace card snapshot into catalog-like card facts.
    /// </summary>
    private static CardInfo CardInfoFromWorkspaceCard(DeckCard card)
    {
        CardSnapshot snapshot = DeckServiceHelpers.GetSnapshot(card);
        return new CardInfo
        {
            Name = card.Name,
            ManaCost = snapshot.ManaCost,
            ManaValue = snapshot.ManaValue,
            TypeLine = snapshot.TypeLine,
            OracleText = snapshot.OracleText,
            Set = snapshot.Set,
            CollectorNumber = snapshot.CollectorNumber,
            Rarity = snapshot.Rarity,
            ReleasedAt = snapshot.ReleasedAt,
            ScryfallUri = snapshot.ScryfallUri,
            EdhrecRank = snapshot.EdhrecRank,
            ColorIdentity = snapshot.ColorIdentity.ToList(),
            Keywords = snapshot.Keywords.ToList(),
            ProducedMana = snapshot.ProducedMana.ToList(),
            Legalities = new Dictionary<string, string>(snapshot.Legalities, StringComparer.OrdinalIgnoreCase),
            Prices = new Dictionary<string, string>(snapshot.Prices, StringComparer.OrdinalIgnoreCase),
            ImageUris = new Dictionary<string, string>(snapshot.ImageUris, StringComparer.OrdinalIgnoreCase),
        };
    }

    /// <summary>
    /// Clones a workspace for read-only preview scoring.
    /// </summary>
    private static DeckWorkspace CloneWorkspace(DeckWorkspace workspace)
    {
        string json = JsonSerializer.Serialize(workspace);
        return JsonSerializer.Deserialize<DeckWorkspace>(json)
            ?? throw new InvalidOperationException("Unable to clone deck workspace for local-meta scoring.");
    }

    /// <summary>
    /// Adds one text-derived pressure row when a keyword matches.
    /// </summary>
    private static void AddPressure(
        List<PlaygroupMetaPressureEvidence> pressures,
        string pressure,
        double score,
        string source,
        string text,
        params string[] needles)
    {
        List<string> matched = needles.Where(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matched.Count == 0)
        {
            return;
        }

        pressures.Add(new PlaygroupMetaPressureEvidence
        {
            Pressure = pressure,
            Score = score,
            Source = source,
            Evidence = [$"matched {string.Join(", ", matched)}"],
        });
    }

    /// <summary>
    /// Adds one imported-deck pressure row when a count threshold matches.
    /// </summary>
    private static void AddImportedPressure(
        List<PlaygroupMetaPressureEvidence> pressures,
        string pressure,
        bool matched,
        string evidence)
    {
        if (!matched)
        {
            return;
        }

        pressures.Add(new PlaygroupMetaPressureEvidence
        {
            Pressure = pressure,
            Score = 0.80,
            Source = "archidekt-decklist",
            Evidence = [evidence],
        });
    }

    /// <summary>
    /// Computes confidence for one Playgroup deck evidence row.
    /// </summary>
    private static double DeckEvidenceConfidence(PlaygroupDeckSummary deck, bool importedDecklist)
    {
        double confidence = 0.45;
        confidence += importedDecklist ? 0.25 : 0;
        confidence += deck.FetchedPlaygroupGames > 0 ? 0.10 : 0;
        confidence += deck.ConfidenceFactor.HasValue ? Math.Clamp(deck.ConfidenceFactor.Value, 0, 1) * 0.15 : 0;
        confidence += deck.AverageWinsByRound.HasValue ? 0.05 : 0;
        return Math.Clamp(confidence, 0.20, 0.95);
    }

    /// <summary>
    /// Builds concise candidate rationale.
    /// </summary>
    private static string BuildMetaCandidateRationale(
        string cardName,
        string role,
        double metaCoverage,
        double selfHarmPenalty)
    {
        string tradeoff = selfHarmPenalty > 0.35
            ? " with a notable self-harm tradeoff"
            : "";
        return $"{cardName} is a {role} candidate with {metaCoverage:0.00} local-meta coverage{tradeoff}.";
    }

    /// <summary>
    /// Gets a scenario rate by name.
    /// </summary>
    private static double ScenarioRate(DeckPerformanceAnalysis analysis, string scenarioName)
    {
        return analysis.Scenarios
            .FirstOrDefault(scenario => scenario.Name.Equals(scenarioName, StringComparison.OrdinalIgnoreCase))
            ?.SuccessRate
            ?? 0;
    }

    /// <summary>
    /// Counts cards with a requested primary role.
    /// </summary>
    private static int CountCards(IEnumerable<DeckCard> cards, string role)
    {
        return cards
            .Where(card => DeckRoleClassifier.Classify(card).PrimaryRole.Equals(role, StringComparison.OrdinalIgnoreCase))
            .Sum(card => Math.Max(1, card.Quantity));
    }

    /// <summary>
    /// Counts cards with a requested secondary tag.
    /// </summary>
    private static int CountTaggedCards(IEnumerable<DeckCard> cards, string tag)
    {
        return cards
            .Where(card => DeckRoleClassifier.Classify(card).Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            .Sum(card => Math.Max(1, card.Quantity));
    }

    /// <summary>
    /// Checks whether a URL points at Archidekt.
    /// </summary>
    private static bool IsArchidektDecklistUrl(string? decklistUrl)
    {
        return Uri.TryCreate(decklistUrl, UriKind.Absolute, out Uri? uri)
            && uri.Host.Contains("archidekt.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Scores a role match when a pressure mapping wants one role.
    /// </summary>
    private static double RoleScore(CardRoleAssignment role, string roleName, double score)
    {
        return role.PrimaryRole.Equals(roleName, StringComparison.OrdinalIgnoreCase) ? score : 0;
    }

    /// <summary>
    /// Scores a tag match when a pressure mapping wants one tag.
    /// </summary>
    private static double TagScore(CardRoleAssignment role, string tag, double score)
    {
        return role.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase) ? score : 0;
    }

    /// <summary>
    /// Scores an oracle-text match when a pressure mapping wants one phrase.
    /// </summary>
    private static double TextScore(string text, double score, params string[] phrases)
    {
        return DeckAnalysisMetrics.ContainsAny(text, phrases) ? score : 0;
    }

    /// <summary>
    /// Returns the largest score from a pressure mapping.
    /// </summary>
    private static double Max(params double[] values)
    {
        return values.Max();
    }

    /// <summary>
    /// Loads a workspace by id or throws when it is unknown.
    /// </summary>
    private async Task<DeckWorkspace> LoadWorkspaceAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        DeckWorkspace? workspace = await repository
            .GetAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return workspace
            ?? throw new InvalidOperationException($"Workspace '{workspaceId}' was not found.");
    }
}
