namespace MtgMcp.OnCurve.Tests;

/// <summary>
/// Creates small, stable fact-only inputs for on-curve behavior tests.
/// </summary>
internal static class OnCurveTestData
{
    /// <summary>
    /// Identifies the default target entry.
    /// </summary>
    internal static readonly Guid TargetEntryId = Id(1);

    /// <summary>
    /// Identifies the default target printing.
    /// </summary>
    internal static readonly Guid TargetPrintingId = Id(101);

    /// <summary>
    /// Identifies the first default modeled land entry.
    /// </summary>
    internal static readonly Guid GreenLandEntryId = Id(2);

    /// <summary>
    /// Identifies the first default modeled land printing.
    /// </summary>
    internal static readonly Guid GreenLandPrintingId = Id(102);

    /// <summary>
    /// Identifies the default non-land filler entry.
    /// </summary>
    internal static readonly Guid FillerEntryId = Id(3);

    /// <summary>
    /// Identifies the default non-land filler printing.
    /// </summary>
    internal static readonly Guid FillerPrintingId = Id(103);

    /// <summary>
    /// Creates one stable identifier whose lexical and numeric order agree.
    /// </summary>
    internal static Guid Id(int value)
    {
        return new Guid(value, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    /// <summary>
    /// Creates one direct source fact that did not list produced mana.
    /// </summary>
    internal static OnCurveProducedMana MissingMana()
    {
        return new OnCurveProducedManaMissing();
    }

    /// <summary>
    /// Creates one direct source fact that explicitly set produced mana to null.
    /// </summary>
    internal static OnCurveProducedMana NullMana()
    {
        return new OnCurveProducedManaNull();
    }

    /// <summary>
    /// Creates one direct source fact with the supplied listed mana values.
    /// </summary>
    internal static OnCurveProducedMana Mana(params string[] colors)
    {
        return new OnCurveProducedManaValues(colors);
    }

    /// <summary>
    /// Creates one resolved deck entry for a compact test request.
    /// </summary>
    internal static OnCurveDeckEntry Entry(
        Guid entryId,
        Guid printingId,
        int quantity,
        OnCurveProducedMana producedMana)
    {
        return new OnCurveDeckEntry(entryId, printingId, quantity, producedMana);
    }

    /// <summary>
    /// Creates a complete valid request that callers can adjust for one test case.
    /// </summary>
    internal static OnCurveRequest Request(
        IReadOnlyList<OnCurveDeckEntry>? mainboard = null,
        OnCurveTarget? target = null,
        IReadOnlyList<Guid>? candidateLandEntryIds = null,
        IReadOnlyList<OnCurveLandRuleInput>? landRules = null,
        int turnLimit = 1,
        int sampleCount = 100,
        bool onThePlay = true,
        string seed = "0123456789abcdef")
    {
        IReadOnlyList<OnCurveDeckEntry> resolvedMainboard = mainboard ??
        [
            Entry(TargetEntryId, TargetPrintingId, 1, MissingMana()),
            Entry(GreenLandEntryId, GreenLandPrintingId, 6, Mana("G")),
        ];
        return new OnCurveRequest(
            Id(999),
            1,
            target ?? new OnCurveTarget(TargetEntryId, TargetPrintingId, "{G}"),
            resolvedMainboard,
            candidateLandEntryIds ?? [GreenLandEntryId],
            landRules ?? [new OnCurveLandRuleInput(GreenLandEntryId, OnCurveLandRule.OneManaSameTurn)],
            turnLimit,
            sampleCount,
            onThePlay,
            seed);
    }

    /// <summary>
    /// Creates one physical modeled card for an ordered library test.
    /// </summary>
    internal static OnCurveLibraryCard Card(Guid entryId, params OnCurveManaSymbol[] sourceColors)
    {
        return new OnCurveLibraryCard(entryId, Array.AsReadOnly(sourceColors));
    }

    /// <summary>
    /// Creates a seven-card hand with the supplied leading cards and inert filler.
    /// </summary>
    internal static IReadOnlyList<OnCurveLibraryCard> OpeningHand(params OnCurveLibraryCard[] leadingCards)
    {
        List<OnCurveLibraryCard> cards = [.. leadingCards];
        while (cards.Count < 7)
        {
            cards.Add(Card(FillerEntryId));
        }

        return Array.AsReadOnly(cards.ToArray());
    }

    /// <summary>
    /// Creates one known green target cost.
    /// </summary>
    internal static OnCurveManaCost GreenCost()
    {
        return new OnCurveManaCost(0, 0, 0, 0, 0, 1, 0);
    }
}
