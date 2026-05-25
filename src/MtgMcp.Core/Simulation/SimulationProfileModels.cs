namespace MtgMcp.Core;

/// <summary>
/// Lists built-in simulation profile identifiers.
/// </summary>
public static class SimulationProfileIds
{
    /// <summary>
    /// Lets the resolver infer a profile from deck facts and intent.
    /// </summary>
    public const string Auto = "auto";

    /// <summary>
    /// Uses conservative Commander assumptions and avoids strong route claims.
    /// </summary>
    public const string Neutral = "neutral";

    /// <summary>
    /// Prioritizes pressure, combat clock, protection, and tempo.
    /// </summary>
    public const string Aggro = "aggro";

    /// <summary>
    /// Prioritizes route assembly, tutors, card selection, and protected wins.
    /// </summary>
    public const string Combo = "combo";

    /// <summary>
    /// Prioritizes mana stability, draw, holding answers, and late inevitability.
    /// </summary>
    public const string Control = "control";

    /// <summary>
    /// Prioritizes engines, commander development, card advantage, and flexible interaction.
    /// </summary>
    public const string Value = "value";

    /// <summary>
    /// Prioritizes ramp, land drops, large payoffs, and mana scaling.
    /// </summary>
    public const string BigMana = "big-mana";

    /// <summary>
    /// Prioritizes early asymmetrical hate and parity-breaking.
    /// </summary>
    public const string Stax = "stax";
}

/// <summary>
/// Describes a deterministic play-pattern profile for heuristic simulation.
/// </summary>
public sealed class SimulationProfile
{
    /// <summary>
    /// Gets or sets the stable profile id.
    /// </summary>
    public string Id { get; set; } = SimulationProfileIds.Neutral;

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets parent profile ids for external profile documentation and validation.
    /// </summary>
    public List<string> Inherits { get; set; } = [];

    /// <summary>
    /// Gets or sets the short purpose statement shown in evidence output.
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Gets or sets the theme tags this profile recognizes as modifiers.
    /// </summary>
    public List<string> ThemeTags { get; set; } = [];

    /// <summary>
    /// Gets or sets opening-hand and London mulligan settings.
    /// </summary>
    public SimulationMulliganSettings Mulligan { get; set; } = new();

    /// <summary>
    /// Gets or sets spell sequencing and hold-up behavior.
    /// </summary>
    public SimulationSequencingSettings Sequencing { get; set; } = new();

    /// <summary>
    /// Gets or sets default target turns for scenario summaries.
    /// </summary>
    public SimulationScenarioSettings Scenarios { get; set; } = new();

    /// <summary>
    /// Gets or sets fallback win-detection thresholds.
    /// </summary>
    public SimulationWinDetectionSettings WinDetection { get; set; } = new();

    /// <summary>
    /// Gets or sets deck-agnostic win routes attached to this profile.
    /// </summary>
    public List<SimulationRouteDefinition> WinRoutes { get; set; } = [];
}

/// <summary>
/// Controls how opening hands are scored before mulligans.
/// </summary>
public sealed class SimulationMulliganSettings
{
    /// <summary>
    /// Gets or sets the keep-score threshold for a seven-card hand when a free mulligan is available.
    /// </summary>
    public double SevenCardFreeKeepScore { get; set; } = 7.5;

    /// <summary>
    /// Gets or sets the keep-score threshold for an ordinary seven-card hand.
    /// </summary>
    public double SevenCardKeepScore { get; set; } = 6;

    /// <summary>
    /// Gets or sets the keep-score threshold for a six-card hand.
    /// </summary>
    public double SixCardKeepScore { get; set; } = 4.5;

    /// <summary>
    /// Gets or sets the keep-score threshold for hands of five or fewer cards.
    /// </summary>
    public double FiveCardKeepScore { get; set; } = 1;

    /// <summary>
    /// Gets or sets how strongly early ramp contributes to keep decisions.
    /// </summary>
    public double EarlyRampWeight { get; set; } = 2;

