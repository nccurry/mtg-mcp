namespace MtgMcp.Archidekt;

/// <summary>
/// Centralizes authentication, pacing, retries, and request budgets while routing provider domains explicitly.
/// </summary>
internal sealed class ArchidektTransport : IDisposable
{
    /// <summary>Owns shared HTTP, credentials, pacing, retry, and authentication state.</summary>
    private readonly ArchidektTransportContext context;

    /// <summary>Owns deck routes and card-resolution transport operations.</summary>
    private readonly ArchidektDeckTransport decks;

    /// <summary>Owns folder tree and mutation transport operations.</summary>
    private readonly ArchidektFolderTransport folders;

    /// <summary>Owns named snapshot transport operations.</summary>
    private readonly ArchidektSnapshotTransport snapshots;

    /// <summary>Creates a production transport with an honestly identified HTTP client.</summary>
    internal ArchidektTransport(ArchidektOptions options, string packageVersion)
        : this(new ArchidektTransportContext(options, packageVersion))
    {
    }

    /// <summary>Creates a deterministic transport over an injected HTTP client.</summary>
    internal ArchidektTransport(
        HttpClient httpClient,
        bool ownsHttpClient,
        ArchidektOptions options,
        ArchidektRequestPacer? pacer = null)
        : this(new ArchidektTransportContext(httpClient, ownsHttpClient, options, pacer))
    {
    }

    /// <summary>Creates the transport facade around one shared context.</summary>
    private ArchidektTransport(ArchidektTransportContext context)
    {
        this.context = context;
        decks = new ArchidektDeckTransport(context);
        folders = new ArchidektFolderTransport(context);
        snapshots = new ArchidektSnapshotTransport(context);
    }

    /// <summary>Gets redacted local credential and session readiness without provider I/O.</summary>
    internal ArchidektAuthStatus GetAuthStatus()
    {
        return context.GetAuthStatus();
    }

    /// <summary>Maps one supported format label to the observed Archidekt format identifier.</summary>
    internal static int MapFormatId(string value)
    {
        return ArchidektTransportContext.MapFormatId(value);
    }

    /// <summary>Preserves numeric provider IDs as numbers and opaque IDs as strings.</summary>
    internal static object? ParseProviderId(string? value)
    {
        return ArchidektTransportContext.ParseProviderId(value);
    }

    /// <summary>Fetches one authenticated page of configured-user decks.</summary>
    internal Task<RemoteDeckPage> ListDecksAsync(
        string? cursor,
        int pageSize,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return decks.ListAsync(cursor, pageSize, budget, cancellationToken);
    }

    /// <summary>Fetches one public or authenticated remote deck.</summary>
    internal Task<RemoteDeckSnapshot> GetDeckAsync(
        string deckId,
        bool requireAuthentication,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return decks.GetAsync(deckId, requireAuthentication, budget, cancellationToken);
    }

    /// <summary>Creates one private-by-default remote deck.</summary>
    internal Task<RemoteDeckSnapshot> CreateDeckAsync(
        ArchidektDeckCreateRequest request,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return decks.CreateAsync(request, budget, cancellationToken);
    }

    /// <summary>Deletes one exact remote deck.</summary>
    internal Task DeleteDeckAsync(
        string deckId,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return decks.DeleteAsync(deckId, budget, cancellationToken);
    }

    /// <summary>Sends one deck metadata patch.</summary>
    internal Task SendDeckMetadataAsync(
        string deckId,
        object payload,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return decks.SendMetadataAsync(deckId, payload, budget, cancellationToken);
    }

    /// <summary>Sends one category creation payload.</summary>
    internal Task SendCategoryCreateAsync(
        string deckId,
        object payload,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return decks.SendCategoryCreateAsync(deckId, payload, budget, cancellationToken);
    }

    /// <summary>Sends one category update payload.</summary>
    internal Task SendCategoryUpdateAsync(
        string categoryId,
        object payload,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return decks.SendCategoryUpdateAsync(categoryId, payload, budget, cancellationToken);
    }

    /// <summary>Sends one category deletion.</summary>
    internal Task SendCategoryDeleteAsync(
        string categoryId,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return decks.SendCategoryDeleteAsync(categoryId, budget, cancellationToken);
    }

    /// <summary>Sends one card mutation payload.</summary>
    internal Task SendCardMutationAsync(
        string deckId,
        object payload,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return decks.SendCardMutationAsync(deckId, payload, budget, cancellationToken);
    }

