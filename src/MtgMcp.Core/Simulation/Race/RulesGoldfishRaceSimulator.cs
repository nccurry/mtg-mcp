namespace MtgMcp.Core;

/// <summary>
/// Runs a conservative template-based life-total race without stack or priority decisions.
/// </summary>
public static class RulesGoldfishRaceSimulator
{
    /// <summary>
    /// Runs paired goldfish races and compares earliest lethal turns.
    /// </summary>
    public static RulesGoldfishRaceResult Run(RulesGoldfishRaceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Decks.Count < 2)
        {
            throw new ArgumentException("Rules-backed goldfish race requires at least two decks.", nameof(request));
        }

        int simulations = Math.Clamp(request.Simulations, 1, 10_000);
        int startingLife = Math.Clamp(request.StartingLife, 1, 200);
        int turnLimit = Math.Clamp(request.TurnLimit, 1, 30);
        int traceLimit = Math.Clamp(request.TraceLimit, 0, 64);
        List<DeckAccumulator> accumulators = [];
        for (int seat = 0; seat < request.Decks.Count; seat++)
        {
            RulesGoldfishRaceDeck deck = request.Decks[seat];
            accumulators.Add(new DeckAccumulator(deck, seat + 1));
        }

        RulesGoldfishRaceResult result = BuildBaseResult(
            request,
            simulations,
            startingLife,
            turnLimit);
        for (int run = 0; run < simulations; run++)
        {
            List<DeckRunResult> runResults = [];
            for (int seat = 0; seat < request.Decks.Count; seat++)
            {
                DeckRunResult runResult = RunDeck(
                    request.Decks[seat],
                    seat,
                    run,
                    request.Seed,
                    startingLife,
                    turnLimit,
                    request.Mulligan,
                    request.FirstPlayerDraws,
                    traceLimit);
                runResults.Add(runResult);
                accumulators[seat].Add(runResult);
            }

            ApplyRaceOutcome(accumulators, result, runResults, run, simulations);
        }

        foreach (DeckAccumulator accumulator in accumulators)
        {
            result.Decks.Add(accumulator.BuildSummary(simulations));
            foreach (string warning in accumulator.Deck.Warnings)
            {
                result.Warnings.Add($"{accumulator.Deck.Label}: {warning}");
            }
        }

