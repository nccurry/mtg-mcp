using MtgMcp.Core.Decks;

namespace MtgMcp.Decks;

/// <summary>
/// Evaluates only local relational and documented Commander fixture structure.
/// </summary>
internal static class DeckValidator
{
    /// <summary>
    /// Produces deterministic local issues without legality, provider, role, or quality inference.
    /// </summary>
    internal static DeckValidationReport Validate(DeckDocument deck)
    {
        ArgumentNullException.ThrowIfNull(deck);
        List<DeckValidationIssue> issues = [];
        HashSet<Guid> entryIds = [];
        foreach (DeckEntry entry in deck.Entries)
        {
            if (!entryIds.Add(entry.EntryId))
            {
                issues.Add(new DeckValidationIssue(
                    "duplicate-entry-id",
                    "An entry ID appears more than once.",
                    entry.EntryId));
            }

            if (entry.Quantity <= 0)
            {
                issues.Add(new DeckValidationIssue(
                    "invalid-entry-quantity",
                    "An entry quantity is not positive.",
                    entry.EntryId));
            }

            if (string.IsNullOrWhiteSpace(entry.Zone))
            {
                issues.Add(new DeckValidationIssue(
                    "invalid-entry-zone",
                    "An entry zone is blank.",
                    entry.EntryId));
            }
        }

        HashSet<Guid> categoryIds = deck.Categories.Select(value => value.CategoryId).ToHashSet();
        HashSet<Guid> primaryEntries = [];
        foreach (DeckCategoryAssignment assignment in deck.CategoryAssignments)
        {
            if (!entryIds.Contains(assignment.EntryId) || !categoryIds.Contains(assignment.CategoryId))
            {
                issues.Add(new DeckValidationIssue(
                    "invalid-category-reference",
                    "A category assignment references a missing local row.",
                    assignment.EntryId,
                    assignment.CategoryId));
            }

            if (assignment.IsPrimary && !primaryEntries.Add(assignment.EntryId))
            {
                issues.Add(new DeckValidationIssue(
                    "multiple-primary-categories",
                    "An entry has more than one primary category.",
                    assignment.EntryId,
                    assignment.CategoryId));
            }
        }

        if (deck.Format.Equals("commander", StringComparison.OrdinalIgnoreCase))
        {
            int commanderQuantity = deck.Entries
                .Where(value => value.Zone.Equals("commander", StringComparison.OrdinalIgnoreCase))
                .Sum(value => value.Quantity);
            if (commanderQuantity == 0)
            {
                issues.Add(new DeckValidationIssue(
                    "commander-zone-empty",
                    "A Commander fixture has no entry in the commander zone."));
            }
        }

        issues.Sort(static (left, right) =>
        {
            int reason = string.Compare(left.ReasonCode, right.ReasonCode, StringComparison.Ordinal);
            if (reason != 0)
            {
                return reason;
            }

            int entry = Nullable.Compare(left.EntryId, right.EntryId);
            return entry != 0 ? entry : Nullable.Compare(left.CategoryId, right.CategoryId);
        });
        return new DeckValidationReport(
            deck.DeckId,
            deck.Revision,
            issues.Count == 0,
            issues.ToArray());
    }
}
