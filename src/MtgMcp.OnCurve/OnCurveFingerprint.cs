using System.Security.Cryptography;
using System.Text;

namespace MtgMcp.OnCurve;

/// <summary>
/// Creates one stable identifier for the full modeled deck, direct facts, and declared rules.
/// </summary>
internal static class OnCurveFingerprint
{
    /// <summary>
    /// Creates a SHA-256 fingerprint that excludes only per-run seed and sample count.
    /// </summary>
    internal static string Create(OnCurveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        StringBuilder builder = new();
        Append(builder, "on-curve-input-v1");
        Append(builder, request.DeckId.ToString("D"));
        Append(builder, request.DeckRevision.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(builder, request.Target!.EntryId.ToString("D"));
        Append(builder, request.Target.PrintingId.ToString("D"));
        Append(builder, request.Target.ManaCost!);
        Append(builder, request.TurnLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(builder, request.OnThePlay ? "play" : "draw");
        Append(builder, OnCurveCalculator.ModelVersion);
        Append(builder, OnCurveCalculator.PolicyId);
        Append(builder, OnCurveCalculator.RandomVersion);

        List<OnCurveDeckEntry> entries = request.Mainboard!.ToList();
        entries.Sort(static (left, right) => CompareIds(left.EntryId, right.EntryId));
        Append(builder, "mainboard");
        Append(builder, entries.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (OnCurveDeckEntry entry in entries)
        {
            Append(builder, entry.EntryId.ToString("D"));
            Append(builder, entry.PrintingId.ToString("D"));
            Append(builder, entry.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AppendProducedMana(builder, entry.ProducedMana);
        }

        List<Guid> candidateLandEntryIds = request.CandidateLandEntryIds!.ToList();
        candidateLandEntryIds.Sort(CompareIds);
        Append(builder, "candidate-land-entry-ids");
        Append(builder, candidateLandEntryIds.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (Guid candidateLandEntryId in candidateLandEntryIds)
        {
            Append(builder, candidateLandEntryId.ToString("D"));
        }

        List<OnCurveLandRuleInput> rules = request.LandRules!.ToList();
        rules.Sort(static (left, right) => CompareIds(left.EntryId, right.EntryId));
        Append(builder, "land-rules");
        Append(builder, rules.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (OnCurveLandRuleInput rule in rules)
        {
            Append(builder, rule.EntryId.ToString("D"));
            Append(builder, rule.Rule.ToString());
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    /// <summary>
    /// Appends one length-marked canonical field so adjacent text cannot be ambiguous.
    /// </summary>
    private static void Append(StringBuilder builder, string value)
    {
        builder.Append(value.Length).Append(':').Append(value).Append('\n');
    }

    /// <summary>
    /// Appends one direct source-field state without reclassifying it.
    /// </summary>
    private static void AppendProducedMana(StringBuilder builder, OnCurveProducedMana producedMana)
    {
        switch (producedMana.Value)
        {
            case OnCurveProducedManaMissing:
                Append(builder, "missing");
                return;
            case OnCurveProducedManaNull:
                Append(builder, "null");
                return;
            case OnCurveProducedManaValues values:
                Append(builder, "values");
                Append(builder, values.Colors.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                foreach (string color in values.Colors)
                {
                    Append(builder, color);
                }

                return;
            default:
                throw new ArgumentException("The produced-mana source fact has no active case.", nameof(producedMana));
        }
    }

    /// <summary>
    /// Compares entry identities in their public stable string form.
    /// </summary>
    private static int CompareIds(Guid left, Guid right)
    {
        return string.CompareOrdinal(left.ToString("D"), right.ToString("D"));
    }
}
