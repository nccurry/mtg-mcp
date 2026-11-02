using System.Text.Json;

namespace MtgMcp.Archidekt.Tests;

/// <summary>
/// Proves the check that tells an edit made on Archidekt during a push from the edits the push made.
/// </summary>
public sealed class ArchidektChangedOutsidePushTests
{
    /// <summary>
    /// Verifies a read after the push that differs only by what an applied operation changed counts as no outside
    /// edit, and the same read with nothing sent does.
    /// </summary>
    [Fact]
    public void ChangedOutsidePush_WithOnlyThePushedCategoryChange_ReturnsFalse()
    {
        RemoteDeckSnapshot before = ReadDeck();
        RemoteDeckSnapshot after = WithFirstCategory(before, value => value with { IsPremier = !value.IsPremier });
        ArchidektRemotePlan plan = ArchidektSyncPlanner.PlanRemoteApply(before, after);
        ArchidektRemoteOperation update = Assert.Single(plan.PlannedOperations).Public;

        Assert.False(ArchidektSyncPlanner.ChangedOutsidePush(before, before, NoOperations(before), []));
        Assert.False(ArchidektSyncPlanner.ChangedOutsidePush(before, after, plan, [Status(update, "applied")]));
        Assert.True(ArchidektSyncPlanner.ChangedOutsidePush(before, after, plan, [Status(update, "not-attempted")]));
    }

    /// <summary>
    /// Verifies a change to the deck metadata that no operation made is an outside edit.
    /// </summary>
    [Fact]
    public void ChangedOutsidePush_WithChangedMetadata_ReturnsTrue()
    {
        RemoteDeckSnapshot before = ReadDeck();

        Assert.True(ArchidektSyncPlanner.ChangedOutsidePush(
            before,
            before with { Name = "Renamed elsewhere" },
            NoOperations(before),
            []));
    }

    /// <summary>
    /// Verifies a change to the flags of a category no operation touched is an outside edit.
    /// </summary>
    [Fact]
    public void ChangedOutsidePush_WithAChangedCategory_ReturnsTrue()
    {
        RemoteDeckSnapshot before = ReadDeck();

        Assert.True(ArchidektSyncPlanner.ChangedOutsidePush(
            before,
            WithFirstCategory(before, value => value with { IsPremier = !value.IsPremier }),
            NoOperations(before),
            []));
    }

    /// <summary>
    /// Verifies a category that appeared without a category-create operation is an outside edit.
    /// </summary>
    [Fact]
    public void ChangedOutsidePush_WithAnExtraCategory_ReturnsTrue()
    {
        RemoteDeckSnapshot before = ReadDeck();
        RemoteDeckCategory extra = before.Categories[0] with { ProviderCategoryId = "999", Name = "Added elsewhere" };

        Assert.True(ArchidektSyncPlanner.ChangedOutsidePush(
            before,
            before with { Categories = [.. before.Categories, extra] },
            NoOperations(before),
            []));
    }

    /// <summary>
    /// Verifies the composed deck after an outside edit keeps the untouched rows as read before the push and takes
    /// what the push changed from the read after it, with fingerprints that match a read of that content.
    /// </summary>
    [Fact]
    public void ComposeAfterOutsideEdit_TakesOnlyThePushedChangeFromTheReadAfter()
    {
        RemoteDeckSnapshot before = ReadDeck();
        RemoteDeckSnapshot pushed = before with { Name = "Pushed name" };
        ArchidektRemotePlan plan = ArchidektSyncPlanner.PlanRemoteApply(before, pushed);
        ArchidektRemoteOperation update = Assert.Single(plan.PlannedOperations).Public;
        RemoteDeckSnapshot after = WithFirstCategory(pushed, value => value with { IsPremier = !value.IsPremier });

        RemoteDeckSnapshot composed = ArchidektSyncPlanner.ComposeAfterOutsideEdit(
            before,
            after,
            plan,
            [Status(update, "applied")]);
        RemoteDeckSnapshot unchanged = ArchidektSyncPlanner.ComposeAfterOutsideEdit(
            before,
            WithFirstCategory(before, value => value with { IsPremier = !value.IsPremier }),
            NoOperations(before),
            []);

        Assert.Equal("Pushed name", composed.Name);
        Assert.Equal(before.Categories, composed.Categories);
        Assert.NotEqual(before.RemoteFingerprint, composed.RemoteFingerprint);
        Assert.Equal(before.RemoteFingerprint, unchanged.RemoteFingerprint);
        Assert.Equal(before.ContentFingerprint, unchanged.ContentFingerprint);
    }

    /// <summary>
    /// Reads the canonical test deck.
    /// </summary>
    private static RemoteDeckSnapshot ReadDeck()
    {
        using JsonDocument document = JsonDocument.Parse(ArchidektTestPayloads.Deck);
        return ArchidektDeckContractMapper.MapDeck(
            document.RootElement,
            ArchidektTestPayloads.Deck,
            DateTimeOffset.UtcNow,
            "GET");
    }

    /// <summary>
    /// Returns the deck with its first category changed.
    /// </summary>
    private static RemoteDeckSnapshot WithFirstCategory(
        RemoteDeckSnapshot deck,
        Func<RemoteDeckCategory, RemoteDeckCategory> change)
    {
        return deck with { Categories = [change(deck.Categories[0]), .. deck.Categories.Skip(1)] };
    }

    /// <summary>
    /// Plans a push with no operations.
    /// </summary>
    private static ArchidektRemotePlan NoOperations(RemoteDeckSnapshot deck)
    {
        return ArchidektSyncPlanner.PlanRemoteApply(deck, deck);
    }

    /// <summary>
    /// Builds the status a push reported for one operation.
    /// </summary>
    private static ArchidektOperationStatus Status(ArchidektRemoteOperation operation, string status)
    {
        return new ArchidektOperationStatus(operation.Sequence, operation.Kind, operation.Subject, status, string.Empty);
    }
}
