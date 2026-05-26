namespace MtgMcp.Core;

/// <summary>
/// Describes the user's desired direction for a deck.
/// </summary>
public sealed class DeckIntent
{
    /// <summary>
    /// Gets or sets the intent format version.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Gets or sets the deck format.
    /// </summary>
    public string? Format { get; set; }

    /// <summary>
    /// Gets or sets the commander name.
    /// </summary>
    public string? Commander { get; set; }

    /// <summary>
    /// Gets or sets the intended archetype.
    /// </summary>
    public string? Archetype { get; set; }

    /// <summary>
    /// Gets or sets the user's plain-language deck goal.
    /// </summary>
    public string? Goal { get; set; }

    /// <summary>
    /// Gets or sets the desired power level.
    /// </summary>
    public string? PowerLevel { get; set; }

    /// <summary>
    /// Gets or sets the plain-language power target.
    /// </summary>
    public string? PowerTarget { get; set; }

    /// <summary>
    /// Gets or sets the named deckbuilding heuristic profile.
    /// </summary>
    public string? HeuristicProfile { get; set; }

    /// <summary>
    /// Gets or sets the named simulation profile.
    /// </summary>
    public string? SimulationProfile { get; set; }

    /// <summary>
    /// Gets or sets the named package template.
    /// </summary>
    public string? PackageTemplate { get; set; }

    /// <summary>
    /// Gets or sets simple archetype/theme tags that modify simulation behavior.
    /// </summary>
    public List<string> ArchetypeTags { get; set; } = [];

    /// <summary>
    /// Gets or sets the desired no-interaction goldfish turn.
    /// </summary>
    public int? TargetGoldfishTurn { get; set; }

    /// <summary>
    /// Gets or sets the budget guidance.
    /// </summary>
    public DeckIntentBudget Budget { get; set; } = new();

