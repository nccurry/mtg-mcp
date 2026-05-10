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
    /// Gets or sets the desired power level.
    /// </summary>
    public string? PowerLevel { get; set; }

    /// <summary>
    /// Gets or sets the named deckbuilding heuristic profile.
    /// </summary>
    public string? HeuristicProfile { get; set; }

    /// <summary>
    /// Gets or sets the named package template.
    /// </summary>
    public string? PackageTemplate { get; set; }

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
    /// Gets or sets desired package count targets.
    /// </summary>
    public Dictionary<string, DeckIntentTarget> Packages { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

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
