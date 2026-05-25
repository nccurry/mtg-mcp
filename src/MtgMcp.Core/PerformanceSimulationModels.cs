namespace MtgMcp.Core;

/// <summary>
/// Represents one physical permanent rather than every copy of a deck card.
/// </summary>
internal sealed class PerformancePermanent
{
    /// <summary>
    /// Gets or sets the card represented by this permanent.
    /// </summary>
    public DeckCard Card { get; set; } = new();
}

/// <summary>
/// Stores the hand and library chosen after mulligans.
/// </summary>
internal sealed class PerformanceOpeningHand
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

    /// <summary>
    /// Gets or sets the land count in the first seven-card hand.
    /// </summary>
    public int OpeningSevenLands { get; set; }
}

/// <summary>
/// Captures deck-level facts used to choose and bottom mulligan hands.
/// </summary>
internal sealed class PerformanceMulliganContext
{
    /// <summary>
    /// Gets or sets whether the first mulligan does not reduce kept hand size.
    /// </summary>
    public bool FreeFirstMulligan { get; set; }

    /// <summary>
    /// Gets or sets the colors the deck must reliably access.
    /// </summary>
    public HashSet<string> DeckColors { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the commander available from the command zone.
    /// </summary>
    public DeckCard? Commander { get; set; }

    /// <summary>
    /// Gets or sets profile-specific mulligan weights and thresholds.
    /// </summary>
    public SimulationMulliganSettings Mulligan { get; set; } = new();
}

/// <summary>
/// Stores target turns for the built-in scenario suite.
/// </summary>
internal sealed class PerformanceScenarioDefaults
{
    /// <summary>
    /// Gets or sets the commander deployment target turn.
    /// </summary>
    public int CommanderTurn { get; set; }

    /// <summary>
    /// Gets or sets the commander protection target turn.
    /// </summary>
    public int ProtectionTurn { get; set; }

    /// <summary>
    /// Gets or sets the graveyard hate access target turn.
    /// </summary>
    public int HateTurn { get; set; }

    /// <summary>
    /// Gets or sets the color access target turn.
    /// </summary>
    public int ColorTurn { get; set; }

    /// <summary>
    /// Gets or sets the interaction access target turn.
    /// </summary>
    public int InteractionTurn { get; set; }

    /// <summary>
    /// Gets or sets the combo assembly target turn.
    /// </summary>
    public int ComboTurn { get; set; }

    /// <summary>
    /// Gets or sets whether deck intent changed any defaults.
    /// </summary>
    public bool IntentAdjusted { get; set; }
}

/// <summary>
/// Stores one sampled performance run.
/// </summary>
internal sealed class PerformanceRun
{
    /// <summary>
    /// Gets or sets how many mulligans were taken.
    /// </summary>
    public int Mulligans { get; set; }

    /// <summary>
    /// Gets or sets the final kept hand size.
    /// </summary>
    public int KeptHandSize { get; set; }

    /// <summary>
    /// Gets or sets the land count in the kept hand.
    /// </summary>
    public int KeptOpeningLands { get; set; }

    /// <summary>
    /// Gets or sets the land count in the first seven-card hand.
    /// </summary>
    public int OpeningSevenLands { get; set; }

    /// <summary>
    /// Gets or sets the earliest commander cast turn.
    /// </summary>
    public int? CommanderCastTurn { get; set; }

    /// <summary>
    /// Gets or sets the earliest commander protected turn.
    /// </summary>
    public int? CommanderProtectedTurn { get; set; }

    /// <summary>
    /// Gets or sets the earliest two-piece combo assembly turn.
    /// </summary>
    public int? ComboAssemblyTurn { get; set; }

    /// <summary>
    /// Gets or sets the earliest tutor-assisted combo turn.
    /// </summary>
    public int? TutorAssistedComboTurn { get; set; }

    /// <summary>
    /// Gets or sets turn-by-turn state snapshots.
    /// </summary>
    public List<PerformanceTurnState> Turns { get; set; } = [];

