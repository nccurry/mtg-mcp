namespace MtgMcp.Core;

/// <summary>
/// Provides heuristic goldfish simulation behavior.
/// </summary>
public sealed partial class DeckWorkspaceService
{
    /// <summary>
    /// Runs a heuristic no-interaction goldfish simulation.
    /// </summary>
    public async Task<GoldfishSimulationResult> SimulateGoldfishAsync(
        string workspaceId,
        int targetTurn,
        int simulations,
        int seed,
        bool mulligan,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        return SimulateGoldfish(workspace, targetTurn, simulations, seed, mulligan);
    }

    /// <summary>
    /// Projects the likely board state by a requested turn.
    /// </summary>
    public async Task<ProjectedTurnState> ProjectBoardStateAsync(
        string workspaceId,
        int turn,
        int simulations,
        int seed,
        CancellationToken cancellationToken)
    {
        GoldfishSimulationResult result = await SimulateGoldfishAsync(
            workspaceId,
            turn,
            simulations,
            seed,
            mulligan: true,
            cancellationToken).ConfigureAwait(false);
        return result.TurnSummaries.LastOrDefault()
            ?? new ProjectedTurnState { Turn = Math.Max(1, turn), LikelyBoard = "No projection could be produced." };
    }

    /// <summary>
    /// Estimates the likely win turn and win routes.
    /// </summary>
    public async Task<WinTurnEstimate> EstimateWinTurnAsync(
        string workspaceId,
        int maxTurn,
        int simulations,
        int seed,
        CancellationToken cancellationToken)
    {
        GoldfishSimulationResult result = await SimulateGoldfishAsync(
            workspaceId,
            maxTurn,
            simulations,
            seed,
            mulligan: true,
            cancellationToken).ConfigureAwait(false);
        return result.WinEstimate;
    }

    /// <summary>
    /// Runs the goldfish simulator for a workspace.
    /// </summary>
    private static GoldfishSimulationResult SimulateGoldfish(
        DeckWorkspace workspace,
        int targetTurn,
        int simulations,
        int seed,
        bool mulligan)
    {
        int safeTurn = Math.Clamp(targetTurn, 1, 20);
        int safeSimulations = Math.Clamp(simulations, 100, 10_000);
        List<GoldfishRun> runs = [];
        for (int index = 0; index < safeSimulations; index++)
        {
            runs.Add(RunGoldfishGame(workspace, safeTurn, seed + index, mulligan));
        }

        GoldfishSimulationResult result = new()
        {
            WorkspaceId = workspace.Id,
            Simulations = safeSimulations,
            TargetTurn = safeTurn,
            Mulligans = runs.Count(run => run.Mulliganed),
            WinEstimate = BuildWinEstimate(workspace, runs, safeTurn)
        };
        for (int turn = 1; turn <= safeTurn; turn++)
        {
            result.TurnSummaries.Add(BuildProjectedTurnState(turn, runs));
        }

        GoldfishRun representative = runs.OrderBy(run => Math.Abs((run.WinTurn ?? safeTurn + 4) - (result.WinEstimate.MedianWinTurn ?? safeTurn + 4))).First();
        result.RepresentativeLines = representative.Line.Take(12).ToList();
        result.Notes.Add("Goldfish projection assumes no opponent interaction and uses role/tag heuristics rather than a full Magic rules engine.");
        result.Notes.Add("Commander is treated as available from the command zone when the deck has a Commander category.");
        return result;
    }

