using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Shapes card-facet lookup output so default responses stay bounded.
/// </summary>
internal static class CardFacetOutputPresenter
{
    /// <summary>
    /// Presents a facet lookup at the requested detail level.
    /// </summary>
    public static object Present(CardFacetSnapshot snapshot, string? detailLevel)
    {
        string normalized = NormalizeDetailLevel(detailLevel);
        if (normalized == DetailLevels.Full)
        {
            return snapshot;
        }

        CardFacetSummaryResult summary = BuildSummary(snapshot);
        if (normalized == DetailLevels.Summary)
        {
            return summary;
        }

        return new CardFacetNormalResult
        {
            Summary = summary,
            Facets = FilterFacets(snapshot.Facets),
        };
    }

    /// <summary>
    /// Creates a structured workspace-card miss result.
    /// </summary>
    public static CardFacetNotFoundResult NotFound(string workspaceId, string cardName)
    {
        return new CardFacetNotFoundResult
        {
            WorkspaceId = workspaceId,
            CardName = cardName,
        };
    }

    /// <summary>
    /// Builds the default key-facet summary.
    /// </summary>
    private static CardFacetSummaryResult BuildSummary(CardFacetSnapshot snapshot)
    {
        return new CardFacetSummaryResult
        {
            WorkspaceId = snapshot.WorkspaceId,
            CardName = snapshot.CardName,
            Quantity = snapshot.Quantity,
            IncludedInDeck = snapshot.IncludedInDeck,
            ScryfallId = snapshot.ScryfallId,
            ScryfallOracleId = snapshot.ScryfallOracleId,
            PrimaryCategory = First(snapshot, CardFacetNames.WorkspacePrimaryCategory),
            Categories = Values(snapshot, CardFacetNames.WorkspaceCategories),
            Role = First(snapshot, "classifier.primary_role"),
            RoleTags = Values(snapshot, "classifier.tags"),
            UserTags = Values(snapshot, CardFacetNames.UserTags),
            TaggerOracleTags = Values(snapshot, CardFacetNames.TaggerOracleTags),
            ManaValue = First(snapshot, "scryfall.mana_value"),
            TypeLine = First(snapshot, "scryfall.type_line"),
            OracleText = First(snapshot, "scryfall.oracle_text"),
            PriceUsd = First(snapshot, "scryfall.prices.usd"),
            CommanderLegality = First(snapshot, "scryfall.legalities.commander"),
            ScryfallUri = First(snapshot, "scryfall.uri"),
        };
    }

    /// <summary>
    /// Filters noisy facets while keeping concrete evidence useful for normal output.
    /// </summary>
    private static Dictionary<string, CardFacet> FilterFacets(
        IReadOnlyDictionary<string, CardFacet> facets)
    {
        Dictionary<string, CardFacet> result = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, CardFacet facet) in facets)
        {
            if (name.StartsWith("scryfall.image_uris", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (name.StartsWith("scryfall.legalities", StringComparison.OrdinalIgnoreCase)
                && !name.Equals("scryfall.legalities.commander", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result[name] = new CardFacet
            {
                Name = facet.Name,
                Source = facet.Source,
                Values = facet.Values.ToList(),
            };
        }

        return result;
    }

    /// <summary>
    /// Reads the first value for one facet.
    /// </summary>
    private static string? First(CardFacetSnapshot snapshot, string facetName)
    {
        return snapshot.Facets.TryGetValue(facetName, out CardFacet? facet)
            ? facet.Values.FirstOrDefault()
            : null;
    }

    /// <summary>
    /// Reads all values for one facet.
    /// </summary>
    private static List<string> Values(CardFacetSnapshot snapshot, string facetName)
    {
        return snapshot.Facets.TryGetValue(facetName, out CardFacet? facet)
            ? facet.Values.ToList()
            : [];
    }

    /// <summary>
    /// Normalizes public detail-level values.
    /// </summary>
    private static string NormalizeDetailLevel(string? detailLevel)
    {
        string normalized = string.IsNullOrWhiteSpace(detailLevel)
            ? DetailLevels.Summary
            : detailLevel.Trim().ToLowerInvariant();
        if (normalized is DetailLevels.Summary or DetailLevels.Normal or DetailLevels.Full)
        {
            return normalized;
        }

        throw new ArgumentException("detailLevel must be summary, normal, or full.", nameof(detailLevel));
    }

    /// <summary>
    /// Public detail-level values for card-facet output.
    /// </summary>
    private static class DetailLevels
    {
        /// <summary>
        /// Key card facets only.
        /// </summary>
        public const string Summary = "summary";

        /// <summary>
        /// Summary plus filtered concrete facets.
        /// </summary>
        public const string Normal = "normal";

        /// <summary>
        /// Full card facet snapshot.
        /// </summary>
        public const string Full = "full";
    }
}
