namespace MtgMcp.Core;

/// <summary>
/// Reports deterministic card scores against observed Playgroup.gg pressure.
/// </summary>
public sealed class PlaygroupMetaScoringResult
{
    /// <summary>
    /// Gets or sets the analyzed workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the requested Playgroup id.
    /// </summary>
    public long PlaygroupId { get; set; }

    /// <summary>
    /// Gets or sets where the candidate cards came from.
    /// </summary>
    public string CandidateSource { get; set; } = "";

    /// <summary>
    /// Gets or sets the resolved simulation profile used for plan-fit and performance deltas.
    /// </summary>
    public ResolvedSimulationProfile ProfileResolution { get; set; } = new();

    /// <summary>
    /// Gets or sets ranked opposing decks used as local-meta evidence.
    /// </summary>
    public List<PlaygroupMetaDeckEvidence> MetaDecks { get; set; } = [];

    /// <summary>
    /// Gets or sets aggregated pressure categories inferred from local-meta decks.
    /// </summary>
    public List<PlaygroupMetaPressureEvidence> MetaPressures { get; set; } = [];

    /// <summary>
    /// Gets or sets scored candidate cards.
    /// </summary>
    public List<PlaygroupMetaCandidateScore> CandidateScores { get; set; } = [];

    /// <summary>
    /// Gets or sets non-fatal data-quality warnings.
    /// </summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>
    /// Gets or sets explanatory notes about the deterministic scoring model.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Describes one ranked local-meta deck and how confidently it was inspected.
/// </summary>
public sealed class PlaygroupMetaDeckEvidence
{
    /// <summary>
    /// Gets or sets the Playgroup deck id.
    /// </summary>
    public long DeckId { get; set; }

    /// <summary>
    /// Gets or sets the deck name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the observed owner name when available.
    /// </summary>
    public string? OwnerName { get; set; }

    /// <summary>
    /// Gets or sets commander names from Playgroup.
    /// </summary>
    public List<string> CommanderNames { get; set; } = [];

    /// <summary>
    /// Gets or sets the ranking metric score.
    /// </summary>
    public double RankingScore { get; set; }

    /// <summary>
    /// Gets or sets how strongly this deck contributes to aggregate pressures.
    /// </summary>
    public double Weight { get; set; }

    /// <summary>
    /// Gets or sets whether an external decklist was imported.
    /// </summary>
    public bool ImportedDecklist { get; set; }

    /// <summary>
    /// Gets or sets the external decklist URL when present.
    /// </summary>
    public string? DecklistUrl { get; set; }

    /// <summary>
    /// Gets or sets confidence in this deck's pressure evidence.
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Gets or sets pressure rows inferred from this deck.
    /// </summary>
    public List<PlaygroupMetaPressureEvidence> Pressures { get; set; } = [];

    /// <summary>
    /// Gets or sets deck-specific data quality warnings.
    /// </summary>
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// Represents one local-meta pressure category with evidence.
/// </summary>
public sealed class PlaygroupMetaPressureEvidence
{
    /// <summary>
    /// Gets or sets the stable pressure id.
    /// </summary>
    public string Pressure { get; set; } = "";

    /// <summary>
    /// Gets or sets the normalized pressure score.
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// Gets or sets where the pressure evidence came from.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Gets or sets readable evidence fragments.
    /// </summary>
    public List<string> Evidence { get; set; } = [];
}

/// <summary>
/// Scores one candidate card against deck plan, simulation, meta, budget, and confidence factors.
/// </summary>
public sealed class PlaygroupMetaCandidateScore
{
    /// <summary>
    /// Gets or sets the candidate card name.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets the candidate's primary role.
    /// </summary>
    public string Role { get; set; } = DeckRoles.Utility;

    /// <summary>
    /// Gets or sets candidate tags.
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets the weighted final score.
    /// </summary>
    public double OverallScore { get; set; }

    /// <summary>
    /// Gets or sets how well the card fits the deck intent and resolved profile.
    /// </summary>
    public double PlanFitScore { get; set; }

    /// <summary>
    /// Gets or sets the normalized performance delta from adding the card in memory.
    /// </summary>
    public double PerformanceDeltaScore { get; set; }

    /// <summary>
    /// Gets or sets how well the card covers aggregated local-meta pressures.
    /// </summary>
    public double MetaCoverageScore { get; set; }

    /// <summary>
    /// Gets or sets how much the card appears to interfere with the deck's own plan.
    /// </summary>
    public double SelfHarmPenalty { get; set; }

    /// <summary>
    /// Gets or sets the score for price, legality, color identity, and bracket constraints.
    /// </summary>
    public double PriceBracketScore { get; set; }

    /// <summary>
    /// Gets or sets confidence in the available candidate and meta evidence.
    /// </summary>
    public double EvidenceConfidence { get; set; }

    /// <summary>
    /// Gets or sets the candidate's known USD price.
    /// </summary>
    public decimal? Price { get; set; }

    /// <summary>
    /// Gets or sets the Scryfall card page for linking the scored candidate.
    /// </summary>
    public string? ScryfallUri { get; set; }

    /// <summary>
    /// Gets or sets whether the card appears on the current Commander Game Changer list.
    /// </summary>
    public bool IsGameChanger { get; set; }

    /// <summary>
    /// Gets or sets concise rationale for the score.
    /// </summary>
    public string Rationale { get; set; } = "";

    /// <summary>
    /// Gets or sets scoring evidence lines.
    /// </summary>
    public List<string> Evidence { get; set; } = [];
}
