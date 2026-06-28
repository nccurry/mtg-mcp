namespace MtgMcp.Core;

/// <summary>
/// Contains private state types used by the heuristic goldfish simulator.
/// </summary>
public sealed partial class DeckSimulationService
{
    /// <summary>
    /// Stores one goldfish run.
    /// </summary>
    private sealed class GoldfishRun
    {
        /// <summary>
        /// Gets or sets whether the run mulliganed.
        /// </summary>
        public bool Mulliganed { get; set; }

        /// <summary>
        /// Gets or sets the win turn.
        /// </summary>
        public int? WinTurn { get; set; }

        /// <summary>
        /// Gets or sets the earliest non-Background commander cast turn.
        /// </summary>
        public int? CommanderCastTurn { get; set; }

        /// <summary>
        /// Gets or sets the earliest Background cast turn.
        /// </summary>
        public int? BackgroundCastTurn { get; set; }

        /// <summary>
        /// Gets or sets the earliest turn where commander and Background were both online.
        /// </summary>
        public int? CommanderWithBackgroundOnlineTurn { get; set; }

        /// <summary>
        /// Gets or sets the win route.
        /// </summary>
        public string? WinRoute { get; set; }

        /// <summary>
        /// Gets or sets turn snapshots.
        /// </summary>
        public List<GoldfishTurnSnapshot> Turns { get; set; } = [];

        /// <summary>
        /// Gets or sets the representative line.
        /// </summary>
        public List<string> Line { get; set; } = [];

        /// <summary>
        /// Gets or sets deterministic route evidence captured during the run.
        /// </summary>
        public List<SimulationRouteEvidence> RouteEvidence { get; set; } = [];
    }

    /// <summary>
    /// Stores a goldfish opening hand after mulligans.
    /// </summary>
    private sealed class GoldfishOpeningHand
    {
        /// <summary>
        /// Gets or sets the kept hand.
        /// </summary>
        public List<DeckCard> Hand { get; set; } = [];

        /// <summary>
        /// Gets or sets the remaining library.
        /// </summary>
        public List<DeckCard> Library { get; set; } = [];

        /// <summary>
        /// Gets or sets how many mulligans were taken.
        /// </summary>
        public int Mulligans { get; set; }
    }

    /// <summary>
    /// Stores one simulated turn snapshot.
    /// </summary>
    private sealed class GoldfishTurnSnapshot
    {
        /// <summary>
        /// Gets or sets the turn number.
        /// </summary>
        public int Turn { get; set; }

        /// <summary>
        /// Gets or sets lands in play.
        /// </summary>
        public int Lands { get; set; }

        /// <summary>
        /// Gets or sets mana sources in play.
        /// </summary>
        public int ManaSources { get; set; }

        /// <summary>
        /// Gets or sets nonland permanents in play.
        /// </summary>
        public int NonlandPermanents { get; set; }

        /// <summary>
        /// Gets or sets cards in hand.
        /// </summary>
        public int CardsInHand { get; set; }

        /// <summary>
        /// Gets or sets battlefield power.
        /// </summary>
        public int Power { get; set; }

        /// <summary>
        /// Gets or sets token count.
        /// </summary>
        public int Tokens { get; set; }

        /// <summary>
        /// Gets or sets the bounded threat-pressure score for this turn.
        /// </summary>
        public int ThreatPressure { get; set; }

        /// <summary>
        /// Gets or sets whether a repeatable engine appeared online by this turn.
        /// </summary>
        public bool EngineOnline { get; set; }

        /// <summary>
        /// Gets activated commander engine pressure evidence for this turn.
        /// </summary>
        public ActivatedCommanderEnginePressure EnginePressure { get; set; } = new();

        /// <summary>
        /// Gets sorcery finisher pressure evidence for this turn.
        /// </summary>
        public SorceryFinisherPressure SorceryFinisherPressure { get; set; } = new();

        /// <summary>
        /// Gets or sets whether a non-Background commander had been cast by this turn.
        /// </summary>
        public bool CommanderCastByTurn { get; set; }

        /// <summary>
        /// Gets or sets whether a Background had been cast by this turn.
        /// </summary>
        public bool BackgroundCastByTurn { get; set; }

        /// <summary>
        /// Gets or sets whether a commander and Background were both online by this turn.
        /// </summary>
        public bool CommanderWithBackgroundOnlineByTurn { get; set; }
    }

    /// <summary>
    /// Carries the bounded cast-cost estimate used by one goldfish spell cast.
    /// </summary>
    private sealed record GoldfishCastCost(int RequiredMana, int XValue)
    {
        /// <summary>
        /// Total mana the sequencer spends, including chosen X value.
        /// </summary>
        public int TotalManaSpent => RequiredMana + XValue;
    }

    /// <summary>
    /// Carries token subcounts from one resolved spell.
    /// </summary>
    private sealed record GoldfishTokenProduction(int Total, int ArtifactTokens, int FoodTokens);

    /// <summary>
    /// Lists hand-spell sequencing windows around delayed command-zone deployment.
    /// </summary>
    private enum GoldfishSpellWindow
    {
        /// <summary>
        /// Cast every eligible spell.
        /// </summary>
        All,

        /// <summary>
        /// Cast only setup spells before delayed command-zone deployment.
        /// </summary>
        SetupOnly,

        /// <summary>
        /// Cast only non-setup spells after delayed command-zone deployment.
        /// </summary>
        NonSetup,
    }
}
