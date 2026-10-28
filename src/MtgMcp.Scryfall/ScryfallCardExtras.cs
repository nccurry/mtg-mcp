namespace MtgMcp.Scryfall;

/// <summary>
/// Carries the facts about one card that come from installed card data rather than from the card object itself.
/// </summary>
/// <param name="Tags">Official oracle tag slugs assigned to the card, sorted by slug.</param>
/// <param name="CheapestPriceUsd">
/// The lowest nonfoil USD price among the card's installed printings, as Scryfall writes it, or null when no
/// installed printing has one.
/// </param>
internal sealed record ScryfallCardExtras(IReadOnlyList<string> Tags, string? CheapestPriceUsd);

/// <summary>
/// Carries installed-data facts for a set of cards, or reports that no complete card data is installed.
/// </summary>
/// <param name="Installed">Whether a complete generation of card data was available to read.</param>
/// <param name="ByOracleId">The facts for each requested Oracle ID; empty when card data is not installed.</param>
internal sealed record ScryfallCardExtrasRead(
    bool Installed,
    IReadOnlyDictionary<Guid, ScryfallCardExtras> ByOracleId)
{
    /// <summary>
    /// Gets the result used when card data is not installed or the requested generation is no longer kept.
    /// </summary>
    internal static ScryfallCardExtrasRead NotInstalled { get; } =
        new(false, new Dictionary<Guid, ScryfallCardExtras>());
}