    /// <summary>
    /// Gets or sets how strongly one-mana ramp contributes to keep decisions.
    /// </summary>
    public double OneManaRampWeight { get; set; } = 1;

    /// <summary>
    /// Gets or sets how strongly early draw contributes to keep decisions.
    /// </summary>
    public double EarlyDrawWeight { get; set; } = 1;

    /// <summary>
    /// Gets or sets how strongly cheap plays contribute to keep decisions.
    /// </summary>
    public double CheapPlayWeight { get; set; } = 0.75;

    /// <summary>
    /// Gets or sets how strongly early interaction contributes to keep decisions.
    /// </summary>
    public double EarlyInteractionWeight { get; set; } = 0.5;

    /// <summary>
    /// Gets or sets how strongly commander-on-curve plans contribute to keep decisions.
    /// </summary>
    public double CommanderPlanWeight { get; set; } = 2;
}

/// <summary>
/// Controls deterministic spell sequencing during heuristic turns.
/// </summary>
public sealed class SimulationSequencingSettings
{
    /// <summary>
    /// Gets or sets whether commander deployment should happen before normal spells when affordable.
    /// </summary>
    public bool PreferCommanderOnCurve { get; set; } = true;

    /// <summary>
    /// Gets or sets the first turn where nonpermanent interaction should usually be held.
    /// </summary>
    public int HoldInteractionFromTurn { get; set; } = 3;

    /// <summary>
    /// Gets or sets the minimum interaction count the profile tries to keep available.
    /// </summary>
    public int MinimumInteractionHeld { get; set; } = 1;

    /// <summary>
    /// Gets or sets whether protection should be held once commander-centric plans are online.
    /// </summary>
    public bool HoldProtectionWhenCommanderOnline { get; set; } = true;

    /// <summary>
    /// Gets or sets the priority for early ramp. Lower values are cast first.
    /// </summary>
    public int EarlyRampPriority { get; set; }

    /// <summary>
    /// Gets or sets the priority for draw and engine cards.
    /// </summary>
    public int DrawPriority { get; set; } = 1;

    /// <summary>
    /// Gets or sets the priority for tutors.
    /// </summary>
    public int TutorPriority { get; set; } = 2;

    /// <summary>
    /// Gets or sets the priority for combo pieces and enablers.
    /// </summary>
    public int ComboPriority { get; set; } = 2;

    /// <summary>
    /// Gets or sets the priority for finishers.
    /// </summary>
    public int WinconPriority { get; set; } = 4;

    /// <summary>
    /// Gets or sets the priority for uncategorized spells.
    /// </summary>
    public int DefaultPriority { get; set; } = 3;
}

/// <summary>
/// Stores target turns for built-in scenario summaries.
/// </summary>
public sealed class SimulationScenarioSettings
{
    /// <summary>
    /// Gets or sets the commander deployment target turn.
    /// </summary>
    public int CommanderTurn { get; set; } = 4;

    /// <summary>
    /// Gets or sets the commander protection target turn.
    /// </summary>
    public int ProtectionTurn { get; set; } = 5;

    /// <summary>
    /// Gets or sets the graveyard or hate-piece access target turn.
    /// </summary>
    public int HateTurn { get; set; } = 3;

    /// <summary>
    /// Gets or sets the color access target turn.
    /// </summary>
    public int ColorTurn { get; set; } = 3;

    /// <summary>
    /// Gets or sets the interaction access target turn.
    /// </summary>
    public int InteractionTurn { get; set; } = 4;

    /// <summary>
    /// Gets or sets the combo assembly target turn.
    /// </summary>
    public int ComboTurn { get; set; } = 5;
}

/// <summary>
/// Controls fallback route detection when no exact or deck-specific route matched.
/// </summary>
public sealed class SimulationWinDetectionSettings
{
    /// <summary>
    /// Gets or sets whether broad combo tags can produce a fallback combo route.
    /// </summary>
    public bool AllowFallbackComboWins { get; set; }

