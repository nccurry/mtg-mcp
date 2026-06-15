namespace MtgMcp.Core;

/// <summary>
/// Builds compact card listings from workspace inclusion zones.
/// </summary>
public sealed partial class DeckWorkspaceService
{
    /// <summary>
    /// Lists cards by active, sideboard, maybeboard, excluded, or all zones.
    /// </summary>
    public async Task<DeckCardsByZoneResult> ListCardsByZoneAsync(
        string workspaceId,
        string zone,
        bool collapseDuplicates,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        string normalizedZone = NormalizeZone(zone);
        Dictionary<string, DeckCategory> categoryMap = DeckCategoryInclusion.BuildCategoryMap(workspace);
        List<DeckCardZoneRow> rows = [];
        foreach (DeckCard card in workspace.Cards)
        {
            string primaryCategory = DeckCategoryOrdering.PrimaryCategory(card);
            bool included = DeckCategoryInclusion.IsIncludedInDeck(categoryMap, primaryCategory);
            if (!ZoneMatches(normalizedZone, primaryCategory, included))
            {
                continue;
            }

            DeckCardZoneRow row = CreateZoneRow(card, categoryMap, included);
            if (collapseDuplicates)
            {
                MergeZoneRow(rows, row);
            }
            else
            {
                rows.Add(row);
            }
        }

        rows.Sort(CompareZoneRows);
        int totalQuantity = 0;
        foreach (DeckCardZoneRow row in rows)
        {
            totalQuantity += Math.Max(0, row.Quantity);
            row.Categories.Sort(StringComparer.OrdinalIgnoreCase);
            row.Locations.Sort(CompareZoneLocations);
        }

        return new DeckCardsByZoneResult
        {
            WorkspaceId = workspace.Id,
            Zone = normalizedZone,
            CollapseDuplicates = collapseDuplicates,
            TotalQuantity = totalQuantity,
            RowCount = rows.Count,
            Cards = rows
        };
    }

    /// <summary>
    /// Normalizes the requested zone name.
    /// </summary>
    private static string NormalizeZone(string? zone)
    {
        string normalized = string.IsNullOrWhiteSpace(zone)
            ? DeckCardZones.Active
            : zone.Trim().ToLowerInvariant();
        if (normalized is DeckCardZones.Active
            or DeckCardZones.Sideboard
            or DeckCardZones.Maybeboard
            or DeckCardZones.Excluded
            or DeckCardZones.All)
        {
            return normalized;
        }

        throw new ArgumentException("zone must be active, sideboard, maybeboard, excluded, or all.", nameof(zone));
    }

    /// <summary>
    /// Checks whether one card row belongs in the requested zone.
    /// </summary>
    private static bool ZoneMatches(string zone, string primaryCategory, bool included)
    {
        return zone switch
        {
            DeckCardZones.Active => included,
            DeckCardZones.Sideboard => primaryCategory.Equals(DeckDefaults.Sideboard, StringComparison.OrdinalIgnoreCase),
            DeckCardZones.Maybeboard => primaryCategory.Equals(DeckDefaults.Maybeboard, StringComparison.OrdinalIgnoreCase),
            DeckCardZones.Excluded => !included,
            DeckCardZones.All => true,
            _ => false
        };
    }

    /// <summary>
    /// Creates one uncollapsed zone row from a workspace card.
    /// </summary>
    private static DeckCardZoneRow CreateZoneRow(
        DeckCard card,
        IReadOnlyDictionary<string, DeckCategory> categoryMap,
        bool included)
    {
        string primaryCategory = DeckCategoryOrdering.PrimaryCategory(card);
        DeckCardZoneRow row = new()
        {
            CardName = card.Name,
            Quantity = Math.Max(0, card.Quantity),
            PrimaryCategory = primaryCategory,
            IncludedInDeck = included,
            TypeLine = card.Snapshot?.TypeLine,
            ScryfallUri = card.Snapshot?.ScryfallUri
        };

        foreach (string category in DeckCategoryOrdering.OrderedDistinct(primaryCategory, card.Categories))
        {
            AddDistinctZoneCategory(row.Categories, category);
        }

        row.Locations.Add(new DeckCardZoneLocation
        {
            Category = primaryCategory,
            Primary = true,
            IncludedInDeck = DeckCategoryInclusion.IsIncludedInDeck(categoryMap, primaryCategory),
            Quantity = Math.Max(0, card.Quantity)
        });

        return row;
    }

    /// <summary>
    /// Merges one row into an existing row with the same card name.
    /// </summary>
    private static void MergeZoneRow(List<DeckCardZoneRow> rows, DeckCardZoneRow incoming)
    {
        DeckCardZoneRow? existing = rows.FirstOrDefault(row =>
            row.CardName.Equals(incoming.CardName, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            rows.Add(incoming);
            return;
        }

        existing.Quantity += incoming.Quantity;
        existing.IncludedInDeck = existing.IncludedInDeck || incoming.IncludedInDeck;
        existing.PrimaryCategory = null;
        if (string.IsNullOrWhiteSpace(existing.TypeLine))
        {
            existing.TypeLine = incoming.TypeLine;
        }

        if (string.IsNullOrWhiteSpace(existing.ScryfallUri))
        {
            existing.ScryfallUri = incoming.ScryfallUri;
        }

        foreach (string category in incoming.Categories)
        {
            AddDistinctZoneCategory(existing.Categories, category);
        }

        foreach (DeckCardZoneLocation incomingLocation in incoming.Locations)
        {
            DeckCardZoneLocation? location = existing.Locations.FirstOrDefault(value =>
                value.Primary == incomingLocation.Primary
                && value.Category.Equals(incomingLocation.Category, StringComparison.OrdinalIgnoreCase));
            if (location is null)
            {
                existing.Locations.Add(incomingLocation);
            }
            else
            {
                location.Quantity += incomingLocation.Quantity;
            }
        }
    }

    /// <summary>
    /// Sorts card rows by name.
    /// </summary>
    private static int CompareZoneRows(DeckCardZoneRow left, DeckCardZoneRow right)
    {
        return string.Compare(left.CardName, right.CardName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sorts locations with primary categories first, then by name.
    /// </summary>
    private static int CompareZoneLocations(DeckCardZoneLocation left, DeckCardZoneLocation right)
    {
        int primary = right.Primary.CompareTo(left.Primary);
        return primary != 0
            ? primary
            : string.Compare(left.Category, right.Category, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Adds a zone category once using case-insensitive equality.
    /// </summary>
    private static void AddDistinctZoneCategory(List<string> values, string value)
    {
        if (!values.Any(existing => existing.Equals(value, StringComparison.OrdinalIgnoreCase)))
        {
            values.Add(value);
        }
    }
}
