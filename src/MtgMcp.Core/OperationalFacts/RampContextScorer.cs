namespace MtgMcp.Core;

/// <summary>
/// Scores supported operational facts against a concrete deck context.
/// </summary>
public static class RampContextScorer
{
    /// <summary>
    /// Evaluates one card's supported operational facts and contextual fit.
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
            DrawKind = facts.Draw?.Kind,
            InteractionKind = facts.Interaction?.Kind,
            DetectedRoles = DetectedRoles(facts),
            Facts = facts,
            Warnings = facts.Warnings.ToList(),
        };

        string? evaluatedRole = SelectEvaluatedRole(facts);
        evaluation.EvaluatedRole = evaluatedRole;
        if (evaluatedRole is null)
        {
            MarkUnsupported(evaluation);
            return evaluation;
        }

        DeckRampContext context = BuildContext(workspace);
        if (evaluatedRole.Equals(CardEvaluationRoles.Ramp, StringComparison.OrdinalIgnoreCase))
        {
            EvaluateRamp(evaluation, facts.Ramp!, context);
        }
        else if (evaluatedRole.Equals(CardEvaluationRoles.Draw, StringComparison.OrdinalIgnoreCase))
        {
            EvaluateDraw(evaluation, facts.Draw!, context);
        }
        else
        {
            EvaluateInteraction(evaluation, facts.Interaction!, context);
        }

        return evaluation;
    }

    /// <summary>
    /// Scores a ramp card's timing, color coverage, and contextual fit.
    /// </summary>
    private static void EvaluateRamp(
        RampContextEvaluation evaluation,
        RampOperationalFacts ramp,
        DeckRampContext context)
    {
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
    }

    /// <summary>
    /// Scores a draw card's card-flow, timing, and contextual fit.
    /// </summary>
    private static void EvaluateDraw(
        RampContextEvaluation evaluation,
        DrawOperationalFacts draw,
        DeckRampContext context)
    {
        evaluation.SubScores["cardsGained"] = ScoreDrawCards(draw);
        evaluation.SubScores["manaEfficiency"] = ScoreDrawManaEfficiency(draw);
        evaluation.SubScores["repeatability"] = draw.Repeatable ? 15 : 6;
        evaluation.SubScores["timing"] = draw.InstantSpeed ? 12 : 7;
        evaluation.SubScores["costOrCondition"] = ScoreDrawCostOrCondition(draw);
        evaluation.SubScores["deckNeed"] = context.DrawCount < 8 ? 10 : context.DrawCount < 11 ? 7 : 4;
        evaluation.Score = Math.Clamp(evaluation.SubScores.Values.Sum(), 0, 100);

        AddDrawIssues(evaluation, draw, context);
        AddDrawStrengths(evaluation, draw, context);
    }

    /// <summary>
    /// Scores an interaction card's efficiency, timing, and coverage.
    /// </summary>
    private static void EvaluateInteraction(
        RampContextEvaluation evaluation,
        InteractionOperationalFacts interaction,
        DeckRampContext context)
    {
        evaluation.SubScores["manaEfficiency"] = ScoreInteractionManaEfficiency(interaction);
        evaluation.SubScores["timing"] = interaction.InstantSpeed ? 16 : 8;
        evaluation.SubScores["coverage"] = ScoreInteractionCoverage(interaction);
        evaluation.SubScores["modality"] = interaction.Modal ? 12 : 6;
        evaluation.SubScores["deckNeed"] = context.InteractionCount < 8 ? 12 : context.InteractionCount < 12 ? 8 : 4;
        evaluation.Score = Math.Clamp(evaluation.SubScores.Values.Sum(), 0, 100);

        AddInteractionIssues(evaluation, interaction, context);
        AddInteractionStrengths(evaluation, interaction, context);
    }

    /// <summary>
    /// Builds deck context used by operational scoring.
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
        int drawCount = included.Count(card => DeckRoleClassifier.Classify(card).PrimaryRole.Equals(DeckRoles.Draw, StringComparison.OrdinalIgnoreCase));
        int interactionCount = included.Count(card =>
            IsInteractionDeckRole(DeckRoleClassifier.Classify(card).PrimaryRole));
        return new DeckRampContext(
            colors,
            commander is null ? null : ManaValue(commander.Snapshot),
            intent,
            drawCount,
            interactionCount);
    }

    /// <summary>
    /// Reads supported fact roles detected for the card.
    /// </summary>
    private static List<string> DetectedRoles(CardOperationalFacts facts)
    {
        List<string> roles = [];
        if (facts.Ramp is not null)
        {
            roles.Add(CardEvaluationRoles.Ramp);
        }

        if (facts.Draw is not null)
        {
            roles.Add(CardEvaluationRoles.Draw);
        }

        if (facts.Interaction is not null)
        {
            roles.Add(CardEvaluationRoles.Interaction);
        }

        return roles;
    }

    /// <summary>
    /// Chooses the supported evaluator role that best matches the card's primary role.
    /// </summary>
    private static string? SelectEvaluatedRole(CardOperationalFacts facts)
    {
        string? primaryRole = SupportedEvaluatorRole(facts.Role);
        if (primaryRole?.Equals(CardEvaluationRoles.Ramp, StringComparison.OrdinalIgnoreCase) == true
            && facts.Ramp is not null)
        {
            return CardEvaluationRoles.Ramp;
        }

        if (primaryRole?.Equals(CardEvaluationRoles.Draw, StringComparison.OrdinalIgnoreCase) == true
            && facts.Draw is not null)
        {
            return CardEvaluationRoles.Draw;
        }

        if (primaryRole?.Equals(CardEvaluationRoles.Interaction, StringComparison.OrdinalIgnoreCase) == true
            && facts.Interaction is not null)
        {
            return CardEvaluationRoles.Interaction;
        }

        if (facts.Ramp is not null)
        {
            return CardEvaluationRoles.Ramp;
        }

        if (facts.Draw is not null)
        {
            return CardEvaluationRoles.Draw;
        }

        return facts.Interaction is null ? null : CardEvaluationRoles.Interaction;
    }

    /// <summary>
    /// Converts deck role labels into supported evaluator role labels.
    /// </summary>
    private static string? SupportedEvaluatorRole(string role)
    {
        if (role.Equals(DeckRoles.Ramp, StringComparison.OrdinalIgnoreCase))
        {
            return CardEvaluationRoles.Ramp;
        }

        if (role.Equals(DeckRoles.Draw, StringComparison.OrdinalIgnoreCase))
        {
            return CardEvaluationRoles.Draw;
        }

        return IsInteractionDeckRole(role) ? CardEvaluationRoles.Interaction : null;
    }

    /// <summary>
    /// Marks cards outside the current evaluator scope explicitly.
    /// </summary>
    private static void MarkUnsupported(RampContextEvaluation evaluation)
    {
        evaluation.Applicable = false;
        evaluation.EvaluationStatus = "unsupported-role";
        evaluation.UnsupportedRole = true;
        evaluation.Score = 0;
        string supportedRoles = string.Join(", ", CardEvaluationRoles.Supported);
        evaluation.TopIssues.Add($"No supported operational facts were detected. Current evaluator roles: {supportedRoles}.");
        evaluation.Warnings.Add($"unsupportedRole: '{evaluation.Role}' is outside the current deterministic evaluator scope.");
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
    /// Scores how much raw or repeatable card flow the draw pattern provides.
    /// </summary>
    private static int ScoreDrawCards(DrawOperationalFacts draw)
    {
        if (draw.SelectionOnly)
        {
            return 8;
        }

        if (draw.Repeatable)
        {
            return 24;
        }

        return draw.ImmediateCards switch
        {
            >= 4 => 24,
            3 => 21,
            2 => 17,
            1 => 11,
            _ => 6,
        };
    }

    /// <summary>
    /// Scores mana efficiency for a draw effect.
    /// </summary>
    private static int ScoreDrawManaEfficiency(DrawOperationalFacts draw)
    {
        int cards = Math.Max(1, draw.ImmediateCards);
        if (draw.Repeatable)
        {
            cards = Math.Max(cards, 2);
        }

        double manaPerCard = draw.CastMana / (double)cards;
        if (manaPerCard <= 1.0)
        {
            return 25;
        }

        if (manaPerCard <= 1.5)
        {
            return 21;
        }

        if (manaPerCard <= 2.0)
        {
            return 16;
        }

        return manaPerCard <= 3.0 ? 10 : 5;
    }

    /// <summary>
    /// Scores discard, exile, and conditional costs for draw effects.
    /// </summary>
    private static int ScoreDrawCostOrCondition(DrawOperationalFacts draw)
    {
        int score = 15;
        if (draw.DiscardsCards)
        {
            score -= 5;
        }

        if (draw.ImpulseDraw)
        {
            score -= 2;
        }

        if (draw.Conditional && !draw.Repeatable)
        {
            score -= 4;
        }

        if (draw.SelectionOnly)
        {
            score -= 3;
        }

        return Math.Clamp(score, 3, 15);
    }

    /// <summary>
    /// Scores mana efficiency for an interaction effect.
    /// </summary>
    private static int ScoreInteractionManaEfficiency(InteractionOperationalFacts interaction)
    {
        return interaction.CastMana switch
        {
            <= 1 => 25,
            2 => 22,
            3 => 17,
            4 => 12,
            _ => 7,
        };
    }

    /// <summary>
    /// Scores the breadth of threats covered by an interaction effect.
    /// </summary>
    private static int ScoreInteractionCoverage(InteractionOperationalFacts interaction)
    {
        if (interaction.BoardWide)
        {
            return 24;
        }

        int score = interaction.Targets.Count switch
        {
            >= 3 => 20,
            2 => 16,
            1 => 12,
            _ => 8,
        };
        if (interaction.StackInteraction)
        {
            score += 4;
        }

        if (interaction.Protection)
        {
            score += 2;
        }

        return Math.Clamp(score, 8, 24);
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
    /// Adds compact draw weaknesses.
    /// </summary>
    private static void AddDrawIssues(
        RampContextEvaluation evaluation,
        DrawOperationalFacts draw,
        DeckRampContext context)
    {
        if (draw.Kind.Equals("unknownShape", StringComparison.OrdinalIgnoreCase))
        {
            evaluation.TopIssues.Add("Draw role was detected, but the operational draw shape is unknown.");
        }

        if (draw.SelectionOnly)
        {
            evaluation.TopIssues.Add("card selection improves quality but does not directly gain cards");
        }

        if (draw.DiscardsCards)
        {
            evaluation.TopIssues.Add("requires or includes discarding cards");
        }

        if (!draw.Repeatable && draw.CastMana >= 4)
        {
            evaluation.TopIssues.Add("one-shot draw costs four or more mana");
        }

        if (draw.Conditional && !draw.Repeatable)
        {
            evaluation.TopIssues.Add("draw value depends on a condition or later trigger");
        }

        if (context.DrawCount >= 11)
        {
            evaluation.TopIssues.Add("deck already has healthy draw density");
        }
    }

    /// <summary>
    /// Adds compact draw strengths.
    /// </summary>
    private static void AddDrawStrengths(
        RampContextEvaluation evaluation,
        DrawOperationalFacts draw,
        DeckRampContext context)
    {
        if (draw.Repeatable)
        {
            evaluation.TopStrengths.Add("repeatable card-flow engine");
        }

        if (draw.ImmediateCards >= 2)
        {
            evaluation.TopStrengths.Add($"gains about {draw.ImmediateCards} cards immediately");
        }

        if (draw.InstantSpeed)
        {
            evaluation.TopStrengths.Add("can be used at instant speed");
        }

        if (draw.CastMana <= 2)
        {
            evaluation.TopStrengths.Add("low mana investment for card flow");
        }

        if (context.DrawCount < 8)
        {
            evaluation.TopStrengths.Add("addresses low draw density in this deck");
        }
    }

    /// <summary>
    /// Adds compact interaction weaknesses.
    /// </summary>
    private static void AddInteractionIssues(
        RampContextEvaluation evaluation,
        InteractionOperationalFacts interaction,
        DeckRampContext context)
    {
        if (interaction.Kind.Equals("unknownShape", StringComparison.OrdinalIgnoreCase))
        {
            evaluation.TopIssues.Add("Interaction role was detected, but the operational answer shape is unknown.");
        }

        if (!interaction.InstantSpeed && !interaction.BoardWide)
        {
            evaluation.TopIssues.Add("sorcery-speed single-target interaction is harder to hold up");
        }

        if (interaction.CastMana >= 4 && !interaction.BoardWide)
        {
            evaluation.TopIssues.Add("interaction costs four or more mana");
        }

        if (interaction.Targets.Count == 0 && !interaction.BoardWide && !interaction.Protection)
        {
            evaluation.TopIssues.Add("answer coverage could not be identified from the cached text");
        }

        if (context.InteractionCount >= 12)
        {
            evaluation.TopIssues.Add("deck already has high interaction density");
        }
    }

    /// <summary>
    /// Adds compact interaction strengths.
    /// </summary>
    private static void AddInteractionStrengths(
        RampContextEvaluation evaluation,
        InteractionOperationalFacts interaction,
        DeckRampContext context)
    {
        if (interaction.InstantSpeed)
        {
            evaluation.TopStrengths.Add("can be held up at instant speed");
        }

        if (interaction.StackInteraction)
        {
            evaluation.TopStrengths.Add("answers spells or abilities on the stack");
        }

        if (interaction.BoardWide)
        {
            evaluation.TopStrengths.Add("answers multiple opposing resources");
        }

        if (interaction.Targets.Count >= 2)
        {
            evaluation.TopStrengths.Add("covers multiple target classes");
        }

        if (context.InteractionCount < 8)
        {
            evaluation.TopStrengths.Add("addresses low interaction density in this deck");
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
    /// Checks whether a deck role belongs to the interaction evaluator bucket.
    /// </summary>
    private static bool IsInteractionDeckRole(string role)
    {
        return role.Equals(DeckRoles.Interaction, StringComparison.OrdinalIgnoreCase)
            || role.Equals(DeckRoles.BoardWipes, StringComparison.OrdinalIgnoreCase)
            || role.Equals(DeckRoles.Protection, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Stores deck context needed by supported operational scorers.
    /// </summary>
    private sealed record DeckRampContext(
        HashSet<string> Colors,
        int? CommanderManaValue,
        DeckIntent? Intent,
        int DrawCount,
        int InteractionCount);
}
