using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MtgMcp.Simulation;

/// <summary>
/// Creates one stable identifier for the deck, groups, rules, and conditions of a request. The seed, the sample
/// count, and the number of returned samples are left out, so runs of the same model share it.
/// </summary>
internal static class SimulationFingerprint
{
    /// <summary>
    /// Creates a lower-case SHA-256 hex fingerprint of the canonical request text.
    /// </summary>
    internal static string Create(SimulationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        StringBuilder builder = new();
        Append(builder, "simulation-input-v1");
        Append(builder, SimulationEngine.ModelVersion);
        Append(builder, SimulationEngine.RandomVersion);
        Append(builder, request.Context);
        Append(builder, request.Turns);
        AppendDeck(builder, request);
        AppendOpeningHand(builder, request.OpeningHand);
        Append(builder, "rules");
        Append(builder, request.EachTurn.Count);
        foreach (SimulationRule rule in request.EachTurn)
        {
            AppendRule(builder, rule);
        }

        Append(builder, "conditions");
        Append(builder, request.Conditions.Count);
        foreach (SimulationCondition condition in request.Conditions)
        {
            Append(builder, condition.Id);
            AppendCheck(builder, condition.Check);
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    /// <summary>
    /// Appends the deck and the groups.
    /// </summary>
    private static void AppendDeck(StringBuilder builder, SimulationRequest request)
    {
        Append(builder, "deck");
        Append(builder, request.Deck.Count);
        foreach (SimulationDeckEntry entry in request.Deck)
        {
            Append(builder, entry.EntryId.ToString("D"));
            Append(builder, entry.Quantity);
        }

        Append(builder, "groups");
        Append(builder, request.Groups.Count);
        foreach (SimulationGroup group in request.Groups)
        {
            Append(builder, group.Id);
            Append(builder, group.Listed ? "listed" : "unordered");
            AppendIds(builder, group.EntryIds);
            if (group.Produces is null)
            {
                Append(builder, "no-produces");
                continue;
            }

            Append(builder, "produces");
            AppendList(builder, group.Produces.Default);
            Append(builder, group.Produces.ByEntry.Count);
            foreach (SimulationProducesOverride value in group.Produces.ByEntry)
            {
                AppendIds(builder, value.EntryIds);
                AppendList(builder, value.Colors);
            }
        }
    }

    /// <summary>
    /// Appends the opening hand and the mulligan.
    /// </summary>
    private static void AppendOpeningHand(StringBuilder builder, SimulationOpeningHand hand)
    {
        Append(builder, "opening-hand");
        Append(builder, hand.Size);
        Append(builder, hand.OnThePlay ? "play" : "draw");
        if (hand.Mulligan is null)
        {
            Append(builder, "no-mulligan");
            return;
        }

        Append(builder, "mulligan");
        Append(builder, hand.Mulligan.MaxMulligans);
        Append(builder, hand.Mulligan.FreeFirstMulligan ? "free-first" : "no-free-first");
        AppendList(builder, hand.Mulligan.PreferGroups);
        AppendCheck(builder, hand.Mulligan.KeepWhen);
    }

    /// <summary>
    /// Appends one per-turn rule.
    /// </summary>
    private static void AppendRule(StringBuilder builder, SimulationRule rule)
    {
        switch (rule.Value)
        {
            case SimulationPlay play:
                Append(builder, "play");
                Append(builder, play.Group);
                Append(builder, play.Pick.ToString());
                Append(builder, play.MaxPerTurn);
                Append(builder, play.AddsMana);
                Append(builder, play.ManaFrom.ToString());
                AppendOverrides(builder, play.ManaFromByEntry);
                Append(builder, play.TargetCost ?? "-");
                break;
            case SimulationCastIfAble cast:
                Append(builder, "cast-if-able");
                Append(builder, cast.Group);
                Append(builder, cast.Pick.ToString());
                Append(builder, cast.MaxPerTurn);
                Append(builder, cast.Cost);
                Append(builder, cast.AddsMana);
                Append(builder, cast.ManaFrom.ToString());
                AppendOverrides(builder, cast.ManaFromByEntry);
                break;
            case SimulationDrawCards draw:
                Append(builder, "draw-cards");
                Append(builder, draw.Group);
                Append(builder, draw.Pick.ToString());
                Append(builder, draw.MaxPerTurn);
                Append(builder, draw.Cost);
                Append(builder, draw.Cards);
                Append(builder, draw.Effect.ToString());
                break;
            default:
                throw new InvalidOperationException("The rule has no active case.");
        }
    }

    /// <summary>
    /// Appends the manaFrom overrides of a rule.
    /// </summary>
    private static void AppendOverrides(StringBuilder builder, IReadOnlyList<SimulationManaFromOverride> overrides)
    {
        Append(builder, overrides.Count);
        foreach (SimulationManaFromOverride value in overrides)
        {
            AppendIds(builder, value.EntryIds);
            Append(builder, value.ManaFrom.ToString());
        }
    }

    /// <summary>
    /// Appends one check and its children.
    /// </summary>
    private static void AppendCheck(StringBuilder builder, SimulationCheck check)
    {
        switch (check.Value)
        {
            case SimulationCountBetween count:
                Append(builder, "count-between");
                Append(builder, count.Group);
                Append(builder, count.Min);
                Append(builder, count.Max);
                break;
            case SimulationAll all:
                AppendChildren(builder, "all", all.Checks);
                break;
            case SimulationAny any:
                AppendChildren(builder, "any", any.Checks);
                break;
            case SimulationManaAtLeast mana:
                Append(builder, "mana-at-least");
                Append(builder, mana.Amount);
                Append(builder, mana.Turn);
                break;
            case SimulationDrawnAtLeast drawn:
                Append(builder, "drawn-at-least");
                Append(builder, drawn.Group);
                Append(builder, drawn.Count);
                Append(builder, drawn.ByTurn);
                break;
            case SimulationOnBattlefieldAtLeast field:
                Append(builder, "on-battlefield-at-least");
                Append(builder, field.Group);
                Append(builder, field.Count);
                Append(builder, field.ByTurn);
                break;
            case SimulationCanCast cast:
                Append(builder, "can-cast");
                Append(builder, cast.ManaCost);
                Append(builder, cast.Turn?.ToString(CultureInfo.InvariantCulture) ?? "-");
                Append(builder, cast.ByTurn?.ToString(CultureInfo.InvariantCulture) ?? "-");
                Append(builder, cast.InHand?.ToString("D") ?? "-");
                break;
            default:
                throw new InvalidOperationException("The check has no active case.");
        }
    }

    /// <summary>
    /// Appends an <c>all</c> or <c>any</c> check.
    /// </summary>
    private static void AppendChildren(StringBuilder builder, string kind, IReadOnlyList<SimulationCheck> checks)
    {
        Append(builder, kind);
        Append(builder, checks.Count);
        foreach (SimulationCheck child in checks)
        {
            AppendCheck(builder, child);
        }
    }

    /// <summary>
    /// Appends a list of entry IDs.
    /// </summary>
    private static void AppendIds(StringBuilder builder, IReadOnlyList<Guid> ids)
    {
        Append(builder, ids.Count);
        foreach (Guid id in ids)
        {
            Append(builder, id.ToString("D"));
        }
    }

    /// <summary>
    /// Appends a list of text values.
    /// </summary>
    private static void AppendList(StringBuilder builder, IReadOnlyList<string> values)
    {
        Append(builder, values.Count);
        foreach (string value in values)
        {
            Append(builder, value);
        }
    }

    /// <summary>
    /// Appends a number as one canonical field.
    /// </summary>
    private static void Append(StringBuilder builder, int value)
    {
        Append(builder, value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Appends one length-marked field so adjacent text cannot be ambiguous.
    /// </summary>
    private static void Append(StringBuilder builder, string value)
    {
        builder.Append(value.Length).Append(':').Append(value).Append('\n');
    }
}
