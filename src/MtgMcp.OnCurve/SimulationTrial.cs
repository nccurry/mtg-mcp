namespace MtgMcp.OnCurve;

/// <summary>
/// Reports how one sampled game ended, apart from the conditions, which are read with
/// <see cref="SimulationTrial.ConditionHeld"/>.
/// </summary>
/// <param name="KeptByRule">True when the keep rule accepted the hand.</param>
/// <param name="KeptAtLimit">True when the last hand was kept because the mulligan limit was reached.</param>
/// <param name="LibraryEmpty">True when a draw was attempted from an empty library.</param>
/// <param name="Record">The full record of the game, when it was asked for.</param>
internal readonly record struct SimulationSampleOutcome(
    bool KeptByRule,
    bool KeptAtLimit,
    bool LibraryEmpty,
    SimulationSampleRecord? Record);

/// <summary>
/// Plays sampled games one after another from one random sequence, applying the fixed engine rules and the rules of
/// the request. One instance owns its buffers and is not shared between threads.
/// </summary>
internal sealed class SimulationTrial
{
    /// <summary>
    /// Holds the compiled request.
    /// </summary>
    private readonly SimulationPlan plan;

    /// <summary>
    /// Supplies every random value, so one seed fixes every game.
    /// </summary>
    private readonly SplitMix64Random random;

    /// <summary>
    /// Stops a long run.
    /// </summary>
    private readonly CancellationToken cancellationToken;

    /// <summary>
    /// Holds the library from the top, with the cards drawn so far still listed before <see cref="next"/>.
    /// </summary>
    private readonly List<int> library = [];

    /// <summary>
    /// Holds the physical cards in hand.
    /// </summary>
    private readonly List<int> hand = [];

    /// <summary>
    /// Marks each physical card that has been seen.
    /// </summary>
    private readonly bool[] seen;

    /// <summary>
    /// Counts the seen cards of each group.
    /// </summary>
    private readonly int[] seenByGroup;

    /// <summary>
    /// Counts the cards of each group in play.
    /// </summary>
    private readonly int[] inPlayByGroup;

    /// <summary>
    /// Holds the cards in play that add mana.
    /// </summary>
    private readonly List<ManaProducer> producers = [];

    /// <summary>
    /// Records, for each leaf check, whether it held on any turn it was evaluated.
    /// </summary>
    private readonly bool[] leafHeld;

    /// <summary>
    /// Holds the units of mana being tested for payment.
    /// </summary>
    private readonly List<OnCurveActiveSource> units = [];

    /// <summary>
    /// Holds the arrays the payment matcher reuses.
    /// </summary>
    private readonly OnCurveMatchScratch scratchArrays = new();

    /// <summary>
    /// Holds the entries already scored for one target-first choice.
    /// </summary>
    private readonly List<int> scored = [];

    /// <summary>
    /// Holds the index of the next library card to draw.
    /// </summary>
    private int next;

    /// <summary>
    /// Holds the generic mana the rules spent this turn.
    /// </summary>
    private int spent;

    /// <summary>
    /// Holds the extra cards drawn at each draw step from each-turn cards in play.
    /// </summary>
    private int extraDraws;

    /// <summary>
    /// Is true once a draw was attempted from an empty library in this game.
    /// </summary>
    private bool libraryEmpty;

    /// <summary>
    /// Collects the cards drawn this turn when the game is recorded.
    /// </summary>
    private List<Guid>? turnDrawn;

    /// <summary>
    /// Collects the cards moved this turn when the game is recorded.
    /// </summary>
    private List<SimulationMove>? turnMoved;

    /// <summary>
    /// Gets a switch for tests: when true the library keeps the deck order and is never shuffled, so a
    /// test can state exactly which cards are drawn.
    /// </summary>
    internal bool KeepDeckOrder { get; init; }

    /// <summary>
    /// Gets the elementary operations this runner has done, counted the way the planner estimates them: each step of
    /// a hand or mana scan, and symbols times units for each payment test. It lets a test hold the estimate to the
    /// real work.
    /// </summary>
    internal long WorkDone { get; private set; }