    /// <summary>Resolves one exact Archidekt card identifier for a remote deck entry.</summary>
    internal Task<string> ResolveCardIdAsync(
        RemoteDeckEntry entry,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return decks.ResolveCardIdAsync(entry, budget, cancellationToken);
    }

    /// <summary>Fetches the complete authenticated folder tree.</summary>
    internal Task<RemoteFolderTree> ListFoldersAsync(
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return folders.ListAsync(budget, cancellationToken);
    }

    /// <summary>Fetches one authenticated folder detail.</summary>
    internal Task<RemoteFolderTree> GetFolderAsync(
        string folderId,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return folders.GetAsync(folderId, budget, cancellationToken);
    }

    /// <summary>Creates one folder and returns its exact remote identifier.</summary>
    internal Task<string> CreateFolderAsync(
        ArchidektFolderCreateRequest request,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return folders.CreateAsync(request, budget, cancellationToken);
    }

    /// <summary>Sends one folder metadata update payload.</summary>
    internal Task SendFolderUpdateAsync(
        string folderId,
        object payload,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return folders.SendUpdateAsync(folderId, payload, budget, cancellationToken);
    }

    /// <summary>Sends one exact typed-item move payload.</summary>
    internal Task SendFolderMoveAsync(
        object payload,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return folders.SendMoveAsync(payload, budget, cancellationToken);
    }

    /// <summary>Sends one empty-folder deletion payload.</summary>
    internal Task SendFolderDeleteAsync(
        object payload,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return folders.SendDeleteAsync(payload, budget, cancellationToken);
    }

    /// <summary>Lists named snapshots for one remote deck.</summary>
    internal Task<RemoteNamedSnapshotPage> ListSnapshotsAsync(
        string deckId,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return snapshots.ListAsync(deckId, budget, cancellationToken);
    }

    /// <summary>Gets one complete named snapshot.</summary>
    internal Task<RemoteNamedSnapshot> GetSnapshotAsync(
        string deckId,
        string snapshotId,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return snapshots.GetAsync(deckId, snapshotId, budget, cancellationToken);
    }

    /// <summary>Sends one named snapshot creation payload.</summary>
    internal Task SendSnapshotCreateAsync(
        string deckId,
        object payload,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return snapshots.SendCreateAsync(deckId, payload, budget, cancellationToken);
    }

    /// <summary>Sends one named snapshot metadata update payload.</summary>
    internal Task SendSnapshotUpdateAsync(
        string snapshotId,
        object payload,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return snapshots.SendUpdateAsync(snapshotId, payload, budget, cancellationToken);
    }

    /// <summary>Sends one named snapshot deletion.</summary>
    internal Task SendSnapshotDeleteAsync(
        string snapshotId,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return snapshots.SendDeleteAsync(snapshotId, budget, cancellationToken);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        context.Dispose();
    }
}

/// <summary>
/// Owns Archidekt deck and card-resolution provider routes.
/// </summary>
internal sealed class ArchidektDeckTransport
{
    /// <summary>Stores the shared authenticated HTTP context.</summary>
    private readonly ArchidektTransportContext context;

    /// <summary>Creates deck transport operations around one shared context.</summary>
    internal ArchidektDeckTransport(ArchidektTransportContext context)
    {
        this.context = context;
    }

    /// <summary>Lists one authenticated deck page.</summary>
    internal Task<RemoteDeckPage> ListAsync(string? cursor, int pageSize, ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return context.ListDecksAsync(cursor, pageSize, budget, cancellationToken);
    }

    /// <summary>Gets one public or authenticated deck.</summary>
    internal Task<RemoteDeckSnapshot> GetAsync(string deckId, bool requireAuthentication,
        ArchidektOperationBudget budget, CancellationToken cancellationToken)
    {
        return context.GetDeckAsync(deckId, requireAuthentication, budget, cancellationToken);
    }

    /// <summary>Creates one remote deck.</summary>
    internal Task<RemoteDeckSnapshot> CreateAsync(ArchidektDeckCreateRequest request,
        ArchidektOperationBudget budget, CancellationToken cancellationToken)
    {
        return context.CreateDeckAsync(request, budget, cancellationToken);
    }

    /// <summary>Deletes one remote deck.</summary>
    internal Task DeleteAsync(string deckId, ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return context.DeleteDeckAsync(deckId, budget, cancellationToken);
    }

    /// <summary>Sends one deck metadata patch.</summary>
    internal Task SendMetadataAsync(string deckId, object payload, ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return context.SendDeckMetadataAsync(deckId, payload, budget, cancellationToken);
    }

