using MtgMcp.Core.Results;

namespace MtgMcp.Archidekt.Tests;

/// <summary>
/// Provides explicitly enabled disposable acceptance against the current authenticated Archidekt contract.
/// </summary>
public sealed class ArchidektLiveTests
{
    /// <summary>
    /// Verifies one bounded authenticated read without creating or changing provider state.
    /// </summary>
    [Fact]
    [Trait("Category", "Live")]
    public async Task OwnedDecks_ReturnOneBoundedReadPage()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("MTGMCP_RUN_ARCHIDEKT_READ_LIVE"),
                "1",
                StringComparison.Ordinal))
        {
            Assert.Skip("Set MTGMCP_RUN_ARCHIDEKT_READ_LIVE=1 to run the authenticated read-only Archidekt check.");
        }

        string credentialsFile = GetCredentialsFile();
        if (!File.Exists(credentialsFile))
        {
            Assert.Skip("The standard Archidekt credentials file is not available.");
        }

        using ArchidektService service = new(
            ArchidektOptions.CreateDefault(credentialsFile: credentialsFile),
            "0.9.0-preview.1");
        RemoteDeckPage page = Success(await service.ListDecksAsync(
            cursor: null,
            pageSize: 1,
            TestContext.Current.CancellationToken));

        Assert.True(page.Items.Count <= 1);
        Assert.Equal("archidekt", page.Evidence.Source);
        Assert.NotEmpty(page.Evidence.SourceChecksum);
    }

    /// <summary>
    /// Creates, organizes, snapshots, and removes disposable provider state under conservative production pacing.
    /// </summary>
    [Fact]
    [Trait("Category", "Live")]
    public async Task DisposableLifecycle_CreatesVerifiesAndCleansEveryObject()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("MTGMCP_RUN_ARCHIDEKT_LIVE"),
                "1",
                StringComparison.Ordinal))
        {
            Assert.Skip("Set MTGMCP_RUN_ARCHIDEKT_LIVE=1 to run the disposable authenticated Archidekt lifecycle.");
        }

        string credentialsFile = GetCredentialsFile();
        if (!File.Exists(credentialsFile))
        {
            Assert.Skip("The standard Archidekt credentials file is not available.");
        }

        using ArchidektService service = new(
            ArchidektOptions.CreateDefault(credentialsFile: credentialsFile),
            "0.9.0-preview.1");
        string suffix = Guid.NewGuid().ToString("N")[..10];
        string deckName = $"mtg-mcp disposable {suffix}";
        string folderName = $"mcp probe {suffix}";
        string snapshotName = $"mtg-mcp disposable snapshot {suffix}";
        RemoteDeckSnapshot? deck = null;
        RemoteFolderRecord? folder = null;
        RemoteNamedSnapshotSummary? snapshot = null;
        try
        {
            deck = Success(await service.CreateDeckAsync(
                new ArchidektDeckCreateRequest(
                    deckName,
                    "commander",
                    "Disposable contract acceptance; safe to delete."),
                TestContext.Current.CancellationToken));
            Assert.Equal("private", deck.Visibility);
            Assert.Equal(deckName, deck.Name);

            RemoteDeckPage listed = Success(await service.ListDecksAsync(
                cursor: null,
                pageSize: 100,
                TestContext.Current.CancellationToken));
            Assert.Contains(listed.Items, value => value.RemoteId == deck.RemoteId);

            folder = Success(await service.CreateFolderAsync(
                new ArchidektFolderCreateRequest(folderName, "private"),
                TestContext.Current.CancellationToken));
            RemoteFolderTree beforeUpdate = Success(await service.ListFoldersAsync(
                TestContext.Current.CancellationToken));
            folder = Success(await service.UpdateFolderAsync(
                new ArchidektFolderUpdateRequest(
                    folder.FolderId,
                    beforeUpdate.TreeFingerprint,
                    Name: $"mcp renamed {suffix}"),
                TestContext.Current.CancellationToken));
            RemoteFolderTree beforeMove = Success(await service.ListFoldersAsync(
                TestContext.Current.CancellationToken));
            ArchidektFolderMoveResult moved = Success(await service.MoveFolderItemsAsync(
                new ArchidektFolderMoveRequest(
                    beforeMove.TreeFingerprint,
                    [new ArchidektFolderMoveItem("deck", deck.RemoteId, deck.ParentFolderId)],
                    folder.FolderId),
                TestContext.Current.CancellationToken));
            Assert.Equal("applied", Assert.Single(moved.Items).Status);

            deck = Success(await service.GetDeckAsync(
                deck.RemoteId,
                TestContext.Current.CancellationToken));
            snapshot = Success(await service.CreateSnapshotAsync(
                new ArchidektSnapshotCreateRequest(
                    deck.RemoteId,
                    deck.RemoteFingerprint,
                    snapshotName,
                    "Disposable contract acceptance."),
                TestContext.Current.CancellationToken));
            RemoteNamedSnapshot completeSnapshot = Success(await service.GetSnapshotAsync(
                deck.RemoteId,
                snapshot.SnapshotId,
                TestContext.Current.CancellationToken));
            snapshot = Success(await service.UpdateSnapshotAsync(
                new ArchidektSnapshotUpdateRequest(
                    deck.RemoteId,
                    snapshot.SnapshotId,
                    completeSnapshot.Summary.Checksum,
                    $"{snapshotName} updated"),
                TestContext.Current.CancellationToken));
            ArchidektSnapshotRestorePreview restore = Success(await service.PreviewSnapshotRestoreAsync(
                deck.RemoteId,
                snapshot.SnapshotId,
                TestContext.Current.CancellationToken));
            if (restore.Operations.Count > 0)
            {
                List<string> metadataDifferences = [];
                AddDifference(metadataDifferences, "name", deck.Name, completeSnapshot.Deck.Name);
                AddDifference(metadataDifferences, "description", deck.Description, completeSnapshot.Deck.Description);
                AddDifference(metadataDifferences, "format", deck.Format, completeSnapshot.Deck.Format);
                AddDifference(metadataDifferences, "visibility", deck.Visibility, completeSnapshot.Deck.Visibility);
                AddDifference(metadataDifferences, "parent-folder", deck.ParentFolderId, completeSnapshot.Deck.ParentFolderId);
                throw new Xunit.Sdk.XunitException(
                    $"An unchanged snapshot planned: {string.Join(", ", restore.Operations.Select(value => value.Kind))}; metadata differences: {string.Join(", ", metadataDifferences)}");
            }
            ArchidektApplyResult restored = Success(await service.ApplySnapshotRestoreAsync(
                new ArchidektSnapshotRestoreApplyRequest(
                    deck.RemoteId,
                    snapshot.SnapshotId,
                    restore.SnapshotChecksum,
                    restore.SnapshotContentFingerprint,
                    restore.RemoteFingerprint,
                    restore.PreviewFingerprint,
                    $"restore snapshot {snapshot.SnapshotId}"),
                TestContext.Current.CancellationToken));
            if (restored.Outcome != "applied")
            {
                string statuses = string.Join(
                    ", ",
                    restored.Operations.Select(value =>
                        $"{value.Kind}={value.Status}:{value.Message}"));
                throw new Xunit.Sdk.XunitException($"Snapshot restore was {restored.Outcome}: {statuses}");
            }

            RemoteFolderTree beforeRootMove = Success(await service.ListFoldersAsync(
                TestContext.Current.CancellationToken));
            deck = Success(await service.GetDeckAsync(
                deck.RemoteId,
                TestContext.Current.CancellationToken));
            ArchidektFolderMoveResult movedToRoot = Success(await service.MoveFolderItemsAsync(
                new ArchidektFolderMoveRequest(
                    beforeRootMove.TreeFingerprint,
                    [new ArchidektFolderMoveItem("deck", deck.RemoteId, deck.ParentFolderId)],
                    DestinationFolderId: null),
                TestContext.Current.CancellationToken));
            Assert.Equal("applied", Assert.Single(movedToRoot.Items).Status);

            snapshot = await DeleteSnapshotAsync(service, deck.RemoteId, snapshot).ConfigureAwait(false);
            deck = await DeleteDeckAsync(service, deck).ConfigureAwait(false);
            folder = await DeleteFolderAsync(service, folder).ConfigureAwait(false);
        }
        finally
        {
            if (snapshot is not null && deck is not null)
            {
                snapshot = await DeleteSnapshotAsync(service, deck.RemoteId, snapshot).ConfigureAwait(false);
            }

            if (deck is not null)
            {
                deck = await DeleteDeckAsync(service, deck).ConfigureAwait(false);
            }

            if (folder is not null)
            {
                _ = await DeleteFolderAsync(service, folder).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Deletes one still-present disposable snapshot using fresh checksum evidence.
    /// </summary>
    private static async Task<RemoteNamedSnapshotSummary?> DeleteSnapshotAsync(
        ArchidektService service,
        string deckId,
        RemoteNamedSnapshotSummary snapshot)
    {
        OperationResult<RemoteNamedSnapshot> current = await service.GetSnapshotAsync(
            deckId,
            snapshot.SnapshotId,
            TestContext.Current.CancellationToken);
        if (current is OperationNotFound)
        {
            return null;
        }

        RemoteNamedSnapshot exact = Success(current);
        ArchidektApplyResult deleted = Success(await service.DeleteSnapshotAsync(
            new ArchidektSnapshotDeleteRequest(
                deckId,
                snapshot.SnapshotId,
                exact.Summary.Checksum,
                $"delete snapshot {snapshot.SnapshotId}"),
            TestContext.Current.CancellationToken));
        Assert.Equal("applied", deleted.Outcome);
        return null;
    }

    /// <summary>
    /// Deletes one still-present disposable deck using fresh remote fingerprint evidence.
    /// </summary>
    private static async Task<RemoteDeckSnapshot?> DeleteDeckAsync(
        ArchidektService service,
        RemoteDeckSnapshot deck)
    {
        OperationResult<RemoteDeckSnapshot> current = await service.GetDeckAsync(
            deck.RemoteId,
            TestContext.Current.CancellationToken);
        if (current is OperationNotFound)
        {
            return null;
        }

        RemoteDeckSnapshot exact = Success(current);
        ArchidektApplyResult deleted = Success(await service.DeleteDeckAsync(
            new ArchidektDeckDeleteRequest(
                deck.RemoteId,
                exact.RemoteFingerprint,
                $"delete {deck.RemoteId}"),
            TestContext.Current.CancellationToken));
        Assert.Equal("applied", deleted.Outcome);
        return null;
    }

    /// <summary>
    /// Deletes one still-present disposable empty folder using a fresh tree fingerprint.
    /// </summary>
    private static async Task<RemoteFolderRecord?> DeleteFolderAsync(
        ArchidektService service,
        RemoteFolderRecord folder)
    {
        RemoteFolderTree tree = Success(await service.ListFoldersAsync(
            TestContext.Current.CancellationToken));
        RemoteFolderRecord? current = tree.Items.FirstOrDefault(value => value.FolderId == folder.FolderId);
        if (current is null)
        {
            return null;
        }

        ArchidektApplyResult deleted = Success(await service.DeleteFolderAsync(
            new ArchidektFolderDeleteRequest(
                current.FolderId,
                current.Name,
                tree.TreeFingerprint,
                $"delete folder {current.FolderId}"),
            TestContext.Current.CancellationToken));
        Assert.Equal("applied", deleted.Outcome);
        return null;
    }

    /// <summary>
    /// Extracts one successful result without logging provider identities or credentials.
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
    /// Creates a diagnostic containing only the adapter's sanitized reason and message.
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

    /// <summary>
    /// Records only a differing field name, never its provider value.
    /// </summary>
    private static void AddDifference(
        ICollection<string> differences,
        string field,
        string? current,
        string? snapshot)
    {
        if (!string.Equals(current, snapshot, StringComparison.Ordinal))
        {
            differences.Add(field);
        }
    }
}