    /// <summary>
    /// Creates a trial runner for one plan and one random sequence.
    /// </summary>
    internal SimulationTrial(SimulationPlan plan, SplitMix64Random random, CancellationToken cancellationToken)
    {
        this.plan = plan;
        this.random = random;
        this.cancellationToken = cancellationToken;
        seen = new bool[plan.CardEntry.Length];
        seenByGroup = new int[plan.GroupIds.Length];
        inPlayByGroup = new int[plan.GroupIds.Length];
        leafHeld = new bool[plan.Leaves.Length];
    }

    /// <summary>
    /// Reports whether a condition held in the game that just ran.
    /// </summary>
    internal bool ConditionHeld(int condition)
    {
        return Evaluate(plan.ConditionRoots[condition]);
    }

    /// <summary>
    /// Plays one game: the opening hand and mulligan, then each turn in turn.
    /// </summary>
    internal SimulationSampleOutcome Run(int sample, bool record)
    {
        hand.Clear();
        producers.Clear();
        Array.Clear(seen);
        Array.Clear(seenByGroup);
        Array.Clear(inPlayByGroup);
        Array.Clear(leafHeld);
        spent = 0;
        extraDraws = 0;
        libraryEmpty = false;
        List<SimulationHandDraw>? hands = record ? [] : null;
        (int mulligans, bool keptByRule, bool keptAtLimit) = DrawOpeningHand(hands);
        foreach (int card in hand)
        {
            MarkSeen(card);
        }

        List<Guid>? bottomed = record ? [] : null;
        BottomCards(mulligans, bottomed);
        List<Guid>? kept = record ? [.. hand.Select(Entry)] : null;
        List<SimulationTurnRecord>? turns = record ? [] : null;
        for (int turn = 1; turn <= plan.Request.Turns; turn++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SimulationTurnRecord turnRecord = PlayTurn(turn, record);
            turns?.Add(turnRecord);
        }

        SimulationSampleRecord? result = record
            ? new SimulationSampleRecord(sample, hands!, kept!, bottomed!, turns!)
            : null;
        return new SimulationSampleOutcome(keptByRule, keptAtLimit, libraryEmpty, result);
    }

    /// <summary>
    /// Shuffles and draws the opening hand, mulliganing while the keep rule fails and mulligans remain.
    /// </summary>
    private (int Mulligans, bool KeptByRule, bool KeptAtLimit) DrawOpeningHand(List<SimulationHandDraw>? hands)
    {
        SimulationMulliganPlan? mulligan = plan.Mulligan;
        int mulligans = 0;
        while (true)
        {
            library.Clear();
            for (int card = 0; card < plan.CardEntry.Length; card++)
            {
                library.Add(card);
            }

            if (!KeepDeckOrder)
            {
                random.Shuffle(library, cancellationToken);
            }

            next = 0;
            hand.Clear();
            for (int count = 0; count < plan.Request.OpeningHand.Size; count++)
            {
                hand.Add(library[next++]);
            }

            hands?.Add(new SimulationHandDraw(mulligans, [.. hand.Select(Entry)]));
            if (mulligan is null)
            {
                return (0, false, false);
            }

            if (Evaluate(mulligan.KeepWhen))
            {
                return (mulligans, true, false);
            }

            if (mulligans >= mulligan.MaxMulligans)
            {
                return (mulligans, false, true);
            }

            mulligans++;
        }
    }

    /// <summary>
    /// Puts cards on the bottom of the library after a mulligan: from the preferred groups first, then at random.
    /// </summary>
    private void BottomCards(int mulligans, List<Guid>? bottomed)
    {
        SimulationMulliganPlan? mulligan = plan.Mulligan;
        if (mulligan is null)
        {
            return;
        }

        int count = mulligan.FreeFirst ? Math.Max(0, mulligans - 1) : mulligans;
        for (int bottom = 0; bottom < count; bottom++)
        {
            int index = ChooseToBottom(mulligan);
            int card = hand[index];
            hand.RemoveAt(index);
            library.Add(card);
            bottomed?.Add(Entry(card));
        }
    }

