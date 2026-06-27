namespace MtgMcp.Core;

/// <summary>
/// Captures normalized machine-usable facts extracted from one card.
/// </summary>
public sealed class CardOperationalFacts
{
    /// <summary>
    /// Gets or sets the card name these facts describe.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets the primary deck role assigned before operational scoring.
    /// </summary>
    public string Role { get; set; } = DeckRoles.Utility;

    /// <summary>
    /// Gets or sets ramp-specific operational facts when the card is ramp-shaped.
    /// </summary>
    public RampOperationalFacts? Ramp { get; set; }

    /// <summary>
    /// Gets or sets source or parser evidence used to derive these facts.
    /// </summary>
    public List<CardFactEvidence> Evidence { get; set; } = [];

    /// <summary>
    /// Gets or sets deterministic confidence and source-data caveats.
    /// </summary>
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// Describes how a ramp card changes mana availability without relying on card-name overrides.
/// </summary>
public sealed class RampOperationalFacts
{
    /// <summary>
    /// Gets or sets the normalized ramp shape.
    /// </summary>
    public string Kind { get; set; } = "unknown";

    /// <summary>
    /// Gets or sets mana needed to cast the card.
    /// </summary>
    public int CastMana { get; set; }

    /// <summary>
    /// Gets or sets future activation mana needed after casting.
    /// </summary>
    public int ActivationMana { get; set; }

    /// <summary>
    /// Gets or sets whether the ramp action requires tapping the permanent.
    /// </summary>
    public bool RequiresTap { get; set; }

    /// <summary>
    /// Gets or sets whether the card sacrifices itself as part of the ramp action.
    /// </summary>
    public bool SacrificesSelf { get; set; }

    /// <summary>
    /// Gets or sets where the mana resource goes, such as battlefield or manaPool.
    /// </summary>
    public string Destination { get; set; } = "unknown";

    /// <summary>
    /// Gets or sets whether the produced permanent or land enters tapped.
    /// </summary>
    public bool? EntersTapped { get; set; }

    /// <summary>
    /// Gets or sets the earliest turn the card can increase usable mana from a normal opening sequence.
    /// </summary>
    public int? EarliestManaGainTurn { get; set; }

    /// <summary>
    /// Gets or sets whether the ramp effect is consumed after one use.
    /// </summary>
    public bool OneShot { get; set; }

    /// <summary>
    /// Gets or sets whether the card can keep producing mana after the first use.
    /// </summary>
    public bool Repeatable { get; set; }

    /// <summary>
    /// Gets or sets known mana symbols this card can produce or fetch access to.
    /// </summary>
    public List<string> ProducedMana { get; set; } = [];
}

/// <summary>
/// Records one source-backed or parser-derived reason for an operational fact.
/// </summary>
public sealed class CardFactEvidence
{
    /// <summary>
    /// Gets or sets the evidence source, such as scryfall, scryfall-tagger, or oracle-parser.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Gets or sets the confidence state such as sourceBacked or parserDerived.
    /// </summary>
    public string Kind { get; set; } = "";

    /// <summary>
    /// Gets or sets the matched fact label.
    /// </summary>
    public string Label { get; set; } = "";

    /// <summary>
    /// Gets or sets compact detail explaining the evidence.
    /// </summary>
    public string Detail { get; set; } = "";
}

/// <summary>
/// Scores one ramp card against a concrete deck context.
/// </summary>
public sealed class RampContextEvaluation
{
    /// <summary>
    /// Gets or sets the evaluator that produced this context result.
    /// </summary>
    public string Evaluator { get; set; } = "ramp";

    /// <summary>
    /// Gets or sets whether the evaluator can score the card's operational facts.
    /// </summary>
    public bool Applicable { get; set; } = true;

    /// <summary>
    /// Gets or sets the stable applicability status for tool clients.
    /// </summary>
    public string EvaluationStatus { get; set; } = "evaluated";

    /// <summary>
    /// Gets or sets the workspace id used for context.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the evaluated card name.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets the primary role assigned before operational scoring.
    /// </summary>
    public string Role { get; set; } = DeckRoles.Utility;

    /// <summary>
    /// Gets or sets the normalized ramp kind when available.
    /// </summary>
    public string? RampKind { get; set; }

    /// <summary>
    /// Gets or sets the deterministic context score from 0 to 100.
    /// </summary>
    public int Score { get; set; }

    /// <summary>
    /// Gets or sets rubric sub-scores using stable rubric key names.
    /// </summary>
    public Dictionary<string, int> SubScores { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the main context-specific weaknesses.
    /// </summary>
    public List<string> TopIssues { get; set; } = [];

    /// <summary>
    /// Gets or sets the main context-specific strengths.
    /// </summary>
    public List<string> TopStrengths { get; set; } = [];

    /// <summary>
    /// Gets or sets deterministic confidence and source-data caveats.
    /// </summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>
    /// Gets or sets the operational facts used for full-detail responses.
    /// </summary>
    public CardOperationalFacts Facts { get; set; } = new();

    /// <summary>
    /// Gets or sets explicitly supplied candidate comparisons.
    /// </summary>
    public List<RampContextEvaluation> CandidateEvaluations { get; set; } = [];
}