    /// <summary>Sends one category creation payload.</summary>
    internal Task SendCategoryCreateAsync(string deckId, object payload, ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return context.SendCategoryCreateAsync(deckId, payload, budget, cancellationToken);
    }

    /// <summary>Sends one category update payload.</summary>
    internal Task SendCategoryUpdateAsync(string categoryId, object payload, ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return context.SendCategoryUpdateAsync(categoryId, payload, budget, cancellationToken);
    }

    /// <summary>Sends one category deletion.</summary>
    internal Task SendCategoryDeleteAsync(string categoryId, ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return context.SendCategoryDeleteAsync(categoryId, budget, cancellationToken);
    }

    /// <summary>Sends one card mutation payload.</summary>
    internal Task SendCardMutationAsync(string deckId, object payload, ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return context.SendCardMutationAsync(deckId, payload, budget, cancellationToken);
    }

    /// <summary>Resolves one exact remote card identifier.</summary>
    internal Task<string> ResolveCardIdAsync(RemoteDeckEntry entry, ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return context.ResolveCardIdAsync(entry, budget, cancellationToken);
    }
}

/// <summary>
/// Owns Archidekt folder provider routes.
/// </summary>
internal sealed class ArchidektFolderTransport
{
    /// <summary>Stores the shared authenticated HTTP context.</summary>
    private readonly ArchidektTransportContext context;

    /// <summary>Creates folder transport operations around one shared context.</summary>
    internal ArchidektFolderTransport(ArchidektTransportContext context)
    {
        this.context = context;
    }

    /// <summary>Lists the complete folder tree.</summary>
    internal Task<RemoteFolderTree> ListAsync(ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return context.ListFoldersAsync(budget, cancellationToken);
    }

    /// <summary>Gets one folder detail.</summary>
    internal Task<RemoteFolderTree> GetAsync(string folderId, ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return context.GetFolderAsync(folderId, budget, cancellationToken);
    }

    /// <summary>Creates one folder.</summary>
    internal Task<string> CreateAsync(ArchidektFolderCreateRequest request,
        ArchidektOperationBudget budget, CancellationToken cancellationToken)
    {
        return context.CreateFolderAsync(request, budget, cancellationToken);
    }

    /// <summary>Sends one folder update.</summary>
    internal Task SendUpdateAsync(string folderId, object payload, ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return context.SendFolderUpdateAsync(folderId, payload, budget, cancellationToken);
    }

    /// <summary>Sends one folder move.</summary>
    internal Task SendMoveAsync(object payload, ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return context.SendFolderMoveAsync(payload, budget, cancellationToken);
    }

    /// <summary>Sends one empty-folder deletion.</summary>
    internal Task SendDeleteAsync(object payload, ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return context.SendFolderDeleteAsync(payload, budget, cancellationToken);
    }
}

/// <summary>
/// Owns Archidekt named snapshot provider routes.
/// </summary>
internal sealed class ArchidektSnapshotTransport
{
    /// <summary>Stores the shared authenticated HTTP context.</summary>
    private readonly ArchidektTransportContext context;

    /// <summary>Creates snapshot transport operations around one shared context.</summary>
    internal ArchidektSnapshotTransport(ArchidektTransportContext context)
    {
        this.context = context;
    }

    /// <summary>Lists named snapshots for one deck.</summary>
    internal Task<RemoteNamedSnapshotPage> ListAsync(string deckId, ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return context.ListSnapshotsAsync(deckId, budget, cancellationToken);
    }

    /// <summary>Gets one complete named snapshot.</summary>
    internal Task<RemoteNamedSnapshot> GetAsync(string deckId, string snapshotId,
        ArchidektOperationBudget budget, CancellationToken cancellationToken)
    {
        return context.GetSnapshotAsync(deckId, snapshotId, budget, cancellationToken);
    }

    /// <summary>Sends one snapshot creation.</summary>
    internal Task SendCreateAsync(string deckId, object payload, ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return context.SendSnapshotCreateAsync(deckId, payload, budget, cancellationToken);
    }

    /// <summary>Sends one snapshot metadata update.</summary>
    internal Task SendUpdateAsync(string snapshotId, object payload, ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return context.SendSnapshotUpdateAsync(snapshotId, payload, budget, cancellationToken);
    }

    /// <summary>Sends one snapshot deletion.</summary>
    internal Task SendDeleteAsync(string snapshotId, ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return context.SendSnapshotDeleteAsync(snapshotId, budget, cancellationToken);
    }
}