    /// <summary>
    /// Picks the hand index to bottom: a random card of the first preferred group that has one in hand, or else a
    /// random card.
    /// </summary>
    private int ChooseToBottom(SimulationMulliganPlan mulligan)
    {
        foreach (int group in mulligan.PreferGroups)
        {
            int inGroup = CountInHand(group);
            if (inGroup > 0)
            {
                return HandIndexOf(group, random.NextIndex(inGroup));
            }
        }

        return random.NextIndex(hand.Count);
    }

    /// <summary>
    /// Plays one turn: draw, apply the rules in order, then check the conditions.
    /// </summary>
    private SimulationTurnRecord PlayTurn(int turn, bool record)
    {
        spent = 0;
        turnDrawn = record ? [] : null;
        turnMoved = record ? [] : null;
        if (turn > 1 || !plan.Request.OpeningHand.OnThePlay)
        {
            DrawCards(1 + extraDraws);
        }

        for (int index = 0; index < plan.Rules.Length; index++)
        {
            ApplyRule(index, turn);
        }

        EvaluateLeaves(turn);
        return new SimulationTurnRecord(
            turn,
            turnDrawn is null ? [] : [.. turnDrawn],
            turnMoved is null ? [] : [.. turnMoved],
            ReadyUnits(turn),
            spent);
    }

    /// <summary>
    /// Applies one rule up to its limit for this turn.
    /// </summary>
    private void ApplyRule(int index, int turn)
    {
        SimulationRulePlan rule = plan.Rules[index];
        for (int fired = 0; fired < rule.MaxPerTurn; fired++)
        {
            if (rule.Kind != SimulationRuleKind.Play && ReadyUnits(turn) - spent < rule.Cost)
            {
                return;
            }

            int handIndex = Choose(rule, turn);
            if (handIndex < 0)
            {
                return;
            }

            int card = hand[handIndex];
            hand.RemoveAt(handIndex);
            int entry = plan.CardEntry[card];
            spent += rule.Cost;
            if (rule.Kind == SimulationRuleKind.DrawCards)
            {
                MoveDrawingCard(rule);
            }
            else
            {
                inPlayByGroup[rule.Group]++;
                if (rule.AddsMana > 0)
                {
                    producers.Add(new ManaProducer(entry, rule.SameTurn[entry] ? turn : turn + 1, rule.AddsMana));
                }
            }

            turnMoved?.Add(new SimulationMove(index, plan.EntryIds[entry]));
            if (rule.Kind == SimulationRuleKind.DrawCards)
            {
                DrawCards(rule.Cards);
            }
        }
    }

    /// <summary>
    /// Keeps a drawing permanent in play, so it draws again at each later draw step; a spell leaves play.
    /// </summary>
    private void MoveDrawingCard(SimulationRulePlan rule)
    {
        if (rule.Effect == SimulationDrawEffect.EachTurn)
        {
            inPlayByGroup[rule.Group]++;
            extraDraws += rule.Cards;
        }
    }

    /// <summary>
    /// Draws cards into the hand; a draw from an empty library does nothing and marks the game.
    /// </summary>
    private void DrawCards(int count)
    {
        for (int draw = 0; draw < count; draw++)
        {
            if (next >= library.Count)
            {
                libraryEmpty = true;
                return;
            }

            int card = library[next++];
            hand.Add(card);
            MarkSeen(card);
            turnDrawn?.Add(Entry(card));
        }
    }

    /// <summary>
    /// Counts a card as seen the first time it reaches a hand.
    /// </summary>
    private void MarkSeen(int card)
    {
        if (seen[card])
        {
            return;
        }

        seen[card] = true;
        int group = plan.EntryGroup[plan.CardEntry[card]];
        if (group >= 0)
        {
            seenByGroup[group]++;
        }
    }

    /// <summary>
    /// Chooses the hand index of the card a rule takes, or -1 when no card of its group is in hand.
    /// </summary>
    private int Choose(SimulationRulePlan rule, int turn)
    {
        int qualifying = CountInHand(rule.Group);
        if (qualifying == 0)
        {
            return -1;
        }

        return rule.Pick switch
        {
            SimulationPick.Random => HandIndexOf(rule.Group, random.NextIndex(qualifying)),
            SimulationPick.ListedOrder => FirstListed(rule.Group),
            SimulationPick.TargetFirst => ChooseTargetFirst(rule, turn),
            _ => throw new InvalidOperationException("The pick has no active case."),
        };
    }