    /// <summary>
    /// Runs one goldfish game.
    /// </summary>
    private static GoldfishRun RunGoldfishGame(DeckWorkspace workspace, int targetTurn, int seed, bool mulligan)
    {
        Random random = new(seed);
        List<DeckCard> deck = ExpandLibrary(workspace);
        Shuffle(deck, random);
        List<DeckCard> hand = deck.Take(7).ToList();
        deck = deck.Skip(7).ToList();
        bool mulliganed = false;
        if (mulligan && !KeepableHand(hand))
        {
            deck = ExpandLibrary(workspace);
            Shuffle(deck, random);
            hand = deck.Take(6).ToList();
            deck = deck.Skip(6).ToList();
            mulliganed = true;
        }

        DeckCard? commander = IncludedCards(workspace).FirstOrDefault(IsCommanderCard);
        bool commanderCast = false;
        List<DeckCard> battlefield = [];
        List<DeckCard> graveyard = [];
        GoldfishRun run = new() { Mulliganed = mulliganed };
        int tokens = 0;
        int winPressure = 0;

        for (int turn = 1; turn <= targetTurn; turn++)
        {
            if (deck.Count > 0)
            {
                hand.Add(deck[0]);
                deck.RemoveAt(0);
            }

            DeckCard? land = hand.FirstOrDefault(card => DeckRoleClassifier.Classify(card).PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase));
            if (land is not null)
            {
                hand.Remove(land);
                battlefield.Add(land);
                run.Line.Add($"T{turn}: played {land.Name}.");
            }

            int availableMana = CountManaSources(battlefield);
            if (!commanderCast && commander is not null && (GetSnapshot(commander).ManaValue ?? 3) <= availableMana)
            {
                commanderCast = true;
                battlefield.Add(commander);
                availableMana -= (int)Math.Ceiling(GetSnapshot(commander).ManaValue ?? 3);
                run.Line.Add($"T{turn}: cast commander {commander.Name}.");
            }

            foreach (DeckCard spell in hand.OrderBy(card => CastPriority(card, turn)).ThenBy(card => GetSnapshot(card).ManaValue ?? 0).ToList())
            {
                if (DeckRoleClassifier.Classify(spell).PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase)
                    || IsCommanderCard(spell))
                {
                    continue;
                }

                int cost = Math.Max(0, (int)Math.Ceiling(GetSnapshot(spell).ManaValue ?? 2));
                if (cost > availableMana)
                {
                    continue;
                }

                availableMana -= cost;
                hand.Remove(spell);
                CardRoleAssignment role = DeckRoleClassifier.Classify(spell);
                if (IsPermanent(spell))
                {
                    battlefield.Add(spell);
                    run.Line.Add($"T{turn}: cast {spell.Name} ({role.PrimaryRole}).");
                }
                else
                {
                    graveyard.Add(spell);
                    run.Line.Add($"T{turn}: used {spell.Name} ({role.PrimaryRole}).");
                }

                if (role.Tags.Contains(DeckTags.Tokens) || role.Tags.Contains(DeckTags.SacrificeFodder))
                {
                    tokens += 2;
                }

                if (role.PrimaryRole.Equals(DeckRoles.Draw, StringComparison.OrdinalIgnoreCase) && deck.Count > 0)
                {
                    hand.Add(deck[0]);
                    deck.RemoveAt(0);
                }

                if (role.PrimaryRole.Equals(DeckRoles.Wincons, StringComparison.OrdinalIgnoreCase) || role.Tags.Contains(DeckTags.Finishers))
                {
                    winPressure += 4;
                }
            }

            int power = EstimateBattlefieldPower(battlefield, tokens);
            int comboPieces = battlefield.Count(card => DeckRoleClassifier.Classify(card).Tags.Any(tag => tag is DeckTags.ComboPiece or DeckTags.ComboEnabler));
            if (!run.WinTurn.HasValue)
            {
                if (comboPieces >= 2)
                {
                    run.WinTurn = Math.Max(turn, 5);
                    run.WinRoute = "combo";
                }
                else if (winPressure >= 8 && power >= 18)
                {
                    run.WinTurn = Math.Max(turn, 6);
                    run.WinRoute = "finisher";
                }
                else if (power >= 32)
                {
                    run.WinTurn = Math.Max(turn, 7);
                    run.WinRoute = "combat";
                }
            }

