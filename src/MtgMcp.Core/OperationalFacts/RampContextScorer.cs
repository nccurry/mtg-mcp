namespace MtgMcp.Core;

/// <summary>
/// Scores ramp operational facts against a concrete deck context.
/// </summary>
public static class RampContextScorer
{
    /// <summary>
    /// Evaluates one card's ramp timing, color coverage, and contextual fit.
    /// </summary>
    public static RampContextEvaluation Evaluate(
        DeckWorkspace workspace,
        DeckCard card,
        CardOperationalFacts facts)
    {
        RampContextEvaluation evaluation = new()
        {
            WorkspaceId = workspace.Id,
            CardName = card.Name,
            Role = facts.Role,
            RampKind = facts.Ramp?.Kind,
            Facts = facts,
            Warnings = facts.Warnings.ToList(),
        };

        if (facts.Ramp is null)
        {
            evaluation.Score = 0;
            evaluation.TopIssues.Add("No ramp operational facts were detected for this card.");
            return evaluation;
        }

        DeckRampContext context = BuildContext(workspace);
        RampOperationalFacts ramp = facts.Ramp;
        evaluation.SubScores["turnAvailableDelta"] = ScoreTurnAvailability(ramp);
        evaluation.SubScores["helpsCommanderOnCurve"] = ScoreCommanderCurve(ramp, context);
        evaluation.SubScores["coloredManaCoverage"] = ScoreColorCoverage(ramp, context);
        evaluation.SubScores["totalManaInvested"] = ScoreTotalMana(ramp);
        evaluation.SubScores["requiresFutureMana"] = ScoreFutureMana(ramp);
        evaluation.SubScores["entersTapped"] = ScoreTapped(ramp);
        evaluation.SubScores["oneShotOrRepeatable"] = ScoreRepeatability(ramp);
        evaluation.SubScores["planSynergy"] = ScorePlanSynergy(ramp, context);
        evaluation.SubScores["lateGameUtility"] = ScoreLateGameUtility(ramp);
        evaluation.Score = Math.Clamp(evaluation.SubScores.Values.Sum(), 0, 100);

        AddIssues(evaluation, ramp, context);
        AddStrengths(evaluation, ramp, context);
        return evaluation;
    }

    /// <summary>
    /// Builds deck context used by ramp scoring.
    /// </summary>
    private static DeckRampContext BuildContext(DeckWorkspace workspace)
    {
        List<DeckCard> included = DeckCategoryInclusion.IncludedCards(workspace).ToList();
        DeckCard? commander = included.FirstOrDefault(card =>
            DeckCategoryOrdering.PrimaryCategory(card).Equals(DeckRoles.Commander, StringComparison.OrdinalIgnoreCase));
        HashSet<string> colors = new(StringComparer.OrdinalIgnoreCase);
        if (commander is not null)
        {
            foreach (string color in commander.Snapshot.ColorIdentity)
            {
                colors.Add(color);
            }
        }

        if (colors.Count == 0)
        {
            foreach (DeckCard card in included)
            {
                foreach (string color in card.Snapshot.ColorIdentity)
                {
                    colors.Add(color);
                }
            }
        }

        DeckIntent? intent = DeckIntentText.Extract(workspace.Description, workspace.Id).Intent;
        return new DeckRampContext(
            colors,
            commander is null ? null : ManaValue(commander.Snapshot),
            intent);
    }

    /// <summary>
    /// Scores how early the card can add usable mana.
    /// </summary>
    private static int ScoreTurnAvailability(RampOperationalFacts ramp)
    {
        return ramp.EarliestManaGainTurn switch
        {
            null => 8,
            <= 1 => 20,
            2 => 18,
            3 => 14,
            4 => 8,
            5 => 4,
            _ => 1,
        };
    }

    /// <summary>
    /// Scores whether the ramp can move the commander ahead of natural land-drop timing.
    /// </summary>
    private static int ScoreCommanderCurve(RampOperationalFacts ramp, DeckRampContext context)
    {
        if (!context.CommanderManaValue.HasValue || ramp.EarliestManaGainTurn is null)
        {
            return 10;
        }

        int commanderTurn = Math.Max(1, context.CommanderManaValue.Value);
        if (ramp.EarliestManaGainTurn.Value <= commanderTurn - 1)
        {
            return 20;
        }

        return ramp.EarliestManaGainTurn.Value == commanderTurn ? 8 : 3;
    }

    /// <summary>
    /// Scores color production or land-search fixing against deck colors.
    /// </summary>
    private static int ScoreColorCoverage(RampOperationalFacts ramp, DeckRampContext context)
    {
        if (ramp.Kind.Contains("LandRamp", StringComparison.OrdinalIgnoreCase)
            || ramp.Destination.Equals("battlefield", StringComparison.OrdinalIgnoreCase))
        {
            return context.Colors.Count == 0 ? 10 : 12;
        }

        if (ramp.ProducedMana.Count == 0)
        {
            return ramp.Kind.Equals("costReducer", StringComparison.OrdinalIgnoreCase) ? 8 : 5;
        }

        if (ramp.ProducedMana.Count >= 5)
        {
            return 15;
        }

        if (context.Colors.Count == 0)
        {
            return ramp.ProducedMana.Contains("C", StringComparer.OrdinalIgnoreCase) ? 10 : 8;
        }

        int covered = context.Colors.Count(color => ramp.ProducedMana.Contains(color, StringComparer.OrdinalIgnoreCase));
        if (covered == context.Colors.Count)
        {
            return 15;
        }

        return covered > 0 ? 10 : 6;
    }

