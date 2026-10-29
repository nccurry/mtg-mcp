using MtgMcp.App.Archidekt;
using MtgMcp.App.Hosting;
using MtgMcp.Archidekt;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;
using MtgMcp.Scryfall;
using MtgMcp.Scryfall.Tests;

namespace MtgMcp.App.Tests;

/// <summary>
/// Provides explicitly enabled disposable acceptance of the whole pull, edit, push, and verify workflow against the
/// current authenticated Archidekt contract.
/// </summary>
public sealed class ArchidektSyncLiveTests
{
    /// <summary>
    /// Pulls a disposable Archidekt deck into a temporary local deck store, adds a card and a colored category
    /// locally, pushes, and checks that the push verified and left nothing to pull. The remote deck is always removed.
    /// </summary>
    [Fact]
    [Trait("Category", "Live")]
    public async Task DisposableDeck_PulledEditedAndPushed_VerifiesAndLeavesNoDifference()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("MTGMCP_RUN_ARCHIDEKT_LIVE"),
                "1",
                StringComparison.Ordinal))
        {
            Assert.Skip("Set MTGMCP_RUN_ARCHIDEKT_LIVE=1 to run the disposable authenticated Archidekt sync workflow.");
        }

        string credentialsFile = GetCredentialsFile();
        if (!File.Exists(credentialsFile))
        {
            Assert.Skip("The standard Archidekt credentials file is not available.");
        }

        string suffix = Guid.NewGuid().ToString("N")[..10];
        string root = Path.Combine(Path.GetTempPath(), $"mtg-mcp-arch-live-{suffix}");
        using ArchidektService service = new(
            ArchidektOptions.CreateDefault(credentialsFile: credentialsFile),
            FoundationServerIdentity.PackageVersion);
        string deckName = $"mtg-mcp sync disposable {suffix}";
        RemoteDeckSnapshot? deck = null;
        bool bodyFailed = false;
        string? cleanupFailure;
        try
        {
            ScryfallCard card = await ReadCardAsync(root).ConfigureAwait(false);
            deck = Success(await service.CreateDeckAsync(
                new ArchidektDeckCreateRequest(
                    deckName,
                    "commander",
                    "Disposable sync acceptance; safe to delete."),
                TestContext.Current.CancellationToken));

            using SqliteDeckStore store = new(Path.Combine(root, "decks"), "live");
            ArchidektCoordinator coordinator = new(service, store);
            ArchidektSyncPreview pullPreview = Success(await coordinator.PreviewPullAsync(
                deck.RemoteId,
                localDeckId: null,
                TestContext.Current.CancellationToken));
            ArchidektApplyResult pulled = Success(await coordinator.ApplyPullAsync(
                new ArchidektPullApplyRequest(
                    deck.RemoteId,
                    null,
                    null,
                    pullPreview.RemoteFingerprint,
                    pullPreview.PreviewFingerprint),
                TestContext.Current.CancellationToken));
            Guid localDeckId = Assert.IsType<Guid>(pulled.LocalDeckId);

            DeckDocument local = Success(await store.GetAsync(localDeckId, TestContext.Current.CancellationToken));
            Guid entryId = Guid.CreateVersion7();
            Guid categoryId = Guid.CreateVersion7();
            local = Success(await store.ApplyChangesAsync(
                localDeckId,
                local.Revision,
                [
                    new AddDeckEntryChange(new DeckEntryDraft(
                        1,
                        card.Name,
                        card.OracleId,
                        card.Id,
                        card.SetCode,
                        card.CollectorNumber,
                        card.Language,
                        "nonfoil",
                        "main",
                        EntryId: entryId)),
                    new AddDeckCategoryChange(new DeckCategoryDraft(
                        $"Acceptance {suffix}",
                        "#6633ff",
                        CategoryId: categoryId)),
                    new AssignDeckCategoryChange(entryId, categoryId, IsPrimary: true),
                ],
                TestContext.Current.CancellationToken));

            ArchidektSyncPreview pushPreview = Success(await coordinator.PreviewPushAsync(
                localDeckId,
                TestContext.Current.CancellationToken));
            Assert.NotEmpty(pushPreview.Operations);
            ArchidektApplyResult pushed = Success(await coordinator.ApplyPushAsync(
                new ArchidektPushApplyRequest(
                    localDeckId,
                    local.Revision,
                    pushPreview.RemoteFingerprint,
                    pushPreview.PreviewFingerprint),
                TestContext.Current.CancellationToken));
            Assert.True(
                pushed.Outcome == "applied",
                $"The push outcome was {pushed.Outcome}: " +
                string.Join(", ", pushed.Operations.Select(value => $"{value.Kind}={value.Status}")));

            ArchidektSyncPreview afterPull = Success(await coordinator.PreviewPullAsync(
                deck.RemoteId,
                localDeckId,
                TestContext.Current.CancellationToken));
            Assert.False(afterPull.HasConflicts);
            Assert.Empty(afterPull.Differences);
        }
        catch
        {
            bodyFailed = true;
            throw;
        }
        finally
        {
            cleanupFailure = await CleanUpAsync(service, deckName, deck, root).ConfigureAwait(false);
            if (cleanupFailure is not null && bodyFailed)
            {
                TestContext.Current.SendDiagnosticMessage(cleanupFailure);
            }
        }

        if (cleanupFailure is not null)
        {
            throw new Xunit.Sdk.XunitException(cleanupFailure);
        }
    }

    /// <summary>
    /// Reads one stable card with its printing identity from Scryfall, so the pushed entry names an exact printing.
    /// </summary>
    private static async Task<ScryfallCard> ReadCardAsync(string root)
    {
        using ScryfallService scryfall = new(
            Path.Combine(root, "scryfall"),
            allowLocalWrites: true,
            FoundationServerIdentity.PackageVersion,
            ScryfallTestFixture.ResultOptions);
        ScryfallCardResponse response = Success(await scryfall.GetCardAsync(
            new ScryfallCardLookup("exact-name", "Sol Ring"),
            "default",
            "standard",
            null,
            TestContext.Current.CancellationToken));
        return Assert.IsType<ScryfallCard>(response.Card.Value);
    }

    /// <summary>
    /// Removes the disposable remote deck and the temporary directory with a fresh timeout, so a cancelled or timed out
    /// test still cleans up. A deck the test never received (creation failed after the request reached Archidekt) is
    /// found by its unique name. Returns a description of what could not be cleaned, or null; one failure never
    /// stops the other step.
    /// </summary>
    private static async Task<string?> CleanUpAsync(
        ArchidektService service,
        string deckName,
        RemoteDeckSnapshot? deck,
        string root)
    {
        List<string> problems = [];
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));
        try
        {
            string? remoteId = deck?.RemoteId ?? await FindDeckIdByNameAsync(service, deckName, timeout.Token)
                .ConfigureAwait(false);
            if (remoteId is not null)
            {
                await DeleteDeckAsync(service, remoteId, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            problems.Add($"The disposable Archidekt deck \"{deckName}\" may still exist and needs deleting by hand: " +
                exception.Message);
        }

        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            problems.Add($"The temporary directory could not be deleted: {exception.Message}");
        }

        return problems.Count == 0 ? null : string.Join(" ", problems);
    }

    /// <summary>
    /// Looks through the owned decks for one with the exact disposable name.
    /// </summary>
    private static async Task<string?> FindDeckIdByNameAsync(
        ArchidektService service,
        string deckName,
        CancellationToken cancellationToken)
    {
        string? cursor = null;
        do
        {
            RemoteDeckPage page = Success(await service.ListDecksAsync(cursor, 100, cancellationToken)
                .ConfigureAwait(false));
            RemoteDeckSummary? match = page.Items.FirstOrDefault(value =>
                string.Equals(value.Name, deckName, StringComparison.Ordinal));
            if (match is not null)
            {
                return match.RemoteId;
            }

            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return null;
    }

    /// <summary>
    /// Deletes the disposable deck, if it still exists, using fresh remote fingerprint evidence.
    /// </summary>
    private static async Task DeleteDeckAsync(
        ArchidektService service,
        string remoteId,
        CancellationToken cancellationToken)
    {
        OperationResult<RemoteDeckSnapshot> current = await service.GetDeckAsync(remoteId, cancellationToken)
            .ConfigureAwait(false);
        if (current is OperationNotFound)
        {
            return;
        }

        RemoteDeckSnapshot exact = Success(current);
        ArchidektApplyResult deleted = Success(await service.DeleteDeckAsync(
            new ArchidektDeckDeleteRequest(
                remoteId,
                exact.RemoteFingerprint,
                $"delete {remoteId}"),
            cancellationToken).ConfigureAwait(false));
        Assert.Equal("applied", deleted.Outcome);
    }

    /// <summary>
    /// Extracts one successful result, naming only the sanitized reason and message of a failure.
    /// </summary>
    private static T Success<T>(OperationResult<T> result)
    {
        return result.Value switch
        {
            OperationSuccess<T> success => success.Data,
            OperationInvalidInput value => throw Failure(value.ReasonCode, value.Message),
            OperationNotFound value => throw Failure(value.ReasonCode, value.Message),
            OperationConflict value => throw Failure(value.ReasonCode, value.Message),
            OperationUnsupported value => throw Failure(value.ReasonCode, value.Message),
            OperationUnavailable value => throw Failure(value.ReasonCode, value.Message),
            _ => throw new Xunit.Sdk.XunitException("The live provider returned an unknown result case."),
        };
    }

    /// <summary>
    /// Creates a diagnostic containing only the sanitized reason and message.
    /// </summary>
    private static Xunit.Sdk.XunitException Failure(string reasonCode, string message)
    {
        return new Xunit.Sdk.XunitException($"{reasonCode}: {message}");
    }

    /// <summary>
    /// Resolves the explicit or standard credential-file location without exposing it.
    /// </summary>
    private static string GetCredentialsFile()
    {
        return Environment.GetEnvironmentVariable("MTGMCP_ARCHIDEKT_CREDENTIALS_FILE")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".mtg-mcp",
                "archidekt.json");
    }
}
