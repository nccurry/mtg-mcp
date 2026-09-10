using MtgMcp.OnCurve;

namespace MtgMcp.App.Decks;

/// <summary>
/// Maps closed calculation values to the stable text used by the public result.
/// </summary>
internal static class DeckOnCurveText
{
    /// <summary>
    /// Parses one caller land-rule value without accepting aliases.
    /// </summary>
    internal static bool TryParseLandRule(string? text, out OnCurveLandRule rule)
    {
        rule = text switch
        {
            "one-mana-same-turn" => OnCurveLandRule.OneManaSameTurn,
            "one-mana-next-turn" => OnCurveLandRule.OneManaNextTurn,
            "not-modeled" => OnCurveLandRule.NotModeled,
            _ => default,
        };
        return text is "one-mana-same-turn" or "one-mana-next-turn" or "not-modeled";
    }

    /// <summary>
    /// Returns the exact public spelling of one caller land rule.
    /// </summary>
    internal static string LandRule(OnCurveLandRule rule)
    {
        return rule switch
        {
            OnCurveLandRule.OneManaSameTurn => "one-mana-same-turn",
            OnCurveLandRule.OneManaNextTurn => "one-mana-next-turn",
            OnCurveLandRule.NotModeled => "not-modeled",
            _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, "The land rule is unsupported."),
        };
    }

    /// <summary>
    /// Returns the exact public spelling of one sampled-hand trace event.
    /// </summary>
    internal static string TraceEvent(OnCurveTraceEventKind kind)
    {
        return kind switch
        {
            OnCurveTraceEventKind.Draw => "draw",
            OnCurveTraceEventKind.LandPlayed => "land-played",
            OnCurveTraceEventKind.LandNotModeled => "land-not-modeled",
            OnCurveTraceEventKind.TargetCast => "target-cast",
            OnCurveTraceEventKind.Miss => "miss",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The trace event is unsupported."),
        };
    }

    /// <summary>
    /// Returns the exact public spelling of one completed miss reason.
    /// </summary>
    internal static string MissReason(OnCurveMissReason reason)
    {
        return reason switch
        {
            OnCurveMissReason.TargetNotDrawn => "target-not-drawn",
            OnCurveMissReason.NoModeledLandPlayed => "no-modeled-land-played",
            OnCurveMissReason.SourceNotReady => "source-not-ready",
            OnCurveMissReason.MissingRequiredColor => "missing-required-color",
            OnCurveMissReason.NotEnoughMana => "not-enough-mana",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "The miss reason is unsupported."),
        };
    }
}