    /// <summary>
    /// Takes the card of the group whose entry is listed first.
    /// </summary>
    private int FirstListed(int group)
    {
        WorkDone += hand.Count;
        int best = -1;
        for (int index = 0; index < hand.Count; index++)
        {
            int entry = plan.CardEntry[hand[index]];
            if (plan.EntryGroup[entry] == group &&
                (best < 0 || plan.EntryRank[entry] < plan.EntryRank[plan.CardEntry[hand[best]]]))
            {
                best = index;
            }
        }

        return best;
    }

    /// <summary>
    /// Takes the card that lets the available mana pay the most colored symbols of the target cost, then the most
    /// symbols in all, then prefers mana usable this turn, then the lowest entry ID.
    /// </summary>
    private int ChooseTargetFirst(SimulationRulePlan rule, int turn)
    {
        OnCurveManaCost cost = rule.TargetCost!;
        scored.Clear();
        FillUnits(turn);
        int baseUnits = units.Count;
        int bestIndex = -1;
        int bestSpecific = 0;
        int bestTotal = 0;
        WorkDone += hand.Count;
        for (int index = 0; index < hand.Count; index++)
        {
            int entry = plan.CardEntry[hand[index]];
            if (plan.EntryGroup[entry] != rule.Group || scored.Contains(entry))
            {
                continue;
            }

            scored.Add(entry);
            units.RemoveRange(baseUnits, units.Count - baseUnits);
            if (rule.SameTurn[entry])
            {
                AddUnits(entry, rule.AddsMana);
            }

            WorkDone += (cost.SpecificSymbolCount + 1L) * (units.Count + 1L);
            OnCurvePaymentAnalysis payment = OnCurveManaPayment.Analyze(cost, units, scratchArrays, spent);
            if (bestIndex < 0 ||
                IsBetter(payment, entry, rule, bestSpecific, bestTotal, plan.CardEntry[hand[bestIndex]]))
            {
                bestIndex = index;
                bestSpecific = payment.SpecificSymbolsCovered;
                bestTotal = payment.TotalSymbolsCovered;
            }
        }

        return bestIndex;
    }

    /// <summary>
    /// Applies the target-first tie-break order to a candidate against the best card so far.
    /// </summary>
    private bool IsBetter(
        OnCurvePaymentAnalysis payment,
        int entry,
        SimulationRulePlan rule,
        int bestSpecific,
        int bestTotal,
        int bestEntry)
    {
        if (payment.SpecificSymbolsCovered != bestSpecific)
        {
            return payment.SpecificSymbolsCovered > bestSpecific;
        }

        if (payment.TotalSymbolsCovered != bestTotal)
        {
            return payment.TotalSymbolsCovered > bestTotal;
        }

        if (rule.SameTurn[entry] != rule.SameTurn[bestEntry])
        {
            return rule.SameTurn[entry];
        }

        return plan.OrdinalRank[entry] < plan.OrdinalRank[bestEntry];
    }