    /// <summary>
    /// Gets or sets the earliest fallback combo turn.
    /// </summary>
    public int FallbackComboEarliestTurn { get; set; } = 5;

    /// <summary>
    /// Gets or sets the win-pressure threshold for fallback finisher routes.
    /// </summary>
    public int FinisherPressureThreshold { get; set; } = 10;

    /// <summary>
    /// Gets or sets the battlefield-pressure threshold for fallback finisher routes.
    /// </summary>
    public int FinisherPowerThreshold { get; set; } = 22;

    /// <summary>
    /// Gets or sets the earliest fallback finisher turn.
    /// </summary>
    public int FinisherEarliestTurn { get; set; } = 6;

    /// <summary>
    /// Gets or sets the battlefield-pressure threshold for fallback combat routes.
    /// </summary>
    public int CombatPowerThreshold { get; set; } = 36;

    /// <summary>
    /// Gets or sets the earliest fallback combat turn.
    /// </summary>
    public int CombatEarliestTurn { get; set; } = 7;
}

/// <summary>
/// Defines a named deterministic route to victory.
/// </summary>
public sealed class SimulationRouteDefinition
{
    /// <summary>
    /// Gets or sets the route name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the route kind, such as combo or combat-finisher.
    /// </summary>
    public string Kind { get; set; } = "route";

    /// <summary>
    /// Gets or sets the earliest turn where this route can count as a win.
    /// </summary>
    public int EarliestTurn { get; set; } = 1;

    /// <summary>
    /// Gets or sets safe deterministic predicates that must all match.
    /// </summary>
    public List<string> Requirements { get; set; } = [];

    /// <summary>
    /// Gets or sets where this route came from.
    /// </summary>
    public string Source { get; set; } = "profile";
}

/// <summary>
/// Reports one reason a simulation profile was selected or modified.
/// </summary>
public sealed class SimulationProfileEvidence
{
    /// <summary>
    /// Gets or sets the evidence source.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Gets or sets the related profile id when applicable.
    /// </summary>
    public string ProfileId { get; set; } = "";

    /// <summary>
    /// Gets or sets a deterministic score used for candidate ranking.
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// Gets or sets a readable explanation.
    /// </summary>
    public string Message { get; set; } = "";
}

/// <summary>
/// Reports the resolved simulation profile and the evidence behind it.
/// </summary>
public sealed class ResolvedSimulationProfile
{
    /// <summary>
    /// Gets or sets the resolved profile.
    /// </summary>
    public SimulationProfile Profile { get; set; } = SimulationProfileCatalog.NeutralProfile();

    /// <summary>
    /// Gets or sets the source that won resolution.
    /// </summary>
    public string Source { get; set; } = "default";

    /// <summary>
    /// Gets or sets auto-profile candidates considered by the resolver.
    /// </summary>
    public List<SimulationProfileEvidence> Candidates { get; set; } = [];

    /// <summary>
    /// Gets or sets profile and override evidence.
    /// </summary>
    public List<SimulationProfileEvidence> Evidence { get; set; } = [];

    /// <summary>
    /// Gets or sets non-fatal resolution warnings.
    /// </summary>
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// Stores route evidence from deterministic route evaluation.
/// </summary>
public sealed class SimulationRouteEvidence
{
    /// <summary>
    /// Gets or sets the route name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the route kind.
    /// </summary>
    public string Kind { get; set; } = "";

    /// <summary>
    /// Gets or sets whether every predicate matched.
    /// </summary>
    public bool Matched { get; set; }

    /// <summary>
    /// Gets or sets the route source.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Gets or sets the earliest turn allowed by the route.
    /// </summary>
    public int EarliestTurn { get; set; }

    /// <summary>
    /// Gets or sets confidence in this route evidence.
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Gets or sets matched evidence lines.
    /// </summary>
    public List<string> Evidence { get; set; } = [];

    /// <summary>
    /// Gets or sets failed predicate explanations.
    /// </summary>
    public List<string> MissingRequirements { get; set; } = [];
}
