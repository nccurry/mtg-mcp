using Microsoft.Data.Sqlite;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;

namespace MtgMcp.Decks;

/// <summary>
/// Owns revisioned local deck transactions over the isolated decks database.
/// </summary>
public sealed class SqliteDeckStore : IDisposable
{
    /// <summary>
    /// Serializes in-process mutations and byte-level backup operations.
    /// </summary>
    private readonly SemaphoreSlim mutationGate = new(1, 1);

    /// <summary>
    /// Owns connection and migration policy for the private database path.
    /// </summary>
    private readonly DeckDatabase database;

    /// <summary>
    /// Supplies controllable timestamps for persistence and tests.
    /// </summary>
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// Supplies stable identifiers, using UUID version 7 by default.
    /// </summary>
    private readonly Func<Guid> createId;

    /// <summary>
    /// Identifies the application version recorded with schema migrations.
    /// </summary>
    private readonly string applicationVersion;

    /// <summary>
    /// Creates a store beneath one resolved application data root.
    /// </summary>
    public SqliteDeckStore(
        string dataRoot,
        string applicationVersion,
        TimeProvider? timeProvider = null,
        Func<Guid>? createId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationVersion);
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.createId = createId ?? (() => Guid.CreateVersion7(this.timeProvider.GetUtcNow()));
        this.applicationVersion = applicationVersion.Trim();
        database = new DeckDatabase(
            Path.Combine(Path.GetFullPath(dataRoot), "decks.db"),
            this.timeProvider,
            this.applicationVersion);
        Backups = new DeckBackupStore(
            database,
            mutationGate,
            this.timeProvider,
            this.createId,
            this.applicationVersion);
    }

    /// <summary>
    /// Gets the backup lifecycle bound to the same database mutation gate.
    /// </summary>
    public DeckBackupStore Backups { get; }

    /// <summary>
    /// Lists one page of canonically ordered decks, from a row offset, without creating absent storage. The slice
    /// says whether decks follow it.
    /// </summary>
    public Task<OperationResult<DeckListSlice>> ListAsync(
        int offset,
        int pageSize,
        CancellationToken cancellationToken)
    {
        return ListAsync(_ => new OperationSuccess<int>(offset), pageSize, cancellationToken);
    }

    /// <summary>
    /// Lists one page of canonically ordered decks from the offset that <paramref name="resolveOffset"/> returns for
    /// the fingerprint of the list, which changes when any deck is created, deleted, renamed, or edited. The
    /// fingerprint and the page come from one read transaction, so a write cannot fall between them. The slice says
    /// whether decks follow it and carries that fingerprint.
    /// </summary>
    public async Task<OperationResult<DeckListSlice>> ListAsync(
        Func<string, OperationResult<int>> resolveOffset,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolveOffset);
        if (pageSize is < 1 or > 100)
        {
            return Invalid<DeckListSlice>("Page size must be between 1 and 100.", "pageSize");
        }

        if (!database.Exists)
        {
            string empty = DeckSql.ListFingerprint([]);
            OperationResult<int> first = resolveOffset(empty);
            return first is OperationSuccess<int>
                ? new OperationSuccess<DeckListSlice>(new DeckListSlice([], false, empty))
                : OperationFailure.Forward<int, DeckListSlice>(first);
        }

        try
        {
            await using SqliteConnection connection = await database.OpenConnectionAsync(
                writable: false,
                cancellationToken).ConfigureAwait(false);
            await using SqliteTransaction transaction = connection.BeginTransaction(deferred: true);
            string fingerprint = DeckSql.ListFingerprint(
                await DeckSql.ListRevisionsAsync(connection, cancellationToken).ConfigureAwait(false));
            OperationResult<int> start = resolveOffset(fingerprint);
            if (start is not OperationSuccess<int> offset)
            {
                return OperationFailure.Forward<int, DeckListSlice>(start);
            }

            IReadOnlyList<DeckSummary> rows = await DeckSql.ListDecksAsync(
                connection,
                offset.Data,
                pageSize + 1,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            bool hasMore = rows.Count > pageSize;
            return new OperationSuccess<DeckListSlice>(
                new DeckListSlice(hasMore ? rows.Take(pageSize).ToArray() : rows, hasMore, fingerprint));
        }
        catch (Exception exception) when (IsExpectedStorageFailure(exception))
        {
            return MapFailure<DeckListSlice>(exception, "pageSize");
        }
    }
    /// <summary>
    /// Gets one complete deck without creating absent storage.
    /// </summary>
    public async Task<OperationResult<DeckDocument>> GetAsync(
        Guid deckId,
        CancellationToken cancellationToken)
    {
        if (deckId == Guid.Empty)
        {
            return Invalid<DeckDocument>("The deck ID is invalid.", "deckId");
        }

        if (!database.Exists)
        {
            return NotFound<DeckDocument>();
        }

        try
        {
            await using SqliteConnection connection = await database.OpenConnectionAsync(
                writable: false,
                cancellationToken).ConfigureAwait(false);
            DeckDocument? deck = await DeckSql.ReadDeckAsync(
                connection,
                transaction: null,
                deckId,
                cancellationToken).ConfigureAwait(false);
            return deck is null
                ? NotFound<DeckDocument>()
                : new OperationSuccess<DeckDocument>(deck);
        }
        catch (Exception exception) when (IsExpectedStorageFailure(exception))
        {
            return MapFailure<DeckDocument>(exception, "deckId");
        }
    }

    /// <summary>
    /// Creates one deck and its explicit initial graph in a single transaction.
    /// </summary>
    public async Task<OperationResult<DeckDocument>> CreateAsync(
        DeckCreateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = timeProvider.GetUtcNow().ToUniversalTime();
            await database.EnsureInitializedAsync(now, applicationVersion, cancellationToken)
                .ConfigureAwait(false);
            DeckDocument deck = BuildNewDeck(request, now);
            ValidateInitialRelationships(deck, "categoryAssignments");
            await using SqliteConnection connection = await database.OpenConnectionAsync(
                writable: true,
                cancellationToken).ConfigureAwait(false);
            await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
            try
            {
                await DeckSql.InsertDeckAsync(
                    connection,
                    transaction,
                    deck,
                    new Dictionary<Guid, string?>(),
                    cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return new OperationSuccess<DeckDocument>(deck);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }
        catch (Exception exception) when (IsExpectedStorageFailure(exception))
        {
            return MapFailure<DeckDocument>(exception, "request");
        }
        finally
        {
            mutationGate.Release();
        }
    }

    /// <summary>
    /// Creates one deck and its initial provider synchronization baseline in a single transaction.
    /// </summary>
    public async Task<OperationResult<DeckDocument>> CreateSynchronizedAsync(
        DeckCreateRequest request,
        DeckSyncBaseline baseline,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(baseline);
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = timeProvider.GetUtcNow().ToUniversalTime();
            await database.EnsureInitializedAsync(now, applicationVersion, cancellationToken)
                .ConfigureAwait(false);
            DeckDocument deck = BuildNewDeck(request, now);
            ValidateInitialRelationships(deck, "categoryAssignments");
            if (!deck.ProviderBindings.Any(value => value.BindingId == baseline.BindingId))
            {
                return Invalid<DeckDocument>(
                    "The synchronization baseline has no matching provider binding.",
                    "localDeckId");
            }

            string canonicalBaseline = DeckContractValidator.Required(
                baseline.CanonicalSnapshot,
                "Canonical baseline");
            await using SqliteConnection connection = await database.OpenConnectionAsync(
                writable: true,
                cancellationToken).ConfigureAwait(false);
            await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
            try
            {
                await DeckSql.InsertDeckAsync(
                    connection,
                    transaction,
                    deck,
                    new Dictionary<Guid, string?>
                    {
                        [baseline.BindingId] = canonicalBaseline,
                    },
                    cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return new OperationSuccess<DeckDocument>(deck);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }
        catch (Exception exception) when (IsExpectedStorageFailure(exception))
        {
            return MapFailure<DeckDocument>(exception, "request");
        }
        finally
        {
            mutationGate.Release();
        }
    }

    /// <summary>
    /// Gets one provider synchronization baseline without exposing any filesystem path.
    /// </summary>
    public async Task<OperationResult<DeckSyncBaseline>> GetSyncBaselineAsync(
        Guid deckId,
        Guid bindingId,
        CancellationToken cancellationToken)
    {
        if (deckId == Guid.Empty || bindingId == Guid.Empty)
        {
            return Invalid<DeckSyncBaseline>("Valid deck and binding IDs are required.", "localDeckId");
        }

        if (!database.Exists)
        {
            return NotFound<DeckSyncBaseline>();
        }

        try
        {
            await using SqliteConnection connection = await database.OpenConnectionAsync(
                writable: false,
                cancellationToken).ConfigureAwait(false);
            IReadOnlyList<DeckSyncBaseline> baselines = await DeckSql.ReadBaselinesAsync(
                connection,
                transaction: null,
                deckId,
                cancellationToken).ConfigureAwait(false);
            DeckSyncBaseline? baseline = baselines.FirstOrDefault(value => value.BindingId == bindingId);
            return baseline is null
                ? new OperationNotFound(
                    "deck-sync-baseline-not-found",
                    "The provider synchronization baseline was not found.")
                : new OperationSuccess<DeckSyncBaseline>(baseline);
        }
        catch (Exception exception) when (IsExpectedStorageFailure(exception))
        {
            return MapFailure<DeckSyncBaseline>(exception, "localDeckId");
        }
    }

    /// <summary>
    /// Reads a lossless interchange snapshot without exposing its private storage path.
    /// </summary>
    internal async Task<OperationResult<DeckInterchangeSnapshot>> GetInterchangeSnapshotAsync(
        Guid deckId,
        CancellationToken cancellationToken)
    {
        if (deckId == Guid.Empty)
        {
            return Invalid<DeckInterchangeSnapshot>("The deck ID is invalid.", "deckId");
        }

        if (!database.Exists)
        {
            return NotFound<DeckInterchangeSnapshot>();
        }

        try
        {
            await using SqliteConnection connection = await database.OpenConnectionAsync(
                writable: false,
                cancellationToken).ConfigureAwait(false);
            await using SqliteTransaction transaction = connection.BeginTransaction(deferred: true);
            DeckDocument? deck = await DeckSql.ReadDeckAsync(
                connection,
                transaction,
                deckId,
                cancellationToken).ConfigureAwait(false);
            if (deck is null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return NotFound<DeckInterchangeSnapshot>();
            }

            IReadOnlyList<DeckSyncBaseline> baselines = await DeckSql.ReadBaselinesAsync(
                connection,
                transaction,
                deckId,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new OperationSuccess<DeckInterchangeSnapshot>(new(deck, baselines));
        }
        catch (Exception exception) when (IsExpectedStorageFailure(exception))
        {
            return MapFailure<DeckInterchangeSnapshot>(exception, "deckId");
        }
    }

    /// <summary>
    /// Creates an exact native interchange graph, preserving lifecycle metadata and stable identities.
    /// </summary>
    internal async Task<OperationResult<DeckDocument>> CreateExactAsync(
        DeckInterchangeSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DeckDocument deck = NormalizeExact(snapshot.Deck);
            ValidateInitialRelationships(deck, null);
            ValidateBaselines(deck, snapshot.SyncBaselines);
            await database.EnsureInitializedAsync(
                timeProvider.GetUtcNow().ToUniversalTime(),
                applicationVersion,
                cancellationToken).ConfigureAwait(false);
            await using SqliteConnection connection = await database.OpenConnectionAsync(
                writable: true,
                cancellationToken).ConfigureAwait(false);
            await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
            try
            {
                Dictionary<Guid, string?> baselines = snapshot.SyncBaselines.ToDictionary(
                    value => value.BindingId,
                    value => (string?)DeckContractValidator.Required(
                        value.CanonicalSnapshot,
                        "Canonical baseline"));
                await DeckSql.InsertDeckAsync(
                    connection,
                    transaction,
                    deck,
                    baselines,
                    cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return new OperationSuccess<DeckDocument>(deck);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }
        catch (Exception exception) when (IsExpectedStorageFailure(exception))
        {
            return MapFailure<DeckDocument>(exception, "content");
        }
        finally
        {
            mutationGate.Release();
        }
    }

    /// <summary>
    /// Deletes one deck only when the caller supplies its current revision.
    /// </summary>
    public async Task<OperationResult<DeckDeleteResult>> DeleteAsync(
        Guid deckId,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        if (deckId == Guid.Empty)
        {
            return Invalid<DeckDeleteResult>("The deck ID is invalid.", "deckId");
        }

        if (expectedRevision <= 0)
        {
            return Invalid<DeckDeleteResult>("The expected revision must be positive.", "expectedRevision");
        }

        if (!database.Exists)
        {
            return NotFound<DeckDeleteResult>();
        }

        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteConnection connection = await database.OpenConnectionAsync(
                writable: true,
                cancellationToken).ConfigureAwait(false);
            await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
            try
            {
                OperationResult<long> revision = await RequireRevisionAsync(
                    connection,
                    transaction,
                    deckId,
                    expectedRevision,
                    cancellationToken).ConfigureAwait(false);
                if (revision is not OperationSuccess<long>)
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    return OperationFailure.Forward<long, DeckDeleteResult>(revision);
                }

                await DeckSql.DeleteDeckAsync(connection, transaction, deckId, cancellationToken)
                    .ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return new OperationSuccess<DeckDeleteResult>(
                    new DeckDeleteResult(deckId, expectedRevision + 1));
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }
        catch (Exception exception) when (IsExpectedStorageFailure(exception))
        {
            return MapFailure<DeckDeleteResult>(exception, "deckId");
        }
        finally
        {
            mutationGate.Release();
        }
    }

    /// <summary>
    /// Applies explicit changes in caller order and increments the revision once on commit.
    /// </summary>
    public async Task<OperationResult<DeckDocument>> ApplyChangesAsync(
        Guid deckId,
        long expectedRevision,
        IReadOnlyList<DeckChange> changes,
        CancellationToken cancellationToken)
    {
        if (deckId == Guid.Empty)
        {
            return Invalid<DeckDocument>("The deck ID is invalid.", "deckId");
        }

        if (expectedRevision <= 0)
        {
            return Invalid<DeckDocument>("The expected revision must be positive.", "expectedRevision");
        }

        if (changes is null || changes.Count == 0)
        {
            return Invalid<DeckDocument>("At least one deck change is required.", "changes");
        }

        if (!database.Exists)
        {
            return NotFound<DeckDocument>();
        }

        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteConnection connection = await database.OpenConnectionAsync(
                writable: true,
                cancellationToken).ConfigureAwait(false);
            await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
            try
            {
                OperationResult<long> revision = await RequireRevisionAsync(
                    connection,
                    transaction,
                    deckId,
                    expectedRevision,
                    cancellationToken).ConfigureAwait(false);
                if (revision is not OperationSuccess<long>)
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    return OperationFailure.Forward<long, DeckDocument>(revision);
                }

                Dictionary<Guid, Guid> primaryAssigned = [];
                for (int index = 0; index < changes.Count; index++)
                {
                    await ApplyIndexedChangeAsync(
                        connection,
                        transaction,
                        deckId,
                        changes[index],
                        index,
                        primaryAssigned,
                        cancellationToken).ConfigureAwait(false);
                }

                long nextRevision = expectedRevision + 1;
                await DeckSql.UpdateRevisionAsync(
                    connection,
                    transaction,
                    deckId,
                    nextRevision,
                    timeProvider.GetUtcNow(),
                    cancellationToken).ConfigureAwait(false);
                DeckDocument updated = await DeckSql.ReadDeckAsync(
                    connection,
                    transaction,
                    deckId,
                    cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidDataException("The updated deck could not be read.");
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return new OperationSuccess<DeckDocument>(updated);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }
        catch (Exception exception) when (IsExpectedStorageFailure(exception))
        {
            return MapFailure<DeckDocument>(exception, "changes");
        }
        finally
        {
            mutationGate.Release();
        }
    }

    /// <summary>
    /// Reports local referential structure without format or legality inference.
    /// </summary>
    public async Task<OperationResult<DeckValidationReport>> ValidateAsync(
        Guid deckId,
        CancellationToken cancellationToken)
    {
        OperationResult<DeckDocument> result = await GetAsync(deckId, cancellationToken)
            .ConfigureAwait(false);
        return result switch
        {
            OperationSuccess<DeckDocument> success =>
                new OperationSuccess<DeckValidationReport>(DeckValidator.Validate(success.Data)),
            OperationNotFound value => value,
            OperationNotCached value => value,
            OperationUnsupported value => value,
            OperationUnavailable value => value,
            OperationConflict value => value,
            OperationInvalidInput value => value,
        };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        mutationGate.Dispose();
    }

    /// <summary>
    /// Applies one change of a batch, naming the change as <c>changes[index]</c> when it is refused without a path.
    /// </summary>
    private async Task ApplyIndexedChangeAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid deckId,
        DeckChange change,
        int index,
        Dictionary<Guid, Guid> primaryAssigned,
        CancellationToken cancellationToken)
    {
        try
        {
            RejectSecondPrimaryInBatch(change, primaryAssigned);
            await ApplyChangeAsync(connection, transaction, deckId, change, cancellationToken).ConfigureAwait(false);
        }
        catch (DeckInputException exception) when (exception.Path is null)
        {
            throw exception.At($"changes[{index}]");
        }
    }
    /// <summary>
    /// Refuses a batch that makes two different categories primary for one entry at the same time. A single primary
    /// assignment demotes the entry's earlier primary category, so only a conflict inside one request is an error.
    /// The primary category of an added entry and of a patch counts as an assignment made by the request.
    /// The entry forgets its remembered primary when the batch unassigns that category, assigns it as non-primary,
    /// removes the category, or removes the entry.
    /// </summary>
    private static void RejectSecondPrimaryInBatch(DeckChange change, Dictionary<Guid, Guid> primaryAssigned)
    {
        switch (change)
        {
            case AssignDeckCategoryChange { IsPrimary: true } assignment:
                if (primaryAssigned.TryGetValue(assignment.EntryId, out Guid current) &&
                    current != assignment.CategoryId)
                {
                    throw DeckInputException.Refusal("An entry may have only one primary category.");
                }

                primaryAssigned[assignment.EntryId] = assignment.CategoryId;
                break;
            case AssignDeckCategoryChange assignment:
                ForgetPrimary(primaryAssigned, assignment.EntryId, assignment.CategoryId);
                break;
            case UnassignDeckCategoryChange unassignment:
                ForgetPrimary(primaryAssigned, unassignment.EntryId, unassignment.CategoryId);
                break;
            case RemoveDeckCategoryChange removal:
                foreach (Guid entryId in primaryAssigned.Where(value => value.Value == removal.CategoryId)
                    .Select(value => value.Key).ToArray())
                {
                    primaryAssigned.Remove(entryId);
                }

                break;
            case RemoveDeckEntryChange removal:
                primaryAssigned.Remove(removal.EntryId);
                break;
            case AddDeckEntryChange { Entry: { EntryId: Guid addedId, PrimaryCategoryId: Guid addedPrimary } }:
                primaryAssigned[addedId] = addedPrimary;
                break;
            case PatchDeckEntriesChange patch:
                foreach (Guid entryId in patch.EntryIds)
                {
                    if (patch.PrimaryCategoryId is Guid patchPrimary)
                    {
                        primaryAssigned[entryId] = patchPrimary;
                    }
                    else
                    {
                        primaryAssigned.Remove(entryId);
                    }
                }

                break;
        }
    }

    /// <summary>
    /// Drops the remembered primary category of an entry when it is the given category.
    /// </summary>
    private static void ForgetPrimary(Dictionary<Guid, Guid> primaryAssigned, Guid entryId, Guid categoryId)
    {
        if (primaryAssigned.TryGetValue(entryId, out Guid current) && current == categoryId)
        {
            primaryAssigned.Remove(entryId);
        }
    }

    /// <summary>
    /// Applies one closed change case through the shared transaction.
    /// </summary>
    private async Task ApplyChangeAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid deckId,
        DeckChange change,
        CancellationToken cancellationToken)
    {
        switch (change)
        {
            case UpdateDeckMetadataChange value:
                await DeckSql.UpdateMetadataAsync(
                    connection, transaction, deckId, value, cancellationToken).ConfigureAwait(false);
                break;
            case AddDeckEntryChange value:
                DeckNewEntry added = DeckContractValidator.Normalize(value.Entry, createId);
                await DeckSql.InsertEntryAsync(
                    connection,
                    transaction,
                    deckId,
                    added.Entry,
                    cancellationToken).ConfigureAwait(false);
                if (added.HasCategories)
                {
                    await DeckDraftCategories.ApplyAsync(
                        connection,
                        transaction,
                        deckId,
                        added,
                        createId,
                        cancellationToken).ConfigureAwait(false);
                }

                break;
            case PatchDeckEntriesChange value:
                await DeckEntryPatcher.ApplyAsync(
                    connection,
                    transaction,
                    deckId,
                    value,
                    createId,
                    cancellationToken).ConfigureAwait(false);
                break;
            case UpdateDeckEntryChange value:
                await DeckSql.UpdateEntryAsync(
                    connection,
                    transaction,
                    deckId,
                    DeckContractValidator.Normalize(value.Entry),
                    cancellationToken).ConfigureAwait(false);
                break;
            case RemoveDeckEntryChange value:
                await DeckSql.RemoveEntryAsync(
                    connection, transaction, deckId, value.EntryId, cancellationToken).ConfigureAwait(false);
                break;
            case AddDeckCategoryChange value:
                await DeckSql.InsertCategoryAsync(
                    connection,
                    transaction,
                    deckId,
                    DeckContractValidator.Normalize(value.Category, createId),
                    cancellationToken).ConfigureAwait(false);
                break;
            case UpdateDeckCategoryChange value:
                await DeckSql.UpdateCategoryAsync(
                    connection,
                    transaction,
                    deckId,
                    DeckContractValidator.Normalize(value.Category),
                    cancellationToken).ConfigureAwait(false);
                break;
            case RemoveDeckCategoryChange value:
                await DeckSql.RemoveCategoryAsync(
                    connection,
                    transaction,
                    deckId,
                    value.CategoryId,
                    cancellationToken).ConfigureAwait(false);
                break;
            case AssignDeckCategoryChange value:
                await DeckSql.AssignCategoryAsync(
                    connection,
                    transaction,
                    deckId,
                    new DeckCategoryAssignment(value.EntryId, value.CategoryId, value.IsPrimary),
                    cancellationToken).ConfigureAwait(false);
                break;
            case UnassignDeckCategoryChange value:
                await DeckSql.UnassignCategoryAsync(
                    connection,
                    transaction,
                    deckId,
                    value.EntryId,
                    value.CategoryId,
                    cancellationToken).ConfigureAwait(false);
                break;
            case UpsertDeckProviderBindingChange value:
                await DeckSql.UpsertProviderBindingAsync(
                    connection,
                    transaction,
                    deckId,
                    DeckContractValidator.Normalize(value.Binding, createId),
                    DeckContractValidator.Optional(value.CanonicalBaseline),
                    cancellationToken).ConfigureAwait(false);
                break;
            case RemoveDeckProviderBindingChange value:
                await DeckSql.RemoveProviderBindingAsync(
                    connection,
                    transaction,
                    deckId,
                    value.BindingId,
                    cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    /// <summary>
    /// Builds a normalized immutable graph for one new deck request.
    /// </summary>
    private DeckDocument BuildNewDeck(DeckCreateRequest request, DateTimeOffset now)
    {
        DeckNewEntry[] drafted = NormalizeEach(
            request.Entries,
            "entries",
            value => DeckContractValidator.Normalize(value, createId));
        List<DeckCategory> categories = [.. NormalizeEach(
            request.Categories,
            "categories",
            value => DeckContractValidator.Normalize(value, createId))];
        DeckProviderBinding[] bindings = NormalizeEach(
            request.ProviderBindings,
            "providerBindings",
            value => DeckContractValidator.Normalize(value, createId));
        List<DeckCategoryAssignment> assignments = [.. request.CategoryAssignments ?? []];
        for (int index = 0; index < drafted.Length; index++)
        {
            DeckNewEntry added = drafted[index];
            if (added.HasCategories)
            {
                (IReadOnlyList<DeckCategoryAssignment> planned, DeckCategory? created) = WithPath(
                    $"entries[{index}]",
                    () => DeckDraftCategories.Plan(added, categories, createId));
                assignments.AddRange(planned);
                if (created is not null)
                {
                    categories.Add(created);
                }
            }
        }

        DeckEntry[] entries = [.. drafted.Select(value => value.Entry)];
        return new DeckDocument(
            WithPath("deckId", () => DeckContractValidator.NormalizeId(request.DeckId ?? createId(), "deck")),
            WithPath("name", () => DeckContractValidator.Required(request.Name, "Deck name")),
            DeckContractValidator.Optional(request.Description) ?? string.Empty,
            WithPath("format", () => DeckContractValidator.Required(request.Format, "Format").ToLowerInvariant()),
            1,
            now,
            now,
            entries,
            categories,
            assignments,
            bindings);
    }

    /// <summary>
    /// Normalizes each item of one request collection, naming the item that fails as <c>collection[index]</c>.
    /// </summary>
    private static T[] NormalizeEach<TInput, T>(
        IReadOnlyList<TInput>? items,
        string collection,
        Func<TInput, T> normalize)
    {
        IReadOnlyList<TInput> source = items ?? [];
        T[] result = new T[source.Count];
        for (int index = 0; index < result.Length; index++)
        {
            TInput item = source[index];
            result[index] = WithPath($"{collection}[{index}]", () => normalize(item));
        }

        return result;
    }

    /// <summary>
    /// Runs a validation and names <paramref name="path"/> on a rejection that names no argument.
    /// </summary>
    private static T WithPath<T>(string path, Func<T> validate)
    {
        try
        {
            return validate();
        }
        catch (DeckInputException exception) when (exception.Path is null)
        {
            throw exception.At(path);
        }
    }

    /// <summary>
    /// Validates and normalizes all fields in a lossless native deck document.
    /// </summary>
    private DeckDocument NormalizeExact(DeckDocument value)
    {
        if (value.Revision <= 0)
        {
            throw new DeckInputException("A native deck revision must be positive.");
        }

        if (value.CreatedAtUtc > value.UpdatedAtUtc)
        {
            throw new DeckInputException("A native deck update cannot predate its creation.");
        }

        return new DeckDocument(
            DeckContractValidator.NormalizeId(value.DeckId, "deck"),
            DeckContractValidator.Required(value.Name, "Deck name"),
            DeckContractValidator.Optional(value.Description) ?? string.Empty,
            DeckContractValidator.Required(value.Format, "Format").ToLowerInvariant(),
            value.Revision,
            value.CreatedAtUtc.ToUniversalTime(),
            value.UpdatedAtUtc.ToUniversalTime(),
            value.Entries.Select(DeckContractValidator.Normalize).ToArray(),
            value.Categories.Select(DeckContractValidator.Normalize).ToArray(),
            value.CategoryAssignments.ToArray(),
            value.ProviderBindings.Select(NormalizeExactBinding).ToArray());
    }

    /// <summary>
    /// Normalizes a native binding while requiring its stable identity to be present.
    /// </summary>
    private DeckProviderBinding NormalizeExactBinding(DeckProviderBinding value)
    {
        DeckContractValidator.NormalizeId(value.BindingId, "binding");
        return DeckContractValidator.Normalize(value, createId);
    }

    /// <summary>
    /// Rejects duplicate or orphaned provider snapshots before an exact native insert.
    /// </summary>
    private static void ValidateBaselines(
        DeckDocument deck,
        IReadOnlyList<DeckSyncBaseline> baselines)
    {
        HashSet<Guid> bindingIds = deck.ProviderBindings.Select(value => value.BindingId).ToHashSet();
        HashSet<Guid> observed = [];
        foreach (DeckSyncBaseline baseline in baselines)
        {
            if (!bindingIds.Contains(baseline.BindingId) || !observed.Add(baseline.BindingId))
            {
                throw new DeckInputException("A native synchronization baseline is invalid.");
            }
        }
    }

    /// <summary>
    /// Rejects initial relationships that do not reference rows in the same request. A rejection names the assignment
    /// as <paramref name="assignmentsPath"/> and its index, when the caller gave a path.
    /// </summary>
    private static void ValidateInitialRelationships(DeckDocument deck, string? assignmentsPath)
    {
        HashSet<Guid> entryIds = deck.Entries.Select(value => value.EntryId).ToHashSet();
        HashSet<Guid> categoryIds = deck.Categories.Select(value => value.CategoryId).ToHashSet();
        HashSet<Guid> primaryEntries = [];
        for (int index = 0; index < deck.CategoryAssignments.Count; index++)
        {
            DeckCategoryAssignment assignment = deck.CategoryAssignments[index];
            string? path = assignmentsPath is null ? null : $"{assignmentsPath}[{index}]";
            if (!entryIds.Contains(assignment.EntryId) || !categoryIds.Contains(assignment.CategoryId))
            {
                throw new DeckInputException(
                    "An initial category assignment has an unknown reference.",
                    path: path);
            }

            if (assignment.IsPrimary && !primaryEntries.Add(assignment.EntryId))
            {
                throw new DeckInputException("An entry may have only one primary category.", path: path);
            }
        }
    }

    /// <summary>
    /// Verifies existence and optimistic revision inside the active transaction.
    /// </summary>
    private static async Task<OperationResult<long>> RequireRevisionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid deckId,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        long? current = await DeckSql.ReadRevisionAsync(
            connection,
            transaction,
            deckId,
            cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            return NotFound<long>();
        }

        if (current.Value != expectedRevision)
        {
            return new OperationConflict(
                SharedReasonCodes.StaleDeckRevision,
                "The deck changed after the supplied revision.",
                new ConflictState(Revision: current.Value));
        }

        return new OperationSuccess<long>(current.Value);
    }

    /// <summary>
    /// Identifies storage and caller-input exceptions that map to structured outcomes.
    /// </summary>
    private static bool IsExpectedStorageFailure(Exception exception)
    {
        return exception is DeckInputException or
            DeckEntityNotFoundException or
            InvalidDataException or
            IOException or
            UnauthorizedAccessException or
            SqliteException;
    }

    /// <summary>
    /// Maps bounded persistence failures without including filesystem paths or rejected values. An invalid-input
    /// failure names the exception's own argument path, or <paramref name="path"/> when the exception names none.
    /// </summary>
    private static OperationResult<T> MapFailure<T>(Exception exception, string path)
    {
        return exception switch
        {
            DeckInputException value => new OperationInvalidInput(value.ReasonCode, value.Message, value.Path ?? path),
            DeckEntityNotFoundException value => new OperationNotFound(
                value.ReasonCode,
                value.Message),
            InvalidDataException => new OperationUnsupported(
                "unsupported-deck-schema",
                "The local deck database uses an unsupported schema."),
            SqliteException { SqliteErrorCode: 5 or 6 } => new OperationUnavailable(
                "deck-database-busy",
                "The local deck database is busy."),
            SqliteException { SqliteErrorCode: 19 } constraint => Invalid<T>(DescribeConstraint(constraint), path),
            SqliteException => new OperationUnavailable(
                "deck-database-unavailable",
                "The local deck database is unavailable."),
            IOException or UnauthorizedAccessException => new OperationUnavailable(
                "deck-storage-unavailable",
                "Local deck storage is unavailable."),
            _ => new OperationUnavailable(
                "deck-storage-unavailable",
                "Local deck storage is unavailable."),
        };
    }

    /// <summary>
    /// Names the violated constraint in plain words, from the table and columns SQLite reports.
    /// </summary>
    internal static string DescribeConstraint(SqliteException exception)
    {
        string message = exception.Message;
        if (message.Contains("deck_categories.deck_id, deck_categories.name", StringComparison.Ordinal))
        {
            return "Two categories of one deck have the same name; category names are unique per deck.";
        }

        if (message.Contains("deck_entries.entry_id", StringComparison.Ordinal))
        {
            return "An entry ID is already used by an entry of a local deck; entry IDs are unique across local decks.";
        }

        if (message.Contains("provider_bindings", StringComparison.Ordinal))
        {
            return "The deck already has a binding to this provider deck.";
        }

        if (message.Contains("deck_entry_categories", StringComparison.Ordinal))
        {
            return "An entry has more than one primary category or repeats a category.";
        }

        int index = message.IndexOf("failed: ", StringComparison.Ordinal);
        string detail = index < 0 ? string.Empty : " (" + message[(index + 8)..].Trim() + ")";
        return "The requested deck change violates a local constraint" + detail + ".";
    }

    /// <summary>
    /// Creates a stable invalid-input outcome.
    /// </summary>
    private static OperationResult<T> Invalid<T>(string message, string path)
    {
        return new OperationInvalidInput(DeckReasonCodes.InvalidDeckInput, message, path);
    }

    /// <summary>
    /// Creates a stable deck-not-found outcome.
    /// </summary>
    private static OperationResult<T> NotFound<T>()
    {
        return new OperationNotFound("deck-not-found", "The local deck was not found.");
    }
}
