namespace MtgMcp.Core;

/// <summary>
/// Describes one deterministic goldfish race request.
/// </summary>
public sealed class RulesGoldfishRaceRequest
{
    /// <summary>
    /// Gets or sets the model name reported to callers.
    /// </summary>
    public string ModelName { get; set; } = RulesGoldfishRaceConstants.ModelName;

    /// <summary>
    /// Gets or sets the decks racing against the same life-total target.
    /// </summary>
    public List<RulesGoldfishRaceDeck> Decks { get; set; } = [];

    /// <summary>
    /// Gets or sets the number of paired simulations.
    /// </summary>
    public int Simulations { get; set; } = 100;

    /// <summary>
    /// Gets or sets the caller-visible replay seed.
    /// </summary>
    public int Seed { get; set; } = 1337;

    /// <summary>
    /// Gets or sets the opponent life total each deck must race through.
    /// </summary>
    public int StartingLife { get; set; } = 40;

    /// <summary>
    /// Gets or sets the last turn considered by the race.
    /// </summary>
    public int TurnLimit { get; set; } = 12;

    /// <summary>
    /// Gets or sets whether Commander-style London mulligans are used.
    /// </summary>
    public bool Mulligan { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the first seat draws on turn one.
    /// </summary>
    public bool FirstPlayerDraws { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of trace lines retained for each deck summary.
    /// </summary>
    public int TraceLimit { get; set; } = 16;
}

/// <summary>
/// Stable constants for the conservative goldfish race model.
/// </summary>
public static class RulesGoldfishRaceConstants
{
    /// <summary>
    /// Identifies the opt-in conservative race model.
    /// </summary>
    public const string ModelName = "rules-backed-goldfish-race-v1";

    /// <summary>
    /// Identifies this implementation version.
    /// </summary>
    public const string EngineVersion = "conservative-template-race-v1";
}

/// <summary>
/// Describes one deck after card snapshots have been compiled into conservative templates.
/// </summary>
public sealed class RulesGoldfishRaceDeck
{
    /// <summary>
    /// Gets or sets the comparison label.
    /// </summary>
    public string Label { get; set; } = "";

    /// <summary>
    /// Gets or sets the workspace id when the deck came from a workspace.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets library cards and non-command-zone cards.
    /// </summary>
    public List<RulesGoldfishRaceCard> Cards { get; set; } = [];

    /// <summary>
    /// Gets or sets cards available from the command zone.
    /// </summary>
    public List<RulesGoldfishRaceCard> CommandZoneCards { get; set; } = [];

    /// <summary>
    /// Gets or sets non-fatal compiler warnings for this deck.
    /// </summary>
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// Describes the conservative simulation template for one card.
/// </summary>
public sealed class RulesGoldfishRaceCard
{
    /// <summary>
    /// Gets or sets the card name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the number of copies represented by this template.
    /// </summary>
    public int Quantity { get; set; } = 1;

    /// <summary>
    /// Gets or sets the generic mana needed to cast this template.
    /// </summary>
    public int ManaValue { get; set; }

    /// <summary>
    /// Gets or sets whether this template is a land.
    /// </summary>
    public bool IsLand { get; set; }

    /// <summary>
    /// Gets or sets whether this template is a creature.
    /// </summary>
    public bool IsCreature { get; set; }

    /// <summary>
    /// Gets or sets whether this template remains on the battlefield when cast.
    /// </summary>
    public bool StaysOnBattlefield { get; set; }

    /// <summary>
    /// Gets or sets power used for combat math.
    /// </summary>
    public int Power { get; set; }

    /// <summary>
    /// Gets or sets toughness kept for traces and later model expansion.
    /// </summary>
    public int Toughness { get; set; }

    /// <summary>
    /// Gets or sets reusable mana produced on later turns.
    /// </summary>
    public int ManaProduced { get; set; }

    /// <summary>
    /// Gets or sets whether reusable mana is produced by a creature.
    /// </summary>
    public bool ManaSourceIsCreature { get; set; }

    /// <summary>
    /// Gets or sets the number of cards drawn when this template resolves.
    /// </summary>
    public int DrawCards { get; set; }

    /// <summary>
    /// Gets or sets the number of basic ramp lands added for later turns.
    /// </summary>
    public int RampLands { get; set; }

    /// <summary>
    /// Gets or sets life lost by the goldfish target when this template resolves.
    /// </summary>
    public int LifeLoss { get; set; }

    /// <summary>
    /// Gets or sets the number of creature tokens created when this template resolves.
    /// </summary>
    public int CreateTokens { get; set; }

    /// <summary>
    /// Gets or sets token power.
    /// </summary>
    public int TokenPower { get; set; } = 1;

    /// <summary>
    /// Gets or sets token toughness.
    /// </summary>
    public int TokenToughness { get; set; } = 1;

    /// <summary>
    /// Gets or sets team power added during combat.
    /// </summary>
    public int TeamPowerBonus { get; set; }

    /// <summary>
    /// Gets or sets whether attacking creatures deal double combat damage.
    /// </summary>
    public bool GrantsTeamDoubleStrike { get; set; }

    /// <summary>
    /// Gets or sets whether the team can attack the turn it enters.
    /// </summary>
    public bool GrantsTeamHaste { get; set; }

    /// <summary>
    /// Gets or sets whether this template is explicitly recognized as a combat payoff.
    /// </summary>
    public bool IsCombatPayoff { get; set; }
}

/// <summary>
/// Reports the result of a conservative goldfish race.
/// </summary>
public sealed class RulesGoldfishRaceResult
{
    /// <summary>
    /// Gets or sets the model name requested by the caller.
    /// </summary>
    public string ModelName { get; set; } = RulesGoldfishRaceConstants.ModelName;

    /// <summary>
    /// Gets or sets the implementation version.
    /// </summary>
    public string EngineVersion { get; set; } = RulesGoldfishRaceConstants.EngineVersion;

    /// <summary>
    /// Gets or sets the deterministic random source kind.
    /// </summary>
    public string RandomKind { get; set; } = DeterministicSimulationRandom.Kind;

    /// <summary>
    /// Gets or sets the caller-visible seed.
    /// </summary>
    public int Seed { get; set; }

    /// <summary>
    /// Gets or sets the paired simulation count.
    /// </summary>
    public int Simulations { get; set; }

    /// <summary>
    /// Gets or sets the life total each deck raced against.
    /// </summary>
    public int StartingLife { get; set; }

    /// <summary>
    /// Gets or sets the final simulated turn.
    /// </summary>
    public int TurnLimit { get; set; }

    /// <summary>
    /// Gets or sets whether Commander-style London mulligans were used.
    /// </summary>
    public bool Mulligan { get; set; }

    /// <summary>
    /// Gets or sets whether the first seat drew on turn one.
    /// </summary>
    public bool FirstPlayerDraws { get; set; }

    /// <summary>
    /// Gets or sets the ordered seat labels.
    /// </summary>
    public List<string> SeatOrder { get; set; } = [];

    /// <summary>
    /// Gets or sets the replay contract for paired seeds.
    /// </summary>
    public string SeedPolicy { get; set; } = "";

    /// <summary>
    /// Gets or sets the tie and draw policy.
    /// </summary>
    public string TiePolicy { get; set; } = "";

    /// <summary>
    /// Gets or sets whether commander damage is excluded from win checks.
    /// </summary>
    public bool CommanderDamageIgnored { get; set; } = true;

    /// <summary>
    /// Gets or sets per-deck race summaries.
    /// </summary>
    public List<RulesGoldfishRaceDeckSummary> Decks { get; set; } = [];

    /// <summary>
    /// Gets or sets bounded per-run outcome samples.
    /// </summary>
    public List<RulesGoldfishRaceOutcome> SampleOutcomes { get; set; } = [];

    /// <summary>
    /// Gets or sets comparison inputs that could not be loaded before the race.
    /// </summary>
    public List<GoldfishReferenceImportFailure> Failures { get; set; } = [];

    /// <summary>
    /// Gets or sets conservative model notes.
    /// </summary>
    public List<string> Notes { get; set; } = [];

    /// <summary>
    /// Gets or sets non-fatal warnings.
    /// </summary>
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// Summarizes one deck's race outcomes.
/// </summary>
public sealed class RulesGoldfishRaceDeckSummary
{
    /// <summary>
    /// Gets or sets the comparison label.
    /// </summary>
    public string Label { get; set; } = "";

    /// <summary>
    /// Gets or sets the one-based seat.
    /// </summary>
    public int Seat { get; set; }

    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the deck name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the number of sole wins.
    /// </summary>
    public int Wins { get; set; }

    /// <summary>
    /// Gets or sets the number of same-turn lethal ties.
    /// </summary>
    public int Ties { get; set; }

    /// <summary>
    /// Gets or sets the number of no-lethal draws.
    /// </summary>
    public int Draws { get; set; }

    /// <summary>
    /// Gets or sets the number of losses.
    /// </summary>
    public int Losses { get; set; }

    /// <summary>
    /// Gets or sets the sole-win rate.
    /// </summary>
    public double WinRate { get; set; }

    /// <summary>
    /// Gets or sets the same-turn lethal tie rate.
    /// </summary>
    public double TieRate { get; set; }

    /// <summary>
    /// Gets or sets the runs where this deck reached lethal.
    /// </summary>
    public int LethalRuns { get; set; }

    /// <summary>
    /// Gets or sets the median observed lethal turn when any run was lethal.
    /// </summary>
    public int? MedianLethalTurn { get; set; }

    /// <summary>
    /// Gets or sets lethal-turn counts.
    /// </summary>
    public Dictionary<int, int> LethalTurnCounts { get; set; } = [];

    /// <summary>
    /// Gets or sets a bounded representative trace.
    /// </summary>
    public List<string> RepresentativeTrace { get; set; } = [];

    /// <summary>
    /// Gets or sets warnings that apply to this deck.
    /// </summary>
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// Reports one sampled paired race outcome.
/// </summary>
public sealed class RulesGoldfishRaceOutcome
{
    /// <summary>
    /// Gets or sets the zero-based run index.
    /// </summary>
    public int Run { get; set; }

    /// <summary>
    /// Gets or sets the sole winner label, when one exists.
    /// </summary>
    public string? WinnerLabel { get; set; }

    /// <summary>
    /// Gets or sets labels that reached lethal on the same earliest turn.
    /// </summary>
    public List<string> TiedLabels { get; set; } = [];

    /// <summary>
    /// Gets or sets whether no deck reached lethal by the turn limit.
    /// </summary>
    public bool IsDraw { get; set; }

    /// <summary>
    /// Gets or sets the earliest lethal turn when any deck reached lethal.
    /// </summary>
    public int? LethalTurn { get; set; }
}
