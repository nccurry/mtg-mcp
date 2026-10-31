namespace MtgMcp.App.Statistics;

/// <summary>
/// Holds the one example arguments object published in each statistics tool description and run by tests.
/// </summary>
internal static class StatisticsToolExamples
{
    /// <summary>
    /// Separates a description's prose from its example so tests can read the example back.
    /// </summary>
    internal const string Marker = " Example: ";

    /// <summary>
    /// Stands in for a real local deck ID in the deck summary example.
    /// </summary>
    internal const string PlaceholderDeckId = "0193e97a-ef95-7700-8000-000000000001";

    /// <summary>
    /// A 99-card library of 36 lands, 10 ramp cards, and 53 other spells.
    /// </summary>
    private const string Population =
        """{"kind":"raw","buckets":[""" +
        """{"count":36,"groups":["lands"]},""" +
        """{"count":10,"groups":["ramp"]},""" +
        """{"count":53,"groups":["spells"]}""" +
        """],"declaredGroups":["lands","ramp","spells"]}""";

    /// <summary>
    /// A 99-card library whose lands are split by the color they produce.
    /// </summary>
    private const string ColorPopulation =
        """{"kind":"raw","buckets":[""" +
        """{"count":20,"groups":["forest"]},""" +
        """{"count":16,"groups":["swamp"]},""" +
        """{"count":63,"groups":["spells"]}""" +
        """],"declaredGroups":["forest","swamp","spells"]}""";

    /// <summary>
    /// Asks for at least three lands in a seven-card opening hand.
    /// </summary>
    internal const string Hypergeometric =
        """{"request":{"population":""" + Population +
        ""","successGroup":"lands","drawCount":7,"event":{"kind":"at-least","count":3}}}""";

    /// <summary>
    /// Asks for two to five lands and at least one ramp card in seven cards.
    /// </summary>
    internal const string Multivariate =
        """{"request":{"population":""" + Population +
        ""","drawCount":7,"conditions":[""" +
        """{"group":"lands","minimum":2,"maximum":5},""" +
        """{"group":"ramp","minimum":1,"maximum":null}""" +
        """]}}""";

    /// <summary>
    /// Tracks at least three lands over turns one to three on the play.
    /// </summary>
    internal const string TurnTable =
        """{"request":{"population":""" + Population +
        ""","successGroup":"lands","openingHandSize":7,"drawsByTurn":[""" +
        """{"turn":1,"draws":0},""" +
        """{"turn":2,"draws":1},""" +
        """{"turn":3,"draws":1}""" +
        """],"event":{"kind":"at-least","count":3}}}""";

    /// <summary>
    /// Asks whether nine cards hold sources that pay one black, one green, and one generic mana.
    /// </summary>
    internal const string ManaAvailability =
        """{"request":{"population":""" + ColorPopulation +
        ""","drawCount":9,"sources":[""" +
        """{"group":"forest","capabilities":["G"]},""" +
        """{"group":"swamp","capabilities":["B"]}""" +
        """],"requirement":{"black":1,"green":1,"generic":1},"maximumUsableSources":3}}""";

    /// <summary>
    /// Asks whether ten cards hold one ramp card and three lands at once.
    /// </summary>
    internal const string PackageAssembly =
        """{"request":{"population":""" + Population +
        ""","drawCount":10,"requirements":[""" +
        """{"name":"ramp","count":1,"eligibleGroups":["ramp"]},""" +
        """{"name":"lands","count":3,"eligibleGroups":["lands"]}""" +
        """]}}""";

    /// <summary>
    /// Keeps two to five lands, with a free first mulligan and a forced keep after it.
    /// </summary>
    internal const string Mulligan =
        """{"request":{"population":""" + Population +
        ""","attempts":[""" +
        """{"drawCount":7,"bottomCount":0,"forced":false},""" +
        """{"drawCount":7,"bottomCount":0,"forced":false},""" +
        """{"drawCount":7,"bottomCount":1,"forced":true}""" +
        """],"keepConditions":[""" +
        """{"group":"lands","minimum":2,"maximum":5}""" +
        """],"bottomPriority":["spells"]}}""";

    /// <summary>
    /// Finds the fewest copies that give at least one in seven cards nine times out of ten.
    /// </summary>
    internal const string MinimumCount =
        """{"request":{"event":""" +
        """{"kind":"hypergeometric-at-least","populationSize":99,"drawCount":7,"requiredSuccesses":1}""" +
        ""","targetNumerator":"9","targetDenominator":"10","minimumCount":0,"maximumCount":99}}""";

    /// <summary>
    /// Summarizes the main zone of one stored deck revision.
    /// </summary>
    internal const string DeckSummary =
        $$"""{"request":{"deckId":"{{PlaceholderDeckId}}","expectedRevision":1,"populationSelectors":[""" +
        """{"kind":"zone-names","zoneNames":["main"]}""" +
        """],"numericSeries":[],"percentiles":[]}}""";
}
