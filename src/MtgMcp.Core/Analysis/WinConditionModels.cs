namespace MtgMcp.Core;

/// <summary>
/// Lists approved public win-route labels emitted by deterministic classifiers.
/// </summary>
public static class WinRouteLabels
{
    /// <summary>
    /// Indicates damage or lethal attacks through creatures.
    /// </summary>
    public const string Combat = "combat";

    /// <summary>
    /// Indicates token creation as a win-condition route.
    /// </summary>
    public const string Tokens = "tokens";

    /// <summary>
    /// Indicates storm-count or spell-copy payoff routes.
    /// </summary>
    public const string Storm = "storm";

    /// <summary>
    /// Indicates infinite mana production.
    /// </summary>
    public const string InfiniteMana = "infinite-mana";

    /// <summary>
    /// Indicates self-mill or self-library depletion routes.
    /// </summary>
    public const string SelfMill = "self-mill";

    /// <summary>
    /// Indicates opponent mill routes.
    /// </summary>
    public const string OpponentMill = "opponent-mill";

    /// <summary>
    /// Indicates extra turn routes.
    /// </summary>
    public const string ExtraTurns = "extra-turns";

    /// <summary>
    /// Indicates sacrifice, death-trigger, or drain routes.
    /// </summary>
    public const string Aristocrats = "aristocrats";

    /// <summary>
    /// Indicates explicit alternate-win or lose-the-game text.
    /// </summary>
    public const string AlternateWin = "alternate-win";

    /// <summary>
    /// Indicates value engines that commonly convert to combat damage.
    /// </summary>
    public const string ValueCombat = "value-combat";

    /// <summary>
    /// Indicates enters-the-battlefield loops or payoffs.
    /// </summary>
    public const string Etb = "etb";

    /// <summary>
    /// Indicates drawing most or all of a deck.
    /// </summary>
    public const string DrawDeck = "draw-deck";

    /// <summary>
    /// Gets all approved public route labels.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Combat,
        Tokens,
        Storm,
        InfiniteMana,
        SelfMill,
        OpponentMill,
        ExtraTurns,
        Aristocrats,
        AlternateWin,
        ValueCombat,
        Etb,
        DrawDeck
    ];
}

/// <summary>
/// Describes one deterministic route classification row.
/// </summary>
public sealed class WinRouteClassification
{
    /// <summary>
    /// Gets or sets the card, combo, or feature group being classified.
    /// </summary>
    public string Subject { get; set; } = "";

    /// <summary>
    /// Gets or sets approved route labels.
    /// </summary>
    public List<string> RouteTypes { get; set; } = [];

    /// <summary>
    /// Gets or sets whether the evidence directly describes a terminal win.
    /// </summary>
    public bool Terminal { get; set; }

    /// <summary>
    /// Gets or sets whether the route needs another payoff to actually win.
    /// </summary>
    public bool NeedsPayoff { get; set; }

    /// <summary>
    /// Gets or sets payoff categories needed for non-terminal routes.
    /// </summary>
    public List<string> PayoffKindsNeeded { get; set; } = [];

    /// <summary>
    /// Gets or sets exact features or card facets that drove the classification.
    /// </summary>
    public List<string> Evidence { get; set; } = [];

    /// <summary>
    /// Gets or sets source and determinism metadata.
    /// </summary>
    public SourceEvidenceMetadata Metadata { get; set; } = new();
}

/// <summary>
/// Reports deterministic route classification for one requested evidence input.
/// </summary>
public sealed class WinRouteClassificationResult
{
    /// <summary>
    /// Gets or sets the workspace id when a workspace was classified.
    /// </summary>
    public string? WorkspaceId { get; set; }

    /// <summary>
    /// Gets or sets the input mode used by the caller.
    /// </summary>
    public string InputKind { get; set; } = "";

    /// <summary>
    /// Gets or sets route classification rows.
    /// </summary>
    public List<WinRouteClassification> Classifications { get; set; } = [];

    /// <summary>
    /// Gets or sets non-fatal classification notes.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Reports Scryfall-query-derived payoff candidates for a route.
/// </summary>
public sealed class WinconPayoffSearchResult
{
    /// <summary>
    /// Gets or sets the requested route label.
    /// </summary>
    public string Route { get; set; } = "";

    /// <summary>
    /// Gets or sets the requested color identity.
    /// </summary>
    public List<string> ColorIdentity { get; set; } = [];

    /// <summary>
    /// Gets or sets the deck format used for legality filtering.
    /// </summary>
    public string Format { get; set; } = "commander";

    /// <summary>
    /// Gets or sets the Scryfall query used to find candidates.
    /// </summary>
    public string ScryfallQuery { get; set; } = "";

    /// <summary>
    /// Gets or sets payoff candidate rows.
    /// </summary>
    public List<WinconPayoffCandidate> Candidates { get; set; } = [];

    /// <summary>
    /// Gets or sets lookup notes and source limitations.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Describes one payoff candidate found by deterministic Scryfall queries.
/// </summary>
public sealed class WinconPayoffCandidate
{
    /// <summary>
    /// Gets or sets the candidate card name.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets why the card matched the route query.
    /// </summary>
    public string WhyItMatches { get; set; } = "";

    /// <summary>
    /// Gets or sets whether the card is legal in the requested format.
    /// </summary>
    public bool LegalInFormat { get; set; }

    /// <summary>
    /// Gets or sets whether the card is within the requested color identity.
    /// </summary>
    public bool ColorIdentityOk { get; set; }

    /// <summary>
    /// Gets or sets known USD price.
    /// </summary>
    public decimal? Price { get; set; }

    /// <summary>
    /// Gets or sets global EDHREC rank from Scryfall when known.
    /// </summary>
    public int? EdhrecRank { get; set; }

    /// <summary>
    /// Gets or sets the Scryfall card page.
    /// </summary>
    public string? ScryfallUri { get; set; }

    /// <summary>
    /// Gets or sets source and determinism metadata.
    /// </summary>
    public SourceEvidenceMetadata Metadata { get; set; } = new();
}