    /// <summary>
    /// Scores total mana invested before the ramp pays off.
    /// </summary>
    private static int ScoreTotalMana(RampOperationalFacts ramp)
    {
        int total = ramp.CastMana + ramp.ActivationMana;
        return total switch
        {
            <= 0 => 15,
            1 => 14,
            2 => 12,
            3 => 7,
            4 => 5,
            _ => 2,
        };
    }

    /// <summary>
    /// Scores whether future mana is required after the card is cast.
    /// </summary>
    private static int ScoreFutureMana(RampOperationalFacts ramp)
    {
        return ramp.ActivationMana switch
        {
            <= 0 => 10,
            1 => 7,
            2 => 3,
            _ => 1,
        };
    }

    /// <summary>
    /// Scores tapped-resource tempo loss.
    /// </summary>
    private static int ScoreTapped(RampOperationalFacts ramp)
    {
        return ramp.EntersTapped switch
        {
            false => 10,
            true => 3,
            null => 6,
        };
    }

    /// <summary>
    /// Scores repeatability compared with one-shot ramp.
    /// </summary>
    private static int ScoreRepeatability(RampOperationalFacts ramp)
    {
        if (ramp.Repeatable)
        {
            return 10;
        }

        return ramp.OneShot ? 4 : 5;
    }

    /// <summary>
    /// Scores coarse plan fit without inferring hidden archetypes.
    /// </summary>
    private static int ScorePlanSynergy(RampOperationalFacts ramp, DeckRampContext context)
    {
        if (context.Intent?.Prefer.Any(card => card.Contains("ramp", StringComparison.OrdinalIgnoreCase)) == true)
        {
            return 5;
        }

        if (ramp.Kind.Equals("activatedLandRamp", StringComparison.OrdinalIgnoreCase)
            && context.Colors.Contains("G"))
        {
            return 2;
        }

        if (ramp.Kind.Equals("manaRock", StringComparison.OrdinalIgnoreCase)
            && context.Colors.Count > 1)
        {
            return 4;
        }

        return 3;
    }

    /// <summary>
    /// Scores whether the card remains useful after early development.
    /// </summary>
    private static int ScoreLateGameUtility(RampOperationalFacts ramp)
    {
        if (ramp.Kind.Equals("costReducer", StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }

        if (ramp.Repeatable)
        {
            return 4;
        }

        return ramp.OneShot ? 1 : 2;
    }

    /// <summary>
    /// Adds compact weaknesses from low sub-scores.
    /// </summary>
    private static void AddIssues(
        RampContextEvaluation evaluation,
        RampOperationalFacts ramp,
        DeckRampContext context)
    {
        if (ramp.Kind.Equals("unknownShape", StringComparison.OrdinalIgnoreCase))
        {
            evaluation.TopIssues.Add("Ramp role was detected, but the operational timing shape is unknown.");
        }

        if (ramp.EarliestManaGainTurn is > 2)
        {
            evaluation.TopIssues.Add($"does not increase usable mana until turn {ramp.EarliestManaGainTurn}");
        }

        if (ramp.ActivationMana > 0)
        {
            evaluation.TopIssues.Add($"requires {ramp.ActivationMana} future activation mana");
        }

        if (ramp.EntersTapped == true)
        {
            evaluation.TopIssues.Add("the gained land or mana source enters tapped");
        }

        if (ramp.OneShot && !ramp.Repeatable)
        {
            evaluation.TopIssues.Add("one-shot ramp with low late-game utility");
        }

        if (context.CommanderManaValue.HasValue
            && ramp.EarliestManaGainTurn.HasValue
            && ramp.EarliestManaGainTurn.Value >= context.CommanderManaValue.Value)
        {
            evaluation.TopIssues.Add("does not help cast the commander ahead of natural land drops");
        }
    }

    /// <summary>
    /// Adds compact strengths from high sub-scores.
    /// </summary>
    private static void AddStrengths(
        RampContextEvaluation evaluation,
        RampOperationalFacts ramp,
        DeckRampContext context)
    {
        if (ramp.Repeatable)
        {
            evaluation.TopStrengths.Add("repeatable mana source");
        }

        if (ramp.CastMana + ramp.ActivationMana <= 2 && ramp.ActivationMana == 0)
        {
            evaluation.TopStrengths.Add("low total mana investment");
        }

        if (ramp.EntersTapped == false)
        {
            evaluation.TopStrengths.Add("does not add tapped-resource delay");
        }

        if (context.CommanderManaValue.HasValue
            && ramp.EarliestManaGainTurn.HasValue
            && ramp.EarliestManaGainTurn.Value <= context.CommanderManaValue.Value - 1)
        {
            evaluation.TopStrengths.Add("can help cast the commander ahead of natural land drops");
        }

        if (ScoreColorCoverage(ramp, context) >= 12)
        {
            evaluation.TopStrengths.Add("supports deck color requirements");
        }
    }

    /// <summary>
    /// Reads a nonnegative integer mana value from cached card data.
    /// </summary>
    private static int ManaValue(CardSnapshot snapshot)
    {
        return Math.Max(0, (int)Math.Ceiling(snapshot.ManaValue ?? 0));
    }

    /// <summary>
    /// Stores context needed for ramp scoring.
    /// </summary>
    private sealed record DeckRampContext(
        HashSet<string> Colors,
        int? CommanderManaValue,
        DeckIntent? Intent);
}
