namespace MtgMcp.Core;

/// <summary>
/// Builds deterministic command-zone deployment plans for heuristic simulators.
/// </summary>
internal static class CommandZonePlanner
{
    /// <summary>
    /// Builds an ordered command-zone plan from included commander-category cards and profile sequencing settings.
    /// </summary>
    public static CommandZonePlan Build(IEnumerable<DeckCard> included, SimulationProfile profile)
    {
        List<CommandZoneCardPlan> cards = [];
        foreach (DeckCard card in included)
        {
            if (!IsCommandZoneCard(card))
            {
                continue;
            }

            cards.Add(new CommandZoneCardPlan
            {
                Card = card,
                Kind = Classify(card),
            });
        }

        bool hasCommander = cards.Any(card => card.Kind == CommandZoneCardKind.Commander);
        bool hasBackground = cards.Any(card => card.Kind == CommandZoneCardKind.Background);
        bool hasChooseBackground = cards.Any(card =>
            card.Kind == CommandZoneCardKind.Commander && ChoosesBackground(card.Card));
        bool hasBackgroundPair = hasBackground && hasCommander && hasChooseBackground;
        List<CommandZoneCardPlan> ordered = OrderCards(cards, profile.Sequencing, hasBackgroundPair);
        AssignTargetTurns(ordered, profile, hasBackgroundPair);

        return new CommandZonePlan
        {
            Cards = ordered,
            HasCommander = hasCommander,
            HasBackground = hasBackground,
            PrimaryCommander = ordered.FirstOrDefault(card => card.Kind == CommandZoneCardKind.Commander)?.Card,
        };
    }

