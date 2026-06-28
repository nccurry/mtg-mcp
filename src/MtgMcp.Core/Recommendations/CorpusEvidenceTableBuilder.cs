namespace MtgMcp.Core;

/// <summary>
/// Builds raw corpus evidence rows without applying recommendation scoring.
/// </summary>
internal static class CorpusEvidenceTableBuilder
{
    /// <summary>
    /// Builds deterministic card evidence rows and labels where cards already live in the workspace.
    /// </summary>
    public static List<CardEvidenceTableRow> Build(
        IReadOnlyList<CardCorpusSignal> signals,
        DeckWorkspace workspace,
        int limit,
        IReadOnlyDictionary<string, string?> scryfallUris)
    {
        Dictionary<string, List<CardWorkspaceLocation>> locationsByName = BuildWorkspaceLocations(workspace);
        return signals
            .Where(signal => !string.IsNullOrWhiteSpace(signal.CardName))
            .GroupBy(
                signal => $"{signal.CardName}|{signal.Source}|{signal.SignalType}",
                signal => signal,
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                CardCorpusSignal best = group
                    .OrderByDescending(signal => signal.Score)
                    .ThenBy(signal => signal.Source)
                    .First();
                int? deckCount = group.Any(signal => signal.DeckCount.HasValue)
                    ? group.Sum(signal => signal.DeckCount ?? 0)
                    : null;
                List<string> rationales = group
                    .Select(signal => signal.Rationale)
                    .Where(rationale => !string.IsNullOrWhiteSpace(rationale))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(2)
                    .ToList();
                List<CardWorkspaceLocation> locations = locationsByName.TryGetValue(best.CardName, out List<CardWorkspaceLocation>? values)
                    ? values
                    : [];
                List<string> categories = WorkspaceCategories(workspace, best.CardName, secondaryOnly: false);
                List<string> secondaryCategories = WorkspaceCategories(workspace, best.CardName, secondaryOnly: true);

                return new CardEvidenceTableRow
                {
                    CardName = best.CardName,
                    Source = best.Source,
                    SignalType = best.SignalType,
                    Score = group.Max(signal => signal.Score),
                    EvidenceCount = deckCount ?? group.Count(),
                    DeckCount = deckCount,
                    InclusionRate = group
                        .Where(signal => signal.InclusionRate.HasValue)
                        .Select(signal => signal.InclusionRate)
                        .DefaultIfEmpty(best.InclusionRate)
                        .Max(),
                    AlreadyInDeck = locations.Any(location => location.Primary && location.IncludedInDeck),
                    AlreadyInWorkspace = locations.Count > 0,
                    Categories = categories,
                    SecondaryCategories = secondaryCategories,
                    Locations = locations,
                    Uri = group
                        .OrderByDescending(signal => signal.Score)
                        .Select(signal => signal.Uri)
                        .FirstOrDefault(uri => !string.IsNullOrWhiteSpace(uri)),
                    ScryfallUri = ResolveScryfallUri(
                        best.CardName,
                        group.Select(signal => signal.ScryfallUri).FirstOrDefault(uri => !string.IsNullOrWhiteSpace(uri)),
                        scryfallUris),
                    Rationale = rationales.Count == 0
                        ? $"{best.SignalType} evidence from {best.Source}."
                        : string.Join(" ", rationales)
                };
            })
            .OrderByDescending(row => row.Score)
            .ThenByDescending(row => row.EvidenceCount)
            .ThenBy(row => row.CardName, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, 100))
            .ToList();
    }

    /// <summary>
    /// Chooses the best available Scryfall page for a card row.
    /// </summary>
    public static string? ResolveScryfallUri(
        string cardName,
        string? preferredUri,
        IReadOnlyDictionary<string, string?> scryfallUris)
    {
        if (!string.IsNullOrWhiteSpace(preferredUri))
        {
            return preferredUri;
        }

        return scryfallUris.TryGetValue(cardName, out string? uri) && !string.IsNullOrWhiteSpace(uri)
            ? uri
            : null;
    }

    /// <summary>
    /// Groups workspace card locations by card name for evidence labels.
    /// </summary>
    private static Dictionary<string, List<CardWorkspaceLocation>> BuildWorkspaceLocations(DeckWorkspace workspace)
    {
        Dictionary<string, DeckCategory> categoryMap = DeckCategoryInclusion.BuildCategoryMap(workspace);
        Dictionary<string, List<CardWorkspaceLocation>> locationsByName = new(StringComparer.OrdinalIgnoreCase);
        foreach (DeckCard card in workspace.Cards)
        {
            string primaryCategory = DeckCategoryOrdering.PrimaryCategory(card);
            AddWorkspaceLocation(
                locationsByName,
                categoryMap,
                card.Name,
                primaryCategory,
                primary: true,
                Math.Max(0, card.Quantity));
        }

        return locationsByName;
    }

    /// <summary>
    /// Lists workspace categories attached to matching card rows.
    /// </summary>
    private static List<string> WorkspaceCategories(DeckWorkspace workspace, string cardName, bool secondaryOnly)
    {
        List<string> categories = [];
        foreach (DeckCard card in workspace.Cards)
        {
            if (!card.Name.Equals(cardName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string primaryCategory = DeckCategoryOrdering.PrimaryCategory(card);
            foreach (string category in DeckCategoryOrdering.OrderedDistinct(primaryCategory, card.Categories))
            {
                if (secondaryOnly && category.Equals(primaryCategory, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AddDistinct(categories, category);
            }
        }

        return categories;
    }

    /// <summary>
    /// Adds one case-insensitive value when it has not already been listed.
    /// </summary>
    private static void AddDistinct(List<string> values, string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || values.Any(existing => existing.Equals(value, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        values.Add(value);
    }

    /// <summary>
    /// Adds or merges a workspace location for one evidence card.
    /// </summary>
    private static void AddWorkspaceLocation(
        Dictionary<string, List<CardWorkspaceLocation>> locationsByName,
        IReadOnlyDictionary<string, DeckCategory> categoryMap,
        string cardName,
        string category,
        bool primary,
        int quantity)
    {
        if (!locationsByName.TryGetValue(cardName, out List<CardWorkspaceLocation>? locations))
        {
            locations = [];
            locationsByName[cardName] = locations;
        }

        CardWorkspaceLocation? existing = locations.FirstOrDefault(location =>
            location.Primary == primary
            && location.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Quantity += quantity;
            return;
        }

        locations.Add(new CardWorkspaceLocation
        {
            Category = category,
            Primary = primary,
            IncludedInDeck = DeckCategoryInclusion.IsIncludedInDeck(categoryMap, category),
            Quantity = quantity
        });
    }
}