            run.Turns.Add(new GoldfishTurnSnapshot
            {
                Turn = turn,
                Lands = battlefield.Count(card => DeckRoleClassifier.Classify(card).PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase)),
                ManaSources = CountManaSources(battlefield),
                NonlandPermanents = battlefield.Count(card => !DeckRoleClassifier.Classify(card).PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase)),
                CardsInHand = hand.Count,
                Power = power,
                Tokens = tokens
            });
        }

        return run;
    }

    /// <summary>
    /// Expands the workspace into a shuffled library candidate.
    /// </summary>
    private static List<DeckCard> ExpandLibrary(DeckWorkspace workspace)
    {
        List<DeckCard> cards = [];
        foreach (DeckCard card in IncludedCards(workspace).Where(card => !IsCommanderCard(card)))
        {
            for (int copy = 0; copy < Math.Max(0, card.Quantity); copy++)
            {
                cards.Add(card);
            }
        }

        return cards;
    }

    /// <summary>
    /// Shuffles a list in place.
    /// </summary>
    private static void Shuffle(List<DeckCard> cards, Random random)
    {
        for (int index = cards.Count - 1; index > 0; index--)
        {
            int swapIndex = random.Next(index + 1);
            (cards[index], cards[swapIndex]) = (cards[swapIndex], cards[index]);
        }
    }

    /// <summary>
    /// Checks whether an opening hand is keepable.
    /// </summary>
    private static bool KeepableHand(IReadOnlyList<DeckCard> hand)
    {
        int lands = hand.Count(card => DeckRoleClassifier.Classify(card).PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase));
        return lands is >= 2 and <= 5;
    }

    /// <summary>
    /// Calculates a simple cast priority.
    /// </summary>
    private static int CastPriority(DeckCard card, int turn)
    {
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        if (turn <= 3 && role.PrimaryRole.Equals(DeckRoles.Ramp, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (role.PrimaryRole.Equals(DeckRoles.Draw, StringComparison.OrdinalIgnoreCase) || role.Tags.Contains(DeckTags.Engines))
        {
            return 1;
        }

        if (role.PrimaryRole.Equals(DeckRoles.Wincons, StringComparison.OrdinalIgnoreCase) || role.Tags.Contains(DeckTags.Finishers))
        {
            return 3;
        }

        return 2;
    }

    /// <summary>
    /// Checks whether a card stays on the battlefield.
    /// </summary>
    private static bool IsPermanent(DeckCard card)
    {
        string typeLine = GetSnapshot(card).TypeLine ?? "";
        return ContainsAny(typeLine, "Creature", "Artifact", "Enchantment", "Planeswalker", "Battle", "Land");
    }

    /// <summary>
    /// Counts battlefield mana sources.
    /// </summary>
    private static int CountManaSources(IReadOnlyList<DeckCard> battlefield)
    {
        return battlefield.Count(card =>
        {
            CardRoleAssignment role = DeckRoleClassifier.Classify(card);
            return role.PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase)
                || role.PrimaryRole.Equals(DeckRoles.Ramp, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// Estimates battlefield power.
    /// </summary>
    private static int EstimateBattlefieldPower(IReadOnlyList<DeckCard> battlefield, int tokens)
    {
        int permanentPower = battlefield
            .Where(card => ContainsAny(GetSnapshot(card).TypeLine ?? "", "Creature"))
            .Sum(card => Math.Max(1, (int)Math.Ceiling(GetSnapshot(card).ManaValue ?? 2)));
        int finisherBoost = battlefield.Count(card => DeckRoleClassifier.Classify(card).Tags.Contains(DeckTags.Finishers)) * 4;
        return permanentPower + tokens + finisherBoost;
    }

    /// <summary>
    /// Builds one projected turn summary.
    /// </summary>
    private static ProjectedTurnState BuildProjectedTurnState(int turn, IReadOnlyList<GoldfishRun> runs)
    {
        List<GoldfishTurnSnapshot> snapshots = runs.SelectMany(run => run.Turns.Where(snapshot => snapshot.Turn == turn)).ToList();
        int lands = Median(snapshots.Select(snapshot => snapshot.Lands));
        int manaSources = Median(snapshots.Select(snapshot => snapshot.ManaSources));
        int permanents = Median(snapshots.Select(snapshot => snapshot.NonlandPermanents));
        int hand = Median(snapshots.Select(snapshot => snapshot.CardsInHand));
        int power = Median(snapshots.Select(snapshot => snapshot.Power));
        int tokens = Median(snapshots.Select(snapshot => snapshot.Tokens));
        return new ProjectedTurnState
        {
            Turn = turn,
            MedianLands = lands,
            MedianManaSources = manaSources,
            MedianNonlandPermanents = permanents,
            MedianCardsInHand = hand,
            MedianPower = power,
            MedianTokens = tokens,
            LikelyBoard = $"{lands} lands, {manaSources} mana sources, {permanents} nonland permanents, about {power} pressure, {hand} cards in hand.",
            Confidence = Math.Clamp(0.45 + Math.Min(0.35, runs.Count / 2000.0), 0, 0.85)
        };
    }

    /// <summary>
    /// Builds a win-turn estimate from goldfish runs.
    /// </summary>
    private static WinTurnEstimate BuildWinEstimate(DeckWorkspace workspace, IReadOnlyList<GoldfishRun> runs, int maxTurn)
    {
        List<int> wins = runs.Where(run => run.WinTurn.HasValue).Select(run => run.WinTurn!.Value).Order().ToList();
        WinTurnEstimate estimate = new()
        {
            WorkspaceId = workspace.Id,
            Simulations = runs.Count,
            MedianWinTurn = Percentile(wins, 0.50),
            P25WinTurn = Percentile(wins, 0.25),
            P75WinTurn = Percentile(wins, 0.75)
        };
        for (int turn = 1; turn <= maxTurn; turn++)
        {
            estimate.WinByTurnRates[turn] = runs.Count == 0 ? 0 : runs.Count(run => run.WinTurn <= turn) / (double)runs.Count;
        }

        foreach (IGrouping<string, GoldfishRun> route in runs.Where(run => run.WinRoute is not null).GroupBy(run => run.WinRoute!))
        {
            estimate.Routes.Add(new WinRoute
            {
                Name = route.Key,
                Kind = route.Key,
                EarliestTurn = route.Min(run => run.WinTurn),
                Probability = route.Count() / (double)runs.Count,
                Cards = RouteCards(workspace, route.Key),
                Rationale = $"The simulator found {route.Key} as a likely goldfish win route."
            });
        }

        if (estimate.MedianWinTurn is null)
        {
            estimate.Notes.Add($"No likely win was found by turn {maxTurn} in the goldfish runs.");
        }

        estimate.Notes.Add("Win timing is probabilistic and assumes no interaction.");
        return estimate;
    }

    /// <summary>
    /// Gets representative cards for a win route.
    /// </summary>
    private static List<string> RouteCards(DeckWorkspace workspace, string route)
    {
        return IncludedCards(workspace)
            .Where(card =>
            {
                CardRoleAssignment role = DeckRoleClassifier.Classify(card);
                return route switch
                {
                    "combo" => role.Tags.Any(tag => tag is DeckTags.ComboPiece or DeckTags.ComboEnabler),
                    "finisher" => role.PrimaryRole.Equals(DeckRoles.Wincons, StringComparison.OrdinalIgnoreCase) || role.Tags.Contains(DeckTags.Finishers),
                    "combat" => ContainsAny(GetSnapshot(card).TypeLine ?? "", "Creature"),
                    _ => false
                };
            })
            .Select(card => card.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
    }

    /// <summary>
    /// Calculates an integer median.
    /// </summary>
    private static int Median(IEnumerable<int> values)
    {
        List<int> sorted = values.Order().ToList();
        return sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
    }

    /// <summary>
    /// Calculates a percentile turn.
    /// </summary>
    private static int? Percentile(IReadOnlyList<int> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
        {
            return null;
        }

        int index = Math.Clamp((int)Math.Round((sortedValues.Count - 1) * percentile), 0, sortedValues.Count - 1);
        return sortedValues[index];
    }

    /// <summary>
    /// Stores one goldfish run.
    /// </summary>
    private sealed class GoldfishRun
    {
        bool mulliganed;
        int? winTurn;
        string? winRoute;
        List<GoldfishTurnSnapshot> turns = [];
        List<string> line = [];

        /// <summary>
        /// Gets or sets whether the run mulliganed.
        /// </summary>
        public bool Mulliganed { get => mulliganed; set => mulliganed = value; }

        /// <summary>
        /// Gets or sets the win turn.
        /// </summary>
        public int? WinTurn { get => winTurn; set => winTurn = value; }

        /// <summary>
        /// Gets or sets the win route.
        /// </summary>
        public string? WinRoute { get => winRoute; set => winRoute = value; }

        /// <summary>
        /// Gets or sets turn snapshots.
        /// </summary>
        public List<GoldfishTurnSnapshot> Turns { get => turns; set => turns = value; }

        /// <summary>
        /// Gets or sets the representative line.
        /// </summary>
        public List<string> Line { get => line; set => line = value; }
    }

    /// <summary>
    /// Stores one simulated turn snapshot.
    /// </summary>
    private sealed class GoldfishTurnSnapshot
    {
        int turn;
        int lands;
        int manaSources;
        int nonlandPermanents;
        int cardsInHand;
        int power;
        int tokens;

        /// <summary>
        /// Gets or sets the turn number.
        /// </summary>
        public int Turn { get => turn; set => turn = value; }

        /// <summary>
        /// Gets or sets lands in play.
        /// </summary>
        public int Lands { get => lands; set => lands = value; }

        /// <summary>
        /// Gets or sets mana sources in play.
        /// </summary>
        public int ManaSources { get => manaSources; set => manaSources = value; }

        /// <summary>
        /// Gets or sets nonland permanents in play.
        /// </summary>
        public int NonlandPermanents { get => nonlandPermanents; set => nonlandPermanents = value; }

        /// <summary>
        /// Gets or sets cards in hand.
        /// </summary>
        public int CardsInHand { get => cardsInHand; set => cardsInHand = value; }

        /// <summary>
        /// Gets or sets battlefield power.
        /// </summary>
        public int Power { get => power; set => power = value; }

        /// <summary>
        /// Gets or sets token count.
        /// </summary>
        public int Tokens { get => tokens; set => tokens = value; }
    }
}