    /// <summary>
    /// Checks whether a card sits in the command zone for the current workspace.
    /// </summary>
    private static bool IsCommandZoneCard(DeckCard card)
    {
        return DeckCategoryOrdering.PrimaryCategory(card).Equals(
            DeckRoles.Commander,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Classifies a command-zone card for sequencing and timing reports.
    /// </summary>
    private static CommandZoneCardKind Classify(DeckCard card)
    {
        return IsBackground(card)
            ? CommandZoneCardKind.Background
            : CommandZoneCardKind.Commander;
    }

    /// <summary>
    /// Checks whether a command-zone card is a Background.
    /// </summary>
    private static bool IsBackground(DeckCard card)
    {
        return (card.Snapshot?.TypeLine ?? "").Contains(
            "Background",
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether a creature commander can choose a Background.
    /// </summary>
    private static bool ChoosesBackground(DeckCard card)
    {
        return (card.Snapshot?.OracleText ?? "").Contains(
            "Choose a Background",
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Orders command-zone cards from explicit profile tokens or deterministic defaults.
    /// </summary>
    private static List<CommandZoneCardPlan> OrderCards(
        IReadOnlyList<CommandZoneCardPlan> cards,
        SimulationSequencingSettings settings,
        bool hasBackgroundPair)
    {
        List<CommandZoneCardPlan> ordered = [];
        foreach (string token in settings.CommandZoneOrder)
        {
            AddMatchingCards(ordered, cards, token);
        }

        if (ordered.Count == 0 && !settings.PreferCommanderOnCurve && hasBackgroundPair)
        {
            AddMatchingCards(ordered, cards, "Background");
            AddMatchingCards(ordered, cards, "Commander");
        }

        AddRemainingCards(ordered, cards, CommandZoneCardKind.Commander);
        AddRemainingCards(ordered, cards, CommandZoneCardKind.Background);
        return ordered;
    }

    /// <summary>
    /// Adds cards matching one order token, preserving workspace order within the token group.
    /// </summary>
    private static void AddMatchingCards(
        List<CommandZoneCardPlan> ordered,
        IReadOnlyList<CommandZoneCardPlan> cards,
        string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        foreach (CommandZoneCardPlan card in cards)
        {
            if (ordered.Contains(card) || !MatchesToken(card, token))
            {
                continue;
            }

            ordered.Add(card);
        }
    }

    /// <summary>
    /// Checks whether a card matches one command-zone order token.
    /// </summary>
    private static bool MatchesToken(CommandZoneCardPlan card, string token)
    {
        return token.Equals("Commander", StringComparison.OrdinalIgnoreCase)
            ? card.Kind == CommandZoneCardKind.Commander
            : token.Equals("Background", StringComparison.OrdinalIgnoreCase)
                ? card.Kind == CommandZoneCardKind.Background
                : card.Card.Name.Equals(token, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Adds still-unordered cards of one kind.
    /// </summary>
    private static void AddRemainingCards(
        List<CommandZoneCardPlan> ordered,
        IReadOnlyList<CommandZoneCardPlan> cards,
        CommandZoneCardKind kind)
    {
        foreach (CommandZoneCardPlan card in cards)
        {
            if (card.Kind == kind && !ordered.Contains(card))
            {
                ordered.Add(card);
            }
        }
    }

    /// <summary>
    /// Assigns no-earlier-than deployment turns to each command-zone card.
    /// </summary>
    private static void AssignTargetTurns(
        IEnumerable<CommandZoneCardPlan> cards,
        SimulationProfile profile,
        bool hasBackgroundPair)
    {
        int commanderTurn = profile.Sequencing.PreferredCommanderTurn
            ?? (!profile.Sequencing.PreferCommanderOnCurve ? profile.Scenarios.CommanderTurn : 1);
        commanderTurn = Math.Max(1, commanderTurn);

        int backgroundTurn = profile.Sequencing.PreferredBackgroundTurn
            ?? (!profile.Sequencing.PreferCommanderOnCurve && hasBackgroundPair
                ? Math.Max(1, commanderTurn - 1)
                : 1);
        backgroundTurn = Math.Max(1, backgroundTurn);

        foreach (CommandZoneCardPlan card in cards)
        {
            card.TargetTurn = card.Kind switch
            {
                CommandZoneCardKind.Background => backgroundTurn,
                CommandZoneCardKind.Commander => commanderTurn,
                _ => commanderTurn,
            };
        }
    }
}

/// <summary>
/// Lists supported command-zone card roles for heuristic sequencing.
/// </summary>
internal enum CommandZoneCardKind
{
    /// <summary>
    /// Represents a non-Background commander card.
    /// </summary>
    Commander,

    /// <summary>
    /// Represents a Background card in the command zone.
    /// </summary>
    Background,
}

/// <summary>
/// Describes one card in a command-zone deployment plan.
/// </summary>
internal sealed class CommandZoneCardPlan
{
    /// <summary>
    /// Gets or sets the workspace card.
    /// </summary>
    public DeckCard Card { get; set; } = new();

    /// <summary>
    /// Gets or sets the card's command-zone role.
    /// </summary>
    public CommandZoneCardKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the first turn where the simulator should consider deploying this card.
    /// </summary>
    public int TargetTurn { get; set; } = 1;
}

/// <summary>
/// Stores a deterministic command-zone deployment plan.
/// </summary>
internal sealed class CommandZonePlan
{
    /// <summary>
    /// Gets or sets the ordered command-zone card plan.
    /// </summary>
    public List<CommandZoneCardPlan> Cards { get; set; } = [];

    /// <summary>
    /// Gets or sets whether the plan contains a non-Background commander.
    /// </summary>
    public bool HasCommander { get; set; }

    /// <summary>
    /// Gets or sets whether the plan contains a Background.
    /// </summary>
    public bool HasBackground { get; set; }

    /// <summary>
    /// Gets or sets the primary non-Background commander used by legacy commander metrics.
    /// </summary>
    public DeckCard? PrimaryCommander { get; set; }
}

/// <summary>
/// Tracks command-zone cards deployed during one simulation run.
/// </summary>
internal sealed class CommandZoneRunState
{
    /// <summary>
    /// Stores the immutable command-zone plan for this run.
    /// </summary>
    private readonly CommandZonePlan plan;

    /// <summary>
    /// Stores cards already deployed from the command zone.
    /// </summary>
    private readonly HashSet<DeckCard> castCards = [];

    /// <summary>
    /// Creates state for one command-zone simulation run.
    /// </summary>
    public CommandZoneRunState(CommandZonePlan plan)
    {
        this.plan = plan;
    }

    /// <summary>
    /// Gets the earliest non-Background commander cast turn.
    /// </summary>
    public int? CommanderCastTurn { get; private set; }

    /// <summary>
    /// Gets the earliest Background cast turn.
    /// </summary>
    public int? BackgroundCastTurn { get; private set; }

    /// <summary>
    /// Gets the earliest turn where a commander and Background were both online.
    /// </summary>
    public int? CommanderWithBackgroundOnlineTurn { get; private set; }

    /// <summary>
    /// Gets whether at least one non-Background commander has been cast.
    /// </summary>
    public bool CommanderOnline => CommanderCastTurn.HasValue;

    /// <summary>
    /// Gets whether at least one Background has been cast.
    /// </summary>
    public bool BackgroundOnline => BackgroundCastTurn.HasValue;

    /// <summary>
    /// Gets whether a card has already been deployed from the command zone.
    /// </summary>
    public bool IsCast(CommandZoneCardPlan card)
    {
        return castCards.Contains(card.Card);
    }

    /// <summary>
    /// Gets the next command-zone card that must be considered before later command-zone cards.
    /// </summary>
    public CommandZoneCardPlan? NextPending()
    {
        foreach (CommandZoneCardPlan card in plan.Cards)
        {
            if (!IsCast(card))
            {
                return card;
            }
        }

        return null;
    }

    /// <summary>
    /// Records that a command-zone card was cast this turn.
    /// </summary>
    public void MarkCast(CommandZoneCardPlan card, int turn)
    {
        castCards.Add(card.Card);
        if (card.Kind == CommandZoneCardKind.Commander)
        {
            CommanderCastTurn ??= turn;
        }

        if (card.Kind == CommandZoneCardKind.Background)
        {
            BackgroundCastTurn ??= turn;
        }

        if (plan.HasCommander && plan.HasBackground && CommanderOnline && BackgroundOnline)
        {
            CommanderWithBackgroundOnlineTurn ??= turn;
        }
    }
}
