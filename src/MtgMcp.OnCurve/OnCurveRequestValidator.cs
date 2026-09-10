using MtgMcp.Core.Results;

namespace MtgMcp.OnCurve;

/// <summary>
/// Checks that a resolved on-curve request has complete bounded entries and land rules.
/// </summary>
internal static class OnCurveRequestValidator
{
    /// <summary>
    /// Defines the smallest mainboard size that can produce a seven-card opening hand.
    /// </summary>
    internal const int MinimumMainboardCopies = 7;

    /// <summary>
    /// Defines the largest supported mainboard size for one run.
    /// </summary>
    internal const int MaximumMainboardCopies = 500;

    /// <summary>
    /// Defines the largest mainboard entry count supported by one exact Scryfall lookup.
    /// </summary>
    internal const int MaximumMainboardEntries = 150;

    /// <summary>
    /// Defines the first supported modeled turn.
    /// </summary>
    internal const int MinimumTurnLimit = 1;

    /// <summary>
    /// Defines the last supported modeled turn.
    /// </summary>
    internal const int MaximumTurnLimit = 12;

    /// <summary>
    /// Defines the smallest supported number of sampled hands.
    /// </summary>
    internal const int MinimumSampleCount = 100;

    /// <summary>
    /// Defines the largest supported number of sampled hands.
    /// </summary>
    internal const int MaximumSampleCount = 100_000;

    /// <summary>
    /// Returns the request when its resolved entries, source values, rules, and bounds are complete.
    /// </summary>
    internal static OperationResult<OnCurveRequest> Validate(OnCurveRequest? request)
    {
        if (request is null || request.DeckId == Guid.Empty || request.DeckRevision <= 0)
        {
            return Invalid("request must name a deck with a positive revision.");
        }

        if (request.Target is null ||
            request.Target.EntryId == Guid.Empty ||
            request.Target.PrintingId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Target.ManaCost))
        {
            return Invalid("request.target must name an entry, a printing, and a mana cost.");
        }

        if (request.Mainboard is null || request.Mainboard.Count is < 1 or > MaximumMainboardEntries)
        {
            return Invalid("request.mainboard must contain 1 through 150 entries.");
        }

        if (request.CandidateLandEntryIds is null || request.LandRules is null)
        {
            return Invalid("request.candidateLandEntryIds and request.landRules are required.");
        }

        if (request.TurnLimit is < MinimumTurnLimit or > MaximumTurnLimit ||
            request.SampleCount is < MinimumSampleCount or > MaximumSampleCount)
        {
            return Invalid("request turnLimit or sampleCount is outside the supported bounds.");
        }

        if (!OnCurveSeed.TryParse(request.Seed, out _))
        {
            return Invalid("request seed must contain exactly 16 hexadecimal characters.");
        }

        Dictionary<Guid, OnCurveDeckEntry> mainboardEntries = [];
        int copyCount = 0;
        foreach (OnCurveDeckEntry? entry in request.Mainboard)
        {
            if (entry is null ||
                entry.EntryId == Guid.Empty ||
                entry.PrintingId == Guid.Empty ||
                entry.Quantity <= 0 ||
                entry.ProducedMana.Value is null ||
                !mainboardEntries.TryAdd(entry.EntryId, entry))
            {
                return Invalid("request.mainboard entries must have unique entry IDs, nonempty printing IDs, source facts, and positive quantities.");
            }

            if (entry.Quantity > MaximumMainboardCopies - copyCount)
            {
                return Invalid("request.mainboard has more than 500 card copies.");
            }

            copyCount += entry.Quantity;
        }

        if (copyCount < MinimumMainboardCopies)
        {
            return Invalid("request.mainboard must contain at least seven card copies.");
        }

        if (!mainboardEntries.TryGetValue(request.Target.EntryId, out OnCurveDeckEntry? targetEntry) ||
            targetEntry.PrintingId != request.Target.PrintingId)
        {
            return Invalid("request.target must match one mainboard entry and its printing ID.");
        }

        HashSet<Guid> candidateLandIds = [];
        foreach (Guid landEntryId in request.CandidateLandEntryIds)
        {
            if (landEntryId == Guid.Empty ||
                !candidateLandIds.Add(landEntryId) ||
                !mainboardEntries.TryGetValue(landEntryId, out OnCurveDeckEntry? entry) ||
                entry.ProducedMana.Value is not OnCurveProducedManaValues values ||
                values.Colors is null ||
                values.Colors.Count == 0)
            {
                return Invalid("request.candidateLandEntryIds must name unique mainboard entries with listed mana colors.");
            }

            foreach (string? color in values.Colors)
            {
                if (string.IsNullOrWhiteSpace(color))
                {
                    return Invalid("request candidate land source facts cannot contain blank mana colors.");
                }
            }
        }

        HashSet<Guid> ruledLandIds = [];
        foreach (OnCurveLandRuleInput? rule in request.LandRules)
        {
            if (rule is null ||
                rule.EntryId == Guid.Empty ||
                !Enum.IsDefined(rule.Rule) ||
                !candidateLandIds.Contains(rule.EntryId) ||
                !ruledLandIds.Add(rule.EntryId))
            {
                return Invalid("request.landRules must name each candidate land once with a supported rule.");
            }
        }

        if (!candidateLandIds.SetEquals(ruledLandIds))
        {
            return Invalid("request.landRules must include every candidate land exactly once.");
        }

        return new OperationSuccess<OnCurveRequest>(request);
    }

    /// <summary>
    /// Creates one stable invalid-input result for an incomplete resolved request.
    /// </summary>
    private static OperationInvalidInput Invalid(string message)
    {
        return new OperationInvalidInput("invalid-on-curve-request", message);
    }
}