    /// <summary>
    /// Gets or sets stranded cards keyed by name.
    /// </summary>
    public Dictionary<string, PerformanceStrandedRun> StrandedCards { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Stores one turn snapshot within a performance run.
/// </summary>
internal sealed class PerformanceTurnState
{
    /// <summary>
    /// Gets or sets the turn number.
    /// </summary>
    public int Turn { get; set; }

    /// <summary>
    /// Gets or sets lands currently in play.
    /// </summary>
    public int LandsInPlay { get; set; }

    /// <summary>
    /// Gets or sets total mana sources in play.
    /// </summary>
    public int ManaSources { get; set; }

    /// <summary>
    /// Gets or sets unused mana after heuristic development.
    /// </summary>
    public int AvailableMana { get; set; }

    /// <summary>
    /// Gets or sets colors currently available from untapped mana sources.
    /// </summary>
    public HashSet<string> ColorSources { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets untapped mana sources available before spending this turn.
    /// </summary>
    public List<PerformanceManaSource> UntappedManaSources { get; set; } = [];

    /// <summary>
    /// Gets or sets whether a land was played this turn.
    /// </summary>
    public bool LandDropMade { get; set; }

    /// <summary>
    /// Gets or sets whether untapped mana kept pace with the turn number.
    /// </summary>
    public bool OnCurveUntappedMana { get; set; }

    /// <summary>
    /// Gets or sets whether ramp has been seen by this turn.
    /// </summary>
    public bool RampSeenByTurn { get; set; }

    /// <summary>
    /// Gets or sets whether ramp has been cast by this turn.
    /// </summary>
    public bool RampCastByTurn { get; set; }

    /// <summary>
    /// Gets or sets whether draw has been seen by this turn.
    /// </summary>
    public bool DrawSeenByTurn { get; set; }

    /// <summary>
    /// Gets or sets whether draw has been cast by this turn.
    /// </summary>
    public bool DrawCastByTurn { get; set; }

    /// <summary>
    /// Gets or sets whether interaction has been seen by this turn.
    /// </summary>
    public bool InteractionSeenByTurn { get; set; }

    /// <summary>
    /// Gets or sets whether protection has been seen by this turn.
    /// </summary>
    public bool ProtectionSeenByTurn { get; set; }

    /// <summary>
    /// Gets or sets whether graveyard hate has been seen by this turn.
    /// </summary>
    public bool GraveyardHateSeenByTurn { get; set; }

    /// <summary>
    /// Gets or sets whether interaction can be held up with unused mana.
    /// </summary>
    public bool InteractionHeldUp { get; set; }

    /// <summary>
    /// Gets or sets whether protection can be held up with unused mana.
    /// </summary>
    public bool ProtectionHeldUp { get; set; }

    /// <summary>
    /// Gets or sets the share of nonland hand cards that are castable.
    /// </summary>
    public double CastableHandRate { get; set; }

    /// <summary>
    /// Gets or sets the hand size at end of turn.
    /// </summary>
    public int CardsInHand { get; set; }

    /// <summary>
    /// Gets or sets whether all inferred deck colors are available.
    /// </summary>
    public bool AllDeckColorsAvailable { get; set; }

    /// <summary>
    /// Gets or sets whether the commander has been cast by this turn.
    /// </summary>
    public bool CommanderCastByTurn { get; set; }

    /// <summary>
    /// Gets or sets whether the commander is protected by this turn.
    /// </summary>
    public bool CommanderProtectedByTurn { get; set; }

    /// <summary>
    /// Gets or sets how many distinct combo cards have been seen.
    /// </summary>
    public int ComboPiecesSeen { get; set; }

    /// <summary>
    /// Gets or sets whether a two-card combo has assembled by this turn.
    /// </summary>
    public bool ComboAssemblyByTurn { get; set; }

    /// <summary>
    /// Gets or sets whether a tutor-assisted combo has assembled by this turn.
    /// </summary>
    public bool TutorAssistedComboByTurn { get; set; }
}

/// <summary>
/// Stores one stranded-card observation for a run.
/// </summary>
internal sealed class PerformanceStrandedRun
{
    /// <summary>
    /// Gets or sets the stranded card name.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets the stranded card mana value.
    /// </summary>
    public double ManaValue { get; set; }

    /// <summary>
    /// Gets or sets whether mana quantity caused the stranding.
    /// </summary>
    public bool ManaStranded { get; set; }

    /// <summary>
    /// Gets or sets whether missing colors caused the stranding.
    /// </summary>
    public bool ColorStranded { get; set; }

    /// <summary>
    /// Gets or sets the final turn checked for stranding.
    /// </summary>
    public int FinalTurn { get; set; }
}
