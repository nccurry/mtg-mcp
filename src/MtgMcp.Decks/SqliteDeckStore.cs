using System.Globalization;
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
        database = new DeckDatabase(Path.Combine(Path.GetFullPath(dataRoot), "decks.db"));
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
    /// Lists one stable page of canonically ordered decks without creating absent storage.
    /// </summary>
    public async Task<OperationResult<DeckPage>> ListAsync(
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (pageSize is < 1 or > 100)
        {
            return Invalid<DeckPage>("Page size must be between 1 and 100.");
        }

        if (!TryParseCursor(cursor, out int offset))
        {
            return Invalid<DeckPage>("The deck cursor is invalid.");
        }

        if (!database.Exists)
        {
            return new OperationSuccess<DeckPage>(new DeckPage([], null));
        }

        try
        {
            await using SqliteConnection connection = await database.OpenConnectionAsync(
                writable: false,
                cancellationToken).ConfigureAwait(false);
            IReadOnlyList<DeckSummary> rows = await DeckSql.ListDecksAsync(
                connection,
                offset,
                pageSize + 1,
                cancellationToken).ConfigureAwait(false);
            bool hasMore = rows.Count > pageSize;
            IReadOnlyList<DeckSummary> items = hasMore ? rows.Take(pageSize).ToArray() : rows;
            string? nextCursor = hasMore
                ? (offset + pageSize).ToString(CultureInfo.InvariantCulture)
                : null;
            return new OperationSuccess<DeckPage>(new DeckPage(items, nextCursor));
        }
        catch (Exception exception) when (IsExpectedStorageFailure(exception))
        {
            return MapFailure<DeckPage>(exception);
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
            return Invalid<DeckDocument>("The deck ID is invalid.");
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
            return MapFailure<DeckDocument>(exception);
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
            ValidateInitialRelationships(deck);
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
            return MapFailure<DeckDocument>(exception);
        }
        finally
        {
            mutationGate.Release();
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
            return Invalid<DeckInterchangeSnapshot>("The deck ID is invalid.");
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
            return MapFailure<DeckInterchangeSnapshot>(exception);
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
            ValidateInitialRelationships(deck);
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
            return MapFailure<DeckDocument>(exception);
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
        if (deckId == Guid.Empty || expectedRevision <= 0)
        {
            return Invalid<DeckDeleteResult>("A valid deck ID and positive expected revision are required.");
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
                    return ForwardFailure<DeckDeleteResult, long>(revision);
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
            return MapFailure<DeckDeleteResult>(exception);
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
        if (deckId == Guid.Empty || expectedRevision <= 0)
        {
            return Invalid<DeckDocument>("A valid deck ID and positive expected revision are required.");
        }

        if (changes is null || changes.Count == 0)
        {
            return Invalid<DeckDocument>("At least one deck change is required.");
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
                    return ForwardFailure<DeckDocument, long>(revision);
                }

                foreach (DeckChange change in changes)
                {
                    await ApplyChangeAsync(
                        connection,
                        transaction,
                        deckId,
                        change,
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
            return MapFailure<DeckDocument>(exception);
        }
        finally
        {
            mutationGate.Release();
        }
    }

    /// <summary>
    /// Reports local referential and Commander fixture structure without legality inference.
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
                await DeckSql.InsertEntryAsync(
                    connection,
                    transaction,
                    deckId,
                    DeckContractValidator.Normalize(value.Entry, createId),
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
        DeckEntry[] entries = (request.Entries ?? [])
            .Select(value => DeckContractValidator.Normalize(value, createId))
            .ToArray();
        DeckCategory[] categories = (request.Categories ?? [])
            .Select(value => DeckContractValidator.Normalize(value, createId))
            .ToArray();
        DeckProviderBinding[] bindings = (request.ProviderBindings ?? [])
            .Select(value => DeckContractValidator.Normalize(value, createId))
            .ToArray();
        DeckCategoryAssignment[] assignments = (request.CategoryAssignments ?? []).ToArray();
        return new DeckDocument(
            DeckContractValidator.NormalizeId(request.DeckId ?? createId(), "deck"),
            DeckContractValidator.Required(request.Name, "Deck name"),
            DeckContractValidator.Optional(request.Description) ?? string.Empty,
            DeckContractValidator.Required(request.Format, "Format").ToLowerInvariant(),
            1,
            now,
            now,
            entries,
            categories,
            assignments,
            bindings);
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
    /// Rejects initial relationships that do not reference rows in the same request.
    /// </summary>
    private static void ValidateInitialRelationships(DeckDocument deck)
    {
        HashSet<Guid> entryIds = deck.Entries.Select(value => value.EntryId).ToHashSet();
        HashSet<Guid> categoryIds = deck.Categories.Select(value => value.CategoryId).ToHashSet();
        HashSet<Guid> primaryEntries = [];
        foreach (DeckCategoryAssignment assignment in deck.CategoryAssignments)
        {
            if (!entryIds.Contains(assignment.EntryId) || !categoryIds.Contains(assignment.CategoryId))
            {
                throw new DeckInputException("An initial category assignment has an unknown reference.");
            }

            if (assignment.IsPrimary && !primaryEntries.Add(assignment.EntryId))
            {
                throw new DeckInputException("An entry may have only one primary category.");
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
                "stale-deck-revision",
                "The deck changed after the supplied revision.");
        }

        return new OperationSuccess<long>(current.Value);
    }

    /// <summary>
    /// Parses an opaque offset cursor without accepting negative or decorated values.
    /// </summary>
    private static bool TryParseCursor(string? value, out int offset)
    {
        if (value is null)
        {
            offset = 0;
            return true;
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out offset) &&
            offset >= 0;
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
    /// Maps bounded persistence failures without including paths or rejected values.
    /// </summary>
    private static OperationResult<T> MapFailure<T>(Exception exception)
    {
        return exception switch
        {
            DeckInputException value => Invalid<T>(value.Message),
            DeckEntityNotFoundException value => new OperationNotFound(
                value.ReasonCode,
                value.Message),
            InvalidDataException => new OperationUnsupported(
                "unsupported-deck-schema",
                "The local deck database uses an unsupported schema."),
            SqliteException { SqliteErrorCode: 5 or 6 } => new OperationUnavailable(
                "deck-database-busy",
                "The local deck database is busy."),
            SqliteException { SqliteErrorCode: 19 } => Invalid<T>(
                "The requested deck change violates a local constraint."),
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
    /// Creates a stable invalid-input outcome.
    /// </summary>
    private static OperationResult<T> Invalid<T>(string message)
    {
        return new OperationInvalidInput("invalid-deck-input", message);
    }

    /// <summary>
    /// Creates a stable deck-not-found outcome.
    /// </summary>
    private static OperationResult<T> NotFound<T>()
    {
        return new OperationNotFound("deck-not-found", "The local deck was not found.");
    }

    /// <summary>
    /// Forwards a closed failure union while rejecting an unexpected success.
    /// </summary>
    private static OperationResult<TTarget> ForwardFailure<TTarget, TSource>(
        OperationResult<TSource> result)
    {
        return result switch
        {
            OperationSuccess<TSource> => new OperationUnavailable(
                "deck-operation-failed",
                "The local deck operation could not be completed."),
            OperationNotFound value => value,
            OperationNotCached value => value,
            OperationUnsupported value => value,
            OperationUnavailable value => value,
            OperationConflict value => value,
            OperationInvalidInput value => value,
        };
    }
}
