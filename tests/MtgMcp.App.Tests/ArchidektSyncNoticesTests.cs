using System.Text.Json;
using System.Text.RegularExpressions;
using MtgMcp.App.Archidekt;
using MtgMcp.Archidekt;

namespace MtgMcp.App.Tests;

/// <summary>
/// Exercises the text a push or pull gives when an entry's zone disagrees with its Archidekt categories.
/// </summary>
public sealed partial class ArchidektSyncNoticesTests
{
    /// <summary>
    /// Verifies entries with different zone pairs get one group each, a group whose match change would clear the
    /// contradiction offers both changes and a group whose match change would not offers only the leave change, the
    /// text says the categories put the entry in a zone, and it ends by saying where to send a change.
    /// </summary>
    [Fact]
    public void DescribeZoneContradictions_WithMoreThanOneZonePair_WritesOneGroupEach()
    {
        ArchidektZoneContradiction moved = Contradiction("Rock", "sideboard", "main", ArchidektMatchFix.Clears);
        ArchidektZoneContradiction stuck = Contradiction("Bolt", "main", "maybeboard", ArchidektMatchFix.DoesNotClear);

        string text = ArchidektSyncNotices.DescribeZoneContradictions([moved, stuck]);

        List<JsonElement> changes = ChangesIn(text);
        Assert.Equal(
            [
                (moved.EntryId, "main", "leave"),
                (moved.EntryId, "sideboard", "match"),
                (stuck.EntryId, "maybeboard", "leave"),
            ],
            changes.Select(value => (
                Guid.Parse(value.GetProperty("entryIds")[0].GetString()!),
                Zone(value),
                Mode(value))));
        Assert.Contains("'Rock' (zone 'sideboard', but its Archidekt categories put it in 'main')", text);
        Assert.Contains("'Bolt' (zone 'main', but its Archidekt categories put it in 'maybeboard')", text);
        Assert.DoesNotContain("categories give", text, StringComparison.Ordinal);
        Assert.EndsWith(
            "Send one of these as a change in local_deck_apply_changes.",
            text,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a contradiction whose match change would be refused without a new primary category says so in words and
    /// offers only the leave change as JSON, and one whose match change would not move the entry says to change its
    /// primary category.
    /// </summary>
    [Fact]
    public void DescribeZoneContradictions_WhenMatchCannotClearIt_SaysWhatElseToChange()
    {
        ArchidektZoneContradiction needsPrimary = Contradiction(
            "Rock",
            "main",
            "sideboard",
            ArchidektMatchFix.NeedsPrimaryCategory);
        ArchidektZoneContradiction doesNotClear = Contradiction(
            "Bolt",
            "main",
            "maybeboard",
            ArchidektMatchFix.DoesNotClear);

        string text = ArchidektSyncNotices.DescribeZoneContradictions([needsPrimary, doesNotClear]);

        Assert.All(ChangesIn(text), value => Assert.Equal("leave", Mode(value)));
        Assert.Equal(2, ChangesIn(text).Count);
        Assert.Contains("primaryCategoryId", text, StringComparison.Ordinal);
        Assert.Contains("change the entry's primary category instead", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies more entries than one change may name are split into changes of at most 100 entries, and that every
    /// entry is named once in each kind of change.
    /// </summary>
    [Fact]
    public void DescribeZoneContradictions_WithMoreThanAHundredEntries_SplitsTheChanges()
    {
        ArchidektZoneContradiction[] entries =
        [
            .. Enumerable.Range(1, 150).Select(number =>
                Contradiction($"Card {number:000}", "sideboard", "main", ArchidektMatchFix.Clears)),
        ];

        string text = ArchidektSyncNotices.DescribeZoneContradictions(entries);

        List<JsonElement> changes = ChangesIn(text);
        Assert.Equal([100, 100, 50, 50], changes.Select(value => value.GetProperty("entryIds").GetArrayLength()));
        Assert.Equal(["leave", "match", "leave", "match"], changes.Select(Mode));
        HashSet<Guid> leaveIds =
        [
            .. changes.Where(value => Mode(value) == "leave")
                .SelectMany(value => value.GetProperty("entryIds").EnumerateArray())
                .Select(value => Guid.Parse(value.GetString()!)),
        ];
        Assert.Equal(entries.Select(value => value.EntryId).Order(), leaveIds.Order());
    }

    /// <summary>
    /// Verifies the pull that keeps cards added locally describes a zone contradiction with the same wording, naming
    /// the card, its zone, and the zone its Archidekt categories put it in.
    /// </summary>
    [Fact]
    public void DescribePullConflict_WithAKeptCardInAnotherZone_NamesTheZoneTheCategoriesGive()
    {
        ArchidektPullKeep keep = new(
            [],
            [],
            [],
            [Contradiction("Rock", "sideboard", "main", ArchidektMatchFix.Clears)],
            new Dictionary<Guid, string>());

        string? text = ArchidektSyncNotices.DescribePullConflict(keep, []);

        Assert.NotNull(text);
        Assert.Contains(
            "'Rock' (zone 'sideboard', but its Archidekt categories put it in 'main')",
            text,
            StringComparison.Ordinal);
        Assert.Contains("discardLocalChanges", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds one contradiction with a new entry ID.
    /// </summary>
    private static ArchidektZoneContradiction Contradiction(
        string cardName,
        string zone,
        string expectedZone,
        ArchidektMatchFix matchFix)
    {
        return new ArchidektZoneContradiction(Guid.NewGuid(), cardName, zone, expectedZone, matchFix);
    }

    /// <summary>
    /// Parses every patch-entries change a notice writes, in the order it writes them.
    /// </summary>
    private static List<JsonElement> ChangesIn(string text)
    {
        return [.. ChangePattern().Matches(text).Select(value => JsonElement.Parse(value.Value))];
    }

    /// <summary>
    /// Reads the zone a change sets.
    /// </summary>
    private static string Zone(JsonElement change)
    {
        return change.GetProperty("zone").GetString()!;
    }

    /// <summary>
    /// Reads the zone categories mode a change sets.
    /// </summary>
    private static string Mode(JsonElement change)
    {
        return change.GetProperty("zoneCategories").GetString()!;
    }

    /// <summary>
    /// Finds one patch-entries change written as a JSON object in a notice.
    /// </summary>
    [GeneratedRegex("""\{"kind":"patch-entries".*?\}""", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ChangePattern();
}