    /// <summary>
    /// Counts the hand cards of a group.
    /// </summary>
    private int CountInHand(int group)
    {
        WorkDone += hand.Count;
        int count = 0;
        foreach (int card in hand)
        {
            if (plan.EntryGroup[plan.CardEntry[card]] == group)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Finds the hand index of the nth card of a group, counting in hand order.
    /// </summary>
    private int HandIndexOf(int group, int nth)
    {
        WorkDone += hand.Count;
        for (int index = 0; index < hand.Count; index++)
        {
            if (plan.EntryGroup[plan.CardEntry[hand[index]]] == group && nth-- == 0)
            {
                return index;
            }
        }

        throw new InvalidOperationException("The group has fewer cards in hand than counted.");
    }

    /// <summary>
    /// Sums the units of mana of every producer usable on a turn.
    /// </summary>
    private int ReadyUnits(int turn)
    {
        WorkDone += producers.Count;
        int total = 0;
        foreach (ManaProducer producer in producers)
        {
            if (producer.ReadyTurn <= turn)
            {
                total += producer.Units;
            }
        }

        return total;
    }

    /// <summary>
    /// Lists every unit of mana usable on a turn in the scratch list.
    /// </summary>
    private void FillUnits(int turn)
    {
        WorkDone += producers.Count + 1;
        units.Clear();
        foreach (ManaProducer producer in producers)
        {
            if (producer.ReadyTurn <= turn)
            {
                AddUnits(producer.Entry, producer.Units);
            }
        }
    }

    /// <summary>
    /// Adds units of mana of one entry to the scratch list.
    /// </summary>
    private void AddUnits(int entry, int count)
    {
        for (int unit = 0; unit < count; unit++)
        {
            units.Add(plan.EntrySource[entry]);
        }
    }

    /// <summary>
    /// Evaluates every leaf check that applies to a turn and has not held yet.
    /// </summary>
    private void EvaluateLeaves(int turn)
    {
        for (int index = 0; index < plan.Leaves.Length; index++)
        {
            SimulationLeafPlan leaf = plan.Leaves[index];
            if (!leafHeld[index] && turn >= leaf.FirstTurn && turn <= leaf.LastTurn)
            {
                leafHeld[index] = EvaluateLeaf(leaf, turn);
            }
        }
    }

    /// <summary>
    /// Evaluates one leaf check at the end of a turn.
    /// </summary>
    private bool EvaluateLeaf(SimulationLeafPlan leaf, int turn)
    {
        switch (leaf.Kind)
        {
            case SimulationLeafKind.ManaAtLeast:
                return ReadyUnits(turn) - spent >= leaf.Count;
            case SimulationLeafKind.DrawnAtLeast:
                return seenByGroup[leaf.Group] >= leaf.Count;
            case SimulationLeafKind.OnBattlefieldAtLeast:
                return inPlayByGroup[leaf.Group] >= leaf.Count;
            case SimulationLeafKind.CanCast:
                return CanCast(leaf, turn);
            default:
                throw new InvalidOperationException("The leaf has no active case.");
        }
    }

    /// <summary>
    /// Tests whether one exact assignment of the turn's mana pays the cost together with the mana already spent.
    /// </summary>
    private bool CanCast(SimulationLeafPlan leaf, int turn)
    {
        if (leaf.InHandEntry >= 0 && !HandHas(leaf.InHandEntry))
        {
            return false;
        }

        FillUnits(turn);
        WorkDone += (leaf.Cost!.SpecificSymbolCount + 1L) * (units.Count + 1L);
        return OnCurveManaPayment.Analyze(leaf.Cost!, units, scratchArrays, spent).IsComplete;
    }

    /// <summary>
    /// Reports whether any copy of an entry is in hand.
    /// </summary>
    private bool HandHas(int entry)
    {
        foreach (int card in hand)
        {
            if (plan.CardEntry[card] == entry)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Evaluates a check tree: a keep rule against the hand, or a condition against the leaf results.
    /// </summary>
    private bool Evaluate(SimulationCheckNode node)
    {
        switch (node.Kind)
        {
            case SimulationCheckKind.CountBetween:
                int count = CountInHand(node.Group);
                return count >= node.Min && count <= node.Max;
            case SimulationCheckKind.Leaf:
                return leafHeld[node.Leaf];
            case SimulationCheckKind.All:
                foreach (SimulationCheckNode child in node.Children)
                {
                    if (!Evaluate(child))
                    {
                        return false;
                    }
                }

                return true;
            case SimulationCheckKind.Any:
                foreach (SimulationCheckNode child in node.Children)
                {
                    if (Evaluate(child))
                    {
                        return true;
                    }
                }

                return false;
            default:
                throw new InvalidOperationException("The check has no active case.");
        }
    }

    /// <summary>
    /// Gets the entry ID of a physical card.
    /// </summary>
    private Guid Entry(int card)
    {
        return plan.EntryIds[plan.CardEntry[card]];
    }

    /// <summary>
    /// Holds one card in play that adds mana.
    /// </summary>
    /// <param name="Entry">The entry index of the card.</param>
    /// <param name="ReadyTurn">The first turn the mana is usable.</param>
    /// <param name="Units">The units of mana added each turn.</param>
    private readonly record struct ManaProducer(int Entry, int ReadyTurn, int Units);
}