        result.Warnings = result.Warnings
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return result;
    }

    /// <summary>
    /// Builds result metadata shared by every race.
    /// </summary>
    private static RulesGoldfishRaceResult BuildBaseResult(
        RulesGoldfishRaceRequest request,
        int simulations,
        int startingLife,
        int turnLimit)
    {
        RulesGoldfishRaceResult result = new()
        {
            ModelName = request.ModelName,
            Seed = request.Seed,
            Simulations = simulations,
            StartingLife = startingLife,
            TurnLimit = turnLimit,
            Mulligan = request.Mulligan,
            FirstPlayerDraws = request.FirstPlayerDraws,
            SeatOrder = request.Decks.Select(deck => deck.Label).ToList(),
            SeedPolicy = "Each run deterministically mixes the caller seed with the run index, seat index, and mulligan attempt; the same inputs replay the same paired shuffles.",
            TiePolicy = "Same-turn lethal is recorded as a tie for those decks; runs with no lethal by the turn limit are draws.",
            CommanderDamageIgnored = true,
            Notes =
            [
                "Conservative template simulator, not a full Magic rules engine: no stack, priority, blockers, targeted interaction, layers, replacement effects, or prevention effects.",
                "Each deck races a goldfish target at the configured starting life; decks do not damage or disrupt each other.",
                "Command-zone cards are removed from the library and can be cast once from the command zone when mana is available.",
                "Commander damage is ignored; only normal life-total damage and life loss are checked for lethal.",
            ],
        };
        return result;
    }

    /// <summary>
    /// Applies one paired run outcome to aggregate deck records.
    /// </summary>
    private static void ApplyRaceOutcome(
        List<DeckAccumulator> accumulators,
        RulesGoldfishRaceResult result,
        List<DeckRunResult> runResults,
        int run,
        int simulations)
    {
        int? earliestLethal = null;
        foreach (DeckRunResult runResult in runResults)
        {
            if (runResult.LethalTurn.HasValue)
            {
                earliestLethal = earliestLethal.HasValue
                    ? Math.Min(earliestLethal.Value, runResult.LethalTurn.Value)
                    : runResult.LethalTurn.Value;
            }
        }

        List<int> winningSeats = [];
        if (earliestLethal.HasValue)
        {
            for (int seat = 0; seat < runResults.Count; seat++)
            {
                if (runResults[seat].LethalTurn == earliestLethal)
                {
                    winningSeats.Add(seat);
                }
            }
        }

        if (!earliestLethal.HasValue)
        {
            foreach (DeckAccumulator accumulator in accumulators)
            {
                accumulator.Draws++;
            }
        }
        else if (winningSeats.Count == 1)
        {
            for (int seat = 0; seat < accumulators.Count; seat++)
            {
                if (seat == winningSeats[0])
                {
                    accumulators[seat].Wins++;
                }
                else
                {
                    accumulators[seat].Losses++;
                }
            }
        }
        else
        {
            for (int seat = 0; seat < accumulators.Count; seat++)
            {
                if (winningSeats.Contains(seat))
                {
                    accumulators[seat].Ties++;
                }
                else
                {
                    accumulators[seat].Losses++;
                }
            }
        }

        if (result.SampleOutcomes.Count < Math.Min(10, simulations))
        {
            result.SampleOutcomes.Add(BuildOutcomeSample(runResults, winningSeats, run, earliestLethal));
        }
    }

    /// <summary>
    /// Builds a bounded outcome row for a paired run.
    /// </summary>
    private static RulesGoldfishRaceOutcome BuildOutcomeSample(
        List<DeckRunResult> runResults,
        List<int> winningSeats,
        int run,
        int? earliestLethal)
    {
        RulesGoldfishRaceOutcome outcome = new()
        {
            Run = run,
            LethalTurn = earliestLethal,
            IsDraw = !earliestLethal.HasValue,
        };
        if (winningSeats.Count == 1)
        {
            outcome.WinnerLabel = runResults[winningSeats[0]].Label;
        }
        else if (winningSeats.Count > 1)
        {
            foreach (int seat in winningSeats)
            {
                outcome.TiedLabels.Add(runResults[seat].Label);
            }
        }

        return outcome;
    }

    /// <summary>
    /// Runs one deck against the configured goldfish life total.
    /// </summary>
    private static DeckRunResult RunDeck(
        RulesGoldfishRaceDeck deck,
        int seat,
        int run,
        int seed,
        int startingLife,
        int turnLimit,
        bool mulligan,
        bool firstPlayerDraws,
        int traceLimit)
    {
        int runSeed = DeriveSeed(seed, run, seat, attempt: 0);
        GoldfishRaceOpening opening = DrawOpeningHand(deck, runSeed, mulligan);
        DeckRunState state = new(deck, opening, startingLife, traceLimit);
        for (int turn = 1; turn <= turnLimit; turn++)
        {
            bool drawForTurn = turn > 1 || firstPlayerDraws || seat > 0;
            RunTurn(state, turn, drawForTurn);
            if (state.TargetLife <= 0)
            {
                return state.ToResult(turn);
            }
        }

        return state.ToResult(lethalTurn: null);
    }

    /// <summary>
    /// Draws an opening hand using a conservative Commander-style mulligan heuristic.
    /// </summary>
    private static GoldfishRaceOpening DrawOpeningHand(
        RulesGoldfishRaceDeck deck,
        int seed,
        bool mulligan)
    {
        List<RulesGoldfishRaceCard> originalLibrary = Expand(deck.Cards);
        int maximumMulligans = mulligan ? MulliganHeuristics.MaximumMulligans(freeFirstMulligan: true) : 0;
        for (int mulligans = 0; mulligans <= maximumMulligans; mulligans++)
        {
            GoldfishRaceOpening candidate = DrawOpeningAttempt(originalLibrary, seed, mulligans);
            bool mustKeep = !mulligan || mulligans == maximumMulligans;
            if (mustKeep || KeepsOpeningHand(candidate.Hand))
            {
                return candidate;
            }
        }

        return DrawOpeningAttempt(originalLibrary, seed, maximumMulligans);
    }

    /// <summary>
    /// Draws one opening-hand attempt and bottoms cards after mulligans.
    /// </summary>
    private static GoldfishRaceOpening DrawOpeningAttempt(
        List<RulesGoldfishRaceCard> originalLibrary,
        int seed,
        int mulligans)
    {
        List<RulesGoldfishRaceCard> library = originalLibrary.ToList();
        DeterministicSimulationRandom random = new(DeriveSeed(seed, run: 0, seat: 0, mulligans));
        Shuffle(library, random);
        List<RulesGoldfishRaceCard> hand = Draw(library, 7);
        int targetHandSize = MulliganHeuristics.TargetHandSize(mulligans, freeFirstMulligan: true);
        while (hand.Count > targetHandSize)
        {
            int index = random.Next(hand.Count);
            library.Add(hand[index]);
            hand.RemoveAt(index);
        }

        return new GoldfishRaceOpening(hand, library, mulligans);
    }

    /// <summary>
    /// Checks whether a hand has a reasonable land count for this conservative race.
    /// </summary>
    private static bool KeepsOpeningHand(IReadOnlyList<RulesGoldfishRaceCard> hand)
    {
        int lands = hand.Count(card => card.IsLand);
        return lands is >= 2 and <= 5;
    }

    /// <summary>
    /// Runs one turn for one deck.
    /// </summary>
    private static void RunTurn(DeckRunState state, int turn, bool drawForTurn)
    {
        List<string> events = [];
        if (drawForTurn && DrawOne(state.Library) is RulesGoldfishRaceCard drawn)
        {
            state.Hand.Add(drawn);
            events.Add($"drew {drawn.Name}");
        }

        if (PlayLand(state, turn) is string landName)
        {
            events.Add($"played {landName}");
        }

        CastMainPhase(state, turn, events);
        int combatDamage = CombatDamage(state.Battlefield, turn);
        if (combatDamage > 0)
        {
            state.TargetLife -= combatDamage;
            events.Add($"attacked for {combatDamage}");
        }

        state.AddTrace($"T{turn}: {string.Join(", ", events.DefaultIfEmpty("no action"))}; target life {Math.Max(0, state.TargetLife)}.");
    }

    /// <summary>
    /// Plays one land if available.
    /// </summary>
    private static string? PlayLand(DeckRunState state, int turn)
    {
        for (int index = 0; index < state.Hand.Count; index++)
        {
            RulesGoldfishRaceCard card = state.Hand[index];
            if (!card.IsLand)
            {
                continue;
            }

            state.Hand.RemoveAt(index);
            state.Battlefield.Add(BattlefieldPermanent.FromCard(card, turn, availableFromTurn: turn));
            return card.Name;
        }

        return null;
    }

    /// <summary>
    /// Casts available spells using conservative priority ordering.
    /// </summary>
    private static void CastMainPhase(DeckRunState state, int turn, List<string> events)
    {
        int spentMana = 0;
        while (true)
        {
            int availableMana = CountAvailableMana(state.Battlefield, turn) - spentMana;
            SpellChoice? choice = ChooseSpell(state, availableMana);
            if (choice is null)
            {
                return;
            }

            spentMana += Math.Max(0, choice.Card.ManaValue);
            if (choice.FromCommandZone)
            {
                state.CommandZone.RemoveAt(choice.Index);
                events.Add($"cast {choice.Card.Name} from command zone");
            }
            else
            {
                state.Hand.RemoveAt(choice.Index);
                events.Add($"cast {choice.Card.Name}");
            }

            ResolveSpell(state, choice.Card, turn, events);
            if (state.TargetLife <= 0)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Chooses the best currently castable spell.
    /// </summary>
    private static SpellChoice? ChooseSpell(DeckRunState state, int availableMana)
    {
        SpellChoice? best = null;
        AddBestSpell(state.CommandZone, fromCommandZone: true, availableMana, ref best);
        AddBestSpell(state.Hand, fromCommandZone: false, availableMana, ref best);
        return best;
    }

    /// <summary>
    /// Updates the best spell choice from one source zone.
    /// </summary>
    private static void AddBestSpell(
        IReadOnlyList<RulesGoldfishRaceCard> cards,
        bool fromCommandZone,
        int availableMana,
        ref SpellChoice? best)
    {
        for (int index = 0; index < cards.Count; index++)
        {
            RulesGoldfishRaceCard card = cards[index];
            if (card.IsLand || card.ManaValue > availableMana)
            {
                continue;
            }

            SpellChoice candidate = new(card, index, fromCommandZone, SpellPriority(card));
            if (best is null
                || candidate.Priority > best.Priority
                || candidate.Priority == best.Priority && candidate.Card.ManaValue > best.Card.ManaValue)
            {
                best = candidate;
            }
        }
    }

    /// <summary>
    /// Scores castable spells so setup happens before combat pressure.
    /// </summary>
    private static int SpellPriority(RulesGoldfishRaceCard card)
    {
        int score = 0;
        if (card.ManaProduced > 0 || card.RampLands > 0)
        {
            score += 100;
        }

        if (card.DrawCards > 0)
        {
            score += 80;
        }

        if (card.LifeLoss > 0)
        {
            score += 70;
        }

        if (card.CreateTokens > 0)
        {
            score += 60;
        }

        if (card.IsCreature)
        {
            score += 50;
        }

        if (card.IsCombatPayoff || card.TeamPowerBonus > 0 || card.GrantsTeamDoubleStrike)
        {
            score += 45;
        }

        return score;
    }

    /// <summary>
    /// Applies conservative spell effects.
    /// </summary>
    private static void ResolveSpell(
        DeckRunState state,
        RulesGoldfishRaceCard card,
        int turn,
        List<string> events)
    {
        if (card.IsCreature || card.StaysOnBattlefield || card.ManaProduced > 0 || card.IsCombatPayoff)
        {
            state.Battlefield.Add(BattlefieldPermanent.FromCard(card, turn, availableFromTurn: turn + 1));
        }

        if (card.DrawCards > 0)
        {
            List<RulesGoldfishRaceCard> drawn = Draw(state.Library, card.DrawCards);
            state.Hand.AddRange(drawn);
            events.Add($"drew {drawn.Count}");
        }

        if (card.RampLands > 0)
        {
            for (int index = 0; index < card.RampLands; index++)
            {
                state.Battlefield.Add(BattlefieldPermanent.RampLand(turn));
            }

            events.Add($"ramped {card.RampLands}");
        }

        if (card.CreateTokens > 0)
        {
            for (int index = 0; index < card.CreateTokens; index++)
            {
                state.Battlefield.Add(BattlefieldPermanent.Token(card, turn));
            }

            events.Add($"created {card.CreateTokens} token(s)");
        }

        if (card.LifeLoss > 0)
        {
            state.TargetLife -= card.LifeLoss;
            events.Add($"drained {card.LifeLoss}");
        }
    }

    /// <summary>
    /// Counts reusable mana available this turn.
    /// </summary>
    private static int CountAvailableMana(IReadOnlyList<BattlefieldPermanent> battlefield, int turn)
    {
        int mana = 0;
        foreach (BattlefieldPermanent permanent in battlefield)
        {
            if (turn >= permanent.AvailableFromTurn)
            {
                mana += Math.Max(0, permanent.ManaProduced);
            }
        }

        return mana;
    }

    /// <summary>
    /// Calculates unblocked combat damage.
    /// </summary>
    private static int CombatDamage(IReadOnlyList<BattlefieldPermanent> battlefield, int turn)
    {
        int teamPowerBonus = 0;
        bool doubleStrike = false;
        bool teamHaste = false;
        foreach (BattlefieldPermanent permanent in battlefield)
        {
            teamPowerBonus += permanent.TeamPowerBonus;
            doubleStrike |= permanent.GrantsTeamDoubleStrike;
            teamHaste |= permanent.GrantsTeamHaste;
        }

        int damage = 0;
        foreach (BattlefieldPermanent permanent in battlefield)
        {
            if (!permanent.IsCreature || permanent.EnteredTurn >= turn && !teamHaste)
            {
                continue;
            }

            damage += Math.Max(0, permanent.Power + teamPowerBonus);
        }

        return doubleStrike ? damage * 2 : damage;
    }

    /// <summary>
    /// Expands quantity-bearing templates into one entry per card.
    /// </summary>
    private static List<RulesGoldfishRaceCard> Expand(IReadOnlyList<RulesGoldfishRaceCard> cards)
    {
        List<RulesGoldfishRaceCard> expanded = [];
        foreach (RulesGoldfishRaceCard card in cards)
        {
            for (int copy = 0; copy < Math.Max(0, card.Quantity); copy++)
            {
                expanded.Add(card);
            }
        }

        return expanded;
    }

    /// <summary>
    /// Draws up to count cards.
    /// </summary>
    private static List<RulesGoldfishRaceCard> Draw(List<RulesGoldfishRaceCard> library, int count)
    {
        List<RulesGoldfishRaceCard> drawn = [];
        for (int index = 0; index < count; index++)
        {
            RulesGoldfishRaceCard? card = DrawOne(library);
            if (card is null)
            {
                break;
            }

            drawn.Add(card);
        }

        return drawn;
    }

    /// <summary>
    /// Draws one card from the top of the library.
    /// </summary>
    private static RulesGoldfishRaceCard? DrawOne(List<RulesGoldfishRaceCard> library)
    {
        if (library.Count == 0)
        {
            return null;
        }

        RulesGoldfishRaceCard card = library[0];
        library.RemoveAt(0);
        return card;
    }

    /// <summary>
    /// Shuffles a library with the deterministic random source.
    /// </summary>
    private static void Shuffle(List<RulesGoldfishRaceCard> library, DeterministicSimulationRandom random)
    {
        for (int index = library.Count - 1; index > 0; index--)
        {
            int swapIndex = random.Next(index + 1);
            (library[index], library[swapIndex]) = (library[swapIndex], library[index]);
        }
    }

    /// <summary>
    /// Derives stable per-run, per-seat, and per-attempt seeds.
    /// </summary>
    private static int DeriveSeed(int seed, int run, int seat, int attempt)
    {
        unchecked
        {
            uint value = (uint)seed;
            value ^= (uint)(run + 1) * 0x9E3779B9u;
            value ^= (uint)(seat + 1) * 0x85EBCA6Bu;
            value ^= (uint)(attempt + 1) * 0xC2B2AE35u;
            return (int)value;
        }
    }

    /// <summary>
    /// Accumulates race outcomes for one deck.
    /// </summary>
    private sealed class DeckAccumulator
    {
        /// <summary>
        /// Stores observed lethal turns.
        /// </summary>
        private readonly List<int> lethalTurns = [];

        /// <summary>
        /// Stores lethal turn counts.
        /// </summary>
        private readonly Dictionary<int, int> lethalTurnCounts = [];

        /// <summary>
        /// Stores the first representative trace.
        /// </summary>
        private List<string> representativeTrace = [];

        /// <summary>
        /// Creates an accumulator.
        /// </summary>
        public DeckAccumulator(RulesGoldfishRaceDeck deck, int seat)
        {
            Deck = deck;
            Seat = seat;
        }

        /// <summary>
        /// Gets the source deck.
        /// </summary>
        public RulesGoldfishRaceDeck Deck { get; }

        /// <summary>
        /// Gets the one-based seat.
        /// </summary>
        public int Seat { get; }

        /// <summary>
        /// Gets or sets sole wins.
        /// </summary>
        public int Wins { get; set; }

        /// <summary>
        /// Gets or sets same-turn lethal ties.
        /// </summary>
        public int Ties { get; set; }

        /// <summary>
        /// Gets or sets no-lethal draws.
        /// </summary>
        public int Draws { get; set; }

        /// <summary>
        /// Gets or sets losses.
        /// </summary>
        public int Losses { get; set; }

        /// <summary>
        /// Adds one deck run.
        /// </summary>
        public void Add(DeckRunResult run)
        {
            if (representativeTrace.Count == 0 || run.LethalTurn.HasValue && lethalTurns.Count == 0)
            {
                representativeTrace = run.Trace.ToList();
            }

            if (!run.LethalTurn.HasValue)
            {
                return;
            }

            lethalTurns.Add(run.LethalTurn.Value);
            lethalTurnCounts[run.LethalTurn.Value] = lethalTurnCounts.GetValueOrDefault(run.LethalTurn.Value) + 1;
        }

        /// <summary>
        /// Builds the public summary row.
        /// </summary>
        public RulesGoldfishRaceDeckSummary BuildSummary(int simulations)
        {
            lethalTurns.Sort();
            return new RulesGoldfishRaceDeckSummary
            {
                Label = Deck.Label,
                Seat = Seat,
                WorkspaceId = Deck.WorkspaceId,
                Name = Deck.Name,
                Wins = Wins,
                Ties = Ties,
                Draws = Draws,
                Losses = Losses,
                WinRate = simulations > 0 ? Wins / (double)simulations : 0,
                TieRate = simulations > 0 ? Ties / (double)simulations : 0,
                LethalRuns = lethalTurns.Count,
                MedianLethalTurn = Median(lethalTurns),
                LethalTurnCounts = new Dictionary<int, int>(lethalTurnCounts),
                RepresentativeTrace = representativeTrace,
                Warnings = Deck.Warnings.ToList(),
            };
        }

        /// <summary>
        /// Calculates the median for sorted turns.
        /// </summary>
        private static int? Median(IReadOnlyList<int> sorted)
        {
            if (sorted.Count == 0)
            {
                return null;
            }

            return sorted[sorted.Count / 2];
        }
    }

    /// <summary>
    /// Stores one deck's current run state.
    /// </summary>
    private sealed class DeckRunState
    {
        /// <summary>
        /// Creates run state from an opening hand.
        /// </summary>
        public DeckRunState(RulesGoldfishRaceDeck deck, GoldfishRaceOpening opening, int startingLife, int traceLimit)
        {
            Deck = deck;
            Hand = opening.Hand;
            Library = opening.Library;
            CommandZone = Expand(deck.CommandZoneCards);
            TargetLife = startingLife;
            TraceLimit = traceLimit;
            AddTrace($"Opening hand kept after {opening.Mulligans} mulligan(s); command zone cards: {CommandZone.Count}.");
        }

        /// <summary>
        /// Gets the source deck.
        /// </summary>
        public RulesGoldfishRaceDeck Deck { get; }

        /// <summary>
        /// Gets cards in hand.
        /// </summary>
        public List<RulesGoldfishRaceCard> Hand { get; }

        /// <summary>
        /// Gets cards in the library.
        /// </summary>
        public List<RulesGoldfishRaceCard> Library { get; }

        /// <summary>
        /// Gets uncast command-zone cards.
        /// </summary>
        public List<RulesGoldfishRaceCard> CommandZone { get; }

        /// <summary>
        /// Gets battlefield permanents.
        /// </summary>
        public List<BattlefieldPermanent> Battlefield { get; } = [];

        /// <summary>
        /// Gets or sets the remaining target life.
        /// </summary>
        public int TargetLife { get; set; }

        /// <summary>
        /// Gets bounded trace lines.
        /// </summary>
        public List<string> Trace { get; } = [];

        /// <summary>
        /// Gets the maximum trace length.
        /// </summary>
        private int TraceLimit { get; }

        /// <summary>
        /// Adds one trace line if within the configured bound.
        /// </summary>
        public void AddTrace(string line)
        {
            if (Trace.Count < TraceLimit)
            {
                Trace.Add(line);
            }
        }

        /// <summary>
        /// Converts the state into a run result.
        /// </summary>
        public DeckRunResult ToResult(int? lethalTurn)
        {
            return new DeckRunResult(Deck.Label, lethalTurn, Trace);
        }
    }

    /// <summary>
    /// Stores a mulliganed opening hand.
    /// </summary>
    private sealed record GoldfishRaceOpening(
        List<RulesGoldfishRaceCard> Hand,
        List<RulesGoldfishRaceCard> Library,
        int Mulligans);

    /// <summary>
    /// Stores one castable spell choice.
    /// </summary>
    private sealed record SpellChoice(
        RulesGoldfishRaceCard Card,
        int Index,
        bool FromCommandZone,
        int Priority);

    /// <summary>
    /// Stores one deck's run result.
    /// </summary>
    private sealed record DeckRunResult(
        string Label,
        int? LethalTurn,
        IReadOnlyList<string> Trace);

    /// <summary>
    /// Stores one permanent on the battlefield.
    /// </summary>
    private sealed class BattlefieldPermanent
    {
        /// <summary>
        /// Gets or sets the source card name.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Gets or sets the turn this permanent entered.
        /// </summary>
        public int EnteredTurn { get; set; }

        /// <summary>
        /// Gets or sets the first turn this permanent can produce mana.
        /// </summary>
        public int AvailableFromTurn { get; set; }

        /// <summary>
        /// Gets or sets whether the permanent is a creature.
        /// </summary>
        public bool IsCreature { get; set; }

        /// <summary>
        /// Gets or sets the combat power.
        /// </summary>
        public int Power { get; set; }

        /// <summary>
        /// Gets or sets reusable mana produced.
        /// </summary>
        public int ManaProduced { get; set; }

        /// <summary>
        /// Gets or sets team power bonus.
        /// </summary>
        public int TeamPowerBonus { get; set; }

        /// <summary>
        /// Gets or sets whether this permanent grants team double strike.
        /// </summary>
        public bool GrantsTeamDoubleStrike { get; set; }

        /// <summary>
        /// Gets or sets whether this permanent grants team haste.
        /// </summary>
        public bool GrantsTeamHaste { get; set; }

        /// <summary>
        /// Creates a permanent from a card template.
        /// </summary>
        public static BattlefieldPermanent FromCard(
            RulesGoldfishRaceCard card,
            int turn,
            int availableFromTurn)
        {
            return new BattlefieldPermanent
            {
                Name = card.Name,
                EnteredTurn = turn,
                AvailableFromTurn = availableFromTurn,
                IsCreature = card.IsCreature,
                Power = Math.Max(0, card.Power),
                ManaProduced = Math.Max(0, card.ManaProduced),
                TeamPowerBonus = Math.Max(0, card.TeamPowerBonus),
                GrantsTeamDoubleStrike = card.GrantsTeamDoubleStrike,
                GrantsTeamHaste = card.GrantsTeamHaste,
            };
        }

        /// <summary>
        /// Creates a conservative ramp land that starts contributing next turn.
        /// </summary>
        public static BattlefieldPermanent RampLand(int turn)
        {
            return new BattlefieldPermanent
            {
                Name = "Ramp land",
                EnteredTurn = turn,
                AvailableFromTurn = turn + 1,
                ManaProduced = 1,
            };
        }

        /// <summary>
        /// Creates a token from a source card.
        /// </summary>
        public static BattlefieldPermanent Token(RulesGoldfishRaceCard source, int turn)
        {
            return new BattlefieldPermanent
            {
                Name = $"{source.Name} token",
                EnteredTurn = turn,
                AvailableFromTurn = turn + 1,
                IsCreature = true,
                Power = Math.Max(0, source.TokenPower),
            };
        }
    }
}
