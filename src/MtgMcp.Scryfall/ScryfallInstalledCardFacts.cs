namespace MtgMcp.Scryfall;

/// <summary>
/// Carries the facts about one card that come from installed card data rather than from the card object itself.
/// </summary>
/// <param name="Tags">Official oracle tag slugs assigned to the card, sorted by slug.</param>
/// <param name="CheapestPriceUsd">
/// The lowest USD price among the card's installed English printings, as Scryfall writes it, or null when no
/// installed English printing has one.
/// </param>
internal sealed record ScryfallInstalledCardFacts(IReadOnlyList<string> Tags, string? CheapestPriceUsd);

/// <summary>
/// Carries installed-data facts for a set of cards, or says why there are none.
/// </summary>
/// <param name="Installed">Whether a complete generation of card data was read.</param>
/// <param name="ByOracleId">The facts for each requested Oracle ID.</param>
internal sealed record ScryfallInstalledCardFactsRead(
    bool Installed,
    IReadOnlyDictionary<Guid, ScryfallInstalledCardFacts> ByOracleId)
{
    /// <summary>
    /// Gets the result used when card data is not installed or the requested generation is no longer kept.
    /// </summary>
    internal static ScryfallInstalledCardFactsRead NotInstalled { get; } = new(false, Empty());

    /// <summary>
    /// Gets the result used when no chosen field needs installed card data, so nothing was read.
    /// </summary>
    internal static ScryfallInstalledCardFactsRead NotRequested { get; } = new(true, Empty());

    /// <summary>
    /// Creates an empty fact dictionary.
    /// </summary>
    private static Dictionary<Guid, ScryfallInstalledCardFacts> Empty()
    {
        return [];
    }
}