    /// <summary>
    /// Gets or sets desired role count targets.
    /// </summary>
    public Dictionary<string, DeckIntentTarget> Targets { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets desired role, tag, or package count targets from v2 intent.
    /// </summary>
    public Dictionary<string, DeckIntentTarget> BuildTargets { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets desired package count targets.
    /// </summary>
    public Dictionary<string, DeckIntentTarget> Packages { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets deck-local simulation settings.
    /// </summary>
    public DeckIntentSimulationSettings Simulation { get; set; } = new();

    /// <summary>
    /// Gets or sets deck-local win route definitions.
    /// </summary>
    public List<DeckIntentWinRoute> WinRoutes { get; set; } = [];

    /// <summary>
    /// Gets or sets scoring priorities.
    /// </summary>
    public ReplacementWeights? Priorities { get; set; }

    /// <summary>
    /// Gets or sets local metagame pressures to account for.
    /// </summary>
    public List<string> LocalMeta { get; set; } = [];

    /// <summary>
    /// Gets or sets things the deck should prefer.
    /// </summary>
    public List<string> Prefer { get; set; } = [];

    /// <summary>
    /// Gets or sets things the deck should avoid.
    /// </summary>
    public List<string> Avoid { get; set; } = [];

    /// <summary>
    /// Gets or sets cards or packages that should not be cut casually.
    /// </summary>
    public List<string> Protect { get; set; } = [];
}

/// <summary>
/// Captures deck-local deterministic simulation assumptions.
/// </summary>
public sealed class DeckIntentSimulationSettings
{
    /// <summary>
    /// Gets or sets raw simulation settings keyed by normalized field name.
    /// </summary>
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets how strongly the deck depends on its commander.
    /// </summary>
    public string? CommanderDependency { get; set; }

    /// <summary>
    /// Gets or sets the mulligan style requested by the deck.
    /// </summary>
    public string? MulliganStyle { get; set; }

    /// <summary>
    /// Gets or sets the first turn where interaction should be held up.
    /// </summary>
    public int? HoldInteractionFromTurn { get; set; }

    /// <summary>
    /// Gets or sets the minimum interaction count to keep available.
    /// </summary>
    public int? MinimumInteractionHeld { get; set; }

    /// <summary>
    /// Gets or sets whether commander deployment should be prioritized.
    /// </summary>
    public bool? PreferCommanderOnCurve { get; set; }

    /// <summary>
    /// Gets or sets the preferred turn for deploying the creature commander.
    /// </summary>
    public int? PreferredCommanderTurn { get; set; }

    /// <summary>
    /// Gets or sets the preferred turn for deploying a command-zone Background.
    /// </summary>
    public int? PreferredBackgroundTurn { get; set; }

    /// <summary>
    /// Gets or sets the preferred command-zone deployment order.
    /// </summary>
    public List<string> CommandZoneOrder { get; set; } = [];

    /// <summary>
    /// Gets or sets whether the simulator may attempt wins without protection.
    /// </summary>
    public bool? AcceptShieldDownWinAttempt { get; set; }
}

/// <summary>
/// Defines one deck-local win route parsed from deck intent.
/// </summary>
public sealed class DeckIntentWinRoute
{
    /// <summary>
    /// Gets or sets the route name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the route kind.
    /// </summary>
    public string Kind { get; set; } = "route";

    /// <summary>
    /// Gets or sets the earliest turn where this route can count as a win.
    /// </summary>
    public int? EarliestTurn { get; set; }

    /// <summary>
    /// Gets or sets the bounded route requirements.
    /// </summary>
    public List<string> Requirements { get; set; } = [];

    /// <summary>
    /// Gets or sets the raw parsed route line.
    /// </summary>
    public string Raw { get; set; } = "";
}

/// <summary>
/// Captures deck budget preferences.
/// </summary>
public sealed class DeckIntentBudget
{
    /// <summary>
    /// Gets or sets the raw budget guidance.
    /// </summary>
    public string? Text { get; set; }

    /// <summary>
    /// Gets or sets the maximum single-card price.
    /// </summary>
    public decimal? MaxCardPrice { get; set; }

    /// <summary>
    /// Gets or sets whether cheaper swaps should be preferred.
    /// </summary>
    public bool PreferCheaperSwaps { get; set; }
}

/// <summary>
/// Describes a desired count for a deck role or tag.
/// </summary>
public sealed class DeckIntentTarget
{
    /// <summary>
    /// Gets or sets the raw target text.
    /// </summary>
    public string Raw { get; set; } = "";

    /// <summary>
    /// Gets or sets the minimum desired count.
    /// </summary>
    public int? Minimum { get; set; }

    /// <summary>
    /// Gets or sets the maximum desired count.
    /// </summary>
    public int? Maximum { get; set; }
}

/// <summary>
/// Reports parsed deck intent and its storage state.
/// </summary>
public sealed class DeckIntentResult
{
    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets whether an intent block was found.
    /// </summary>
    public bool Found { get; set; }

    /// <summary>
    /// Gets or sets the parsed intent.
    /// </summary>
    public DeckIntent? Intent { get; set; }

    /// <summary>
    /// Gets or sets the human-readable intent text.
    /// </summary>
    public string IntentText { get; set; } = "";

    /// <summary>
    /// Gets or sets parser warnings.
    /// </summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>
    /// Gets or sets where the intent came from.
    /// </summary>
    public string Source { get; set; } = "description";
}

/// <summary>
/// Reports a deck intent description update.
/// </summary>
public sealed class DeckIntentChangeResult
{
    /// <summary>
    /// Gets or sets the updated workspace.
    /// </summary>
    public DeckWorkspace Workspace { get; set; } = new();

    /// <summary>
    /// Gets or sets the parsed intent result after the change.
    /// </summary>
    public DeckIntentResult Intent { get; set; } = new();

    /// <summary>
    /// Gets or sets the persistence target.
    /// </summary>
    public string Persistence { get; set; } = DeckPersistence.LocalOnly;

    /// <summary>
    /// Gets or sets a short change message.
    /// </summary>
    public string Message { get; set; } = "";
}
