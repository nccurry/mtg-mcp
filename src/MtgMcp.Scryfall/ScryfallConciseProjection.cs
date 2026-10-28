namespace MtgMcp.Scryfall;

/// <summary>
/// Builds the concise card shape from a mapped card and the facts read from installed card data.
/// </summary>
internal static class ScryfallConciseProjection
{
    /// <summary>
    /// Projects one card to the selected concise fields in their fixed order.
    /// </summary>
    /// <param name="card">The mapped card.</param>
    /// <param name="facts">
    /// The tag slugs and cheapest price read from installed card data, or null when they are not available.
    /// </param>
    /// <param name="selected">The field names to write.</param>
    internal static ScryfallConciseCard Project(
        ScryfallCard card,
        ScryfallInstalledCardFacts? facts,
        IReadOnlySet<string> selected)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(selected);
        List<KeyValuePair<string, object?>> fields = [];
        Dictionary<string, string?>? prices = null;
        foreach (ScryfallConciseField field in ScryfallConciseFieldTable.Definitions)
        {
            if (!selected.Contains(ScryfallConciseFieldTable.NameOf(field)))
            {
                continue;
            }

            switch (field)
            {
                case ScryfallPlainField plain:
                    fields.Add(new KeyValuePair<string, object?>(plain.Name, plain.Read(card)));
                    break;
                case ScryfallInstalledDataField installed:
                    fields.Add(new KeyValuePair<string, object?>(installed.Name, installed.Read(facts)));
                    break;
                case ScryfallPriceField price:
                    // All price fields share one "prices" object, placed where the first selected one falls.
                    if (prices is null)
                    {
                        prices = new Dictionary<string, string?>(StringComparer.Ordinal);
                        fields.Add(new KeyValuePair<string, object?>("prices", prices));
                    }

                    prices[price.OutputKey] = card.Prices.TryGetValue(price.SourceKey, out string? amount)
                        ? amount
                        : null;
                    break;
            }
        }

        return new ScryfallConciseCard(fields);
    }
}
