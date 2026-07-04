using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;

namespace MtgMcp.Decks;

/// <summary>
/// Owns opaque backup creation, inventory, guarded restore, and deletion.
/// </summary>
public sealed class DeckBackupStore
{
    /// <summary>
    /// Serializes the versioned manifest format deterministically.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Owns database access and schema inspection.
    /// </summary>
    private readonly DeckDatabase database;

    /// <summary>
    /// Coordinates backup bytes with ordinary deck mutations.
    /// </summary>
    private readonly SemaphoreSlim mutationGate;

    /// <summary>
    /// Supplies controllable manifest timestamps.
    /// </summary>
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// Supplies opaque backup identifiers.
    /// </summary>
    private readonly Func<Guid> createId;

    /// <summary>
    /// Identifies the application version used when an empty store is initialized.
    /// </summary>
    private readonly string applicationVersion;

    /// <summary>
    /// Creates a backup boundary sharing its owning deck store's mutation lock.
    /// </summary>
    internal DeckBackupStore(
        DeckDatabase database,
        SemaphoreSlim mutationGate,
        TimeProvider timeProvider,
        Func<Guid> createId,
        string applicationVersion)
    {
        this.database = database;
        this.mutationGate = mutationGate;
        this.timeProvider = timeProvider;
        this.createId = createId;
        this.applicationVersion = applicationVersion;
    }

    /// <summary>
    /// Lists verified manifests and the current conservative database fingerprint.
    /// </summary>
    public async Task<OperationResult<DeckBackupPage>> ListAsync(CancellationToken cancellationToken)
    {
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string? currentFingerprint = database.Exists
                ? await ComputeCurrentFingerprintAsync(cancellationToken).ConfigureAwait(false)
                : null;
            string backupDirectory = GetBackupDirectory();
            if (!Directory.Exists(backupDirectory))
            {
                return new OperationSuccess<DeckBackupPage>(
                    new DeckBackupPage(currentFingerprint, []));
            }

            List<DeckBackup> backups = [];
            foreach (string manifestPath in Directory.EnumerateFiles(backupDirectory, "*.json"))
            {
                DeckBackupManifest manifest = await ReadManifestAsync(
                    manifestPath,
                    cancellationToken).ConfigureAwait(false);
                backups.Add(manifest.ToPublic());
            }

            backups.Sort(static (left, right) =>
            {
                int created = right.CreatedAtUtc.CompareTo(left.CreatedAtUtc);
                return created != 0 ? created : left.BackupId.CompareTo(right.BackupId);
            });
            return new OperationSuccess<DeckBackupPage>(
                new DeckBackupPage(currentFingerprint, backups.ToArray()));
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return MapFailure<DeckBackupPage>(exception);
        }
        finally
        {
            mutationGate.Release();
        }
    }

    /// <summary>
    /// Creates one consistent verified SQLite snapshot and opaque manifest.
    /// </summary>
    public async Task<OperationResult<DeckBackup>> CreateAsync(CancellationToken cancellationToken)
    {
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await database.EnsureInitializedAsync(
                timeProvider.GetUtcNow(),
                applicationVersion,
                cancellationToken).ConfigureAwait(false);
            DeckBackupManifest manifest = await CreateUnderGateAsync(
                isRollback: false,
                cancellationToken).ConfigureAwait(false);
            return new OperationSuccess<DeckBackup>(manifest.ToPublic());
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return MapFailure<DeckBackup>(exception);
        }
        finally
        {
            mutationGate.Release();
        }
    }

    /// <summary>
    /// Restores a verified backup only when the current database fingerprint still matches.
    /// </summary>
    public async Task<OperationResult<DeckRestoreResult>> RestoreAsync(
        Guid backupId,
        string expectedDatabaseFingerprint,
        CancellationToken cancellationToken)
    {
        if (backupId == Guid.Empty || string.IsNullOrWhiteSpace(expectedDatabaseFingerprint))
        {
            return Invalid<DeckRestoreResult>("A backup ID and current database fingerprint are required.");
        }

        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!database.Exists)
            {
                return new OperationNotFound(
                    "deck-database-not-found",
                    "The local deck database was not found.");
            }

            string currentFingerprint = await ComputeCurrentFingerprintAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!currentFingerprint.Equals(
                    expectedDatabaseFingerprint.Trim(),
                    StringComparison.Ordinal))
            {
                return new OperationConflict(
                    "stale-database-fingerprint",
                    "The local deck database changed after the supplied fingerprint.");
            }

            (string backupPath, string manifestPath) = GetBackupPaths(backupId);
            if (!File.Exists(backupPath) || !File.Exists(manifestPath))
            {
                return BackupNotFound<DeckRestoreResult>();
            }

            DeckBackupManifest selected = await ReadManifestAsync(
                manifestPath,
                cancellationToken).ConfigureAwait(false);
            if (selected.BackupId != backupId)
            {
                throw new InvalidDataException("The local deck backup manifest is invalid.");
            }

            string selectedFingerprint = await ComputeFileFingerprintAsync(
                backupPath,
                selected.SchemaVersion,
                cancellationToken).ConfigureAwait(false);
            if (!selectedFingerprint.Equals(selected.Fingerprint, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The local deck backup failed fingerprint validation.");
            }

            await VerifyBackupAsync(backupPath, cancellationToken).ConfigureAwait(false);
            DeckBackupManifest rollback = await CreateUnderGateAsync(
                isRollback: true,
                cancellationToken).ConfigureAwait(false);

            DeleteSidecarFiles();
            string replacementPath = database.DatabasePath + ".restore";
            try
            {
                File.Copy(backupPath, replacementPath, overwrite: true);
                File.Move(replacementPath, database.DatabasePath, overwrite: true);
            }
            finally
            {
                File.Delete(replacementPath);
            }

            string restoredFingerprint = await ComputeCurrentFingerprintAsync(cancellationToken)
                .ConfigureAwait(false);
            return new OperationSuccess<DeckRestoreResult>(
                new DeckRestoreResult(backupId, rollback.BackupId, restoredFingerprint));
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return MapFailure<DeckRestoreResult>(exception);
        }
        finally
        {
            mutationGate.Release();
        }
    }

    /// <summary>
    /// Deletes one opaque backup pair without accepting a caller-supplied path.
    /// </summary>
    public async Task<OperationResult<DeckDeleteResult>> DeleteAsync(
        Guid backupId,
        CancellationToken cancellationToken)
    {
        if (backupId == Guid.Empty)
        {
            return Invalid<DeckDeleteResult>("The backup ID is invalid.");
        }

        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            (string backupPath, string manifestPath) = GetBackupPaths(backupId);
            if (!File.Exists(backupPath) || !File.Exists(manifestPath))
            {
                return BackupNotFound<DeckDeleteResult>();
            }

            File.Delete(manifestPath);
            File.Delete(backupPath);
            return new OperationSuccess<DeckDeleteResult>(new DeckDeleteResult(backupId));
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return MapFailure<DeckDeleteResult>(exception);
        }
        finally
        {
            mutationGate.Release();
        }
    }

    /// <summary>
    /// Creates a snapshot while the shared mutation gate is already held.
    /// </summary>
    private async Task<DeckBackupManifest> CreateUnderGateAsync(
        bool isRollback,
        CancellationToken cancellationToken)
    {
        string directory = GetBackupDirectory();
        Directory.CreateDirectory(directory);
        Guid backupId = createId();
        (string backupPath, string manifestPath) = GetBackupPaths(backupId);
        string temporaryPath = backupPath + ".tmp";
        string temporaryManifestPath = manifestPath + ".tmp";
        bool published = false;
        try
        {
            await using SqliteConnection source = await database.OpenConnectionAsync(
                writable: true,
                cancellationToken).ConfigureAwait(false);
            await using (SqliteCommand checkpoint = source.CreateCommand())
            {
                checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                await checkpoint.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            int deckCount = await DeckSql.CountDecksAsync(source, cancellationToken)
                .ConfigureAwait(false);
            SqliteConnectionStringBuilder destinationBuilder = new()
            {
                DataSource = temporaryPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
            };
            await using (SqliteConnection destination = new(destinationBuilder.ToString()))
            {
                await destination.OpenAsync(cancellationToken).ConfigureAwait(false);
                source.BackupDatabase(destination);
            }

            await VerifyBackupAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
            string fingerprint = await ComputeFileFingerprintAsync(
                temporaryPath,
                DeckDatabase.SchemaVersion,
                cancellationToken).ConfigureAwait(false);
            DeckBackupManifest manifest = new(
                backupId,
                DeckDatabase.SchemaVersion,
                fingerprint,
                timeProvider.GetUtcNow().ToUniversalTime(),
                deckCount,
                isRollback);
            string json = JsonSerializer.Serialize(manifest, SerializerOptions);
            await File.WriteAllTextAsync(
                temporaryManifestPath,
                json,
                Encoding.UTF8,
                cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, backupPath, overwrite: false);
            File.Move(temporaryManifestPath, manifestPath, overwrite: false);
            published = true;
            return manifest;
        }
        finally
        {
            File.Delete(temporaryPath);
            File.Delete(temporaryManifestPath);
            if (!published)
            {
                File.Delete(backupPath);
                File.Delete(manifestPath);
            }
        }
    }

    /// <summary>
    /// Verifies SQLite integrity and rejects future schemas before restore.
    /// </summary>
    private static async Task VerifyBackupAsync(
        string path,
        CancellationToken cancellationToken)
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        };
        await using SqliteConnection connection = new(builder.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand integrity = connection.CreateCommand();
        integrity.CommandText = "PRAGMA integrity_check;";
        string result = Convert.ToString(
            await integrity.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        if (!result.Equals("ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The local deck backup failed integrity validation.");
        }

        await DeckDatabase.ValidateExistingSchemaAsync(connection, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Computes the conservative current byte fingerprint while the mutation gate is held.
    /// </summary>
    private async Task<string> ComputeCurrentFingerprintAsync(CancellationToken cancellationToken)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"decks-schema-{DeckDatabase.SchemaVersion}\n"));
        await AppendFileAsync(hash, database.DatabasePath, cancellationToken).ConfigureAwait(false);
        string walPath = database.DatabasePath + "-wal";
        if (File.Exists(walPath))
        {
            await AppendFileAsync(hash, walPath, cancellationToken).ConfigureAwait(false);
        }

        return $"v{DeckDatabase.SchemaVersion}:{Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()}";
    }

    /// <summary>
    /// Computes one version-prefixed SHA-256 over immutable backup bytes.
    /// </summary>
    private static async Task<string> ComputeFileFingerprintAsync(
        string path,
        int schemaVersion,
        CancellationToken cancellationToken)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"decks-schema-{schemaVersion}\n"));
        await AppendFileAsync(hash, path, cancellationToken).ConfigureAwait(false);
        return $"v{schemaVersion}:{Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()}";
    }

    /// <summary>
    /// Streams one file into a hash without loading deck storage into memory.
    /// </summary>
    private static async Task AppendFileAsync(
        IncrementalHash hash,
        string path,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[64 * 1024];
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            buffer.Length,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            hash.AppendData(buffer.AsSpan(0, read));
        }
    }

    /// <summary>
    /// Reads and semantically validates one versioned manifest.
    /// </summary>
    private static async Task<DeckBackupManifest> ReadManifestAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        DeckBackupManifest manifest = await JsonSerializer.DeserializeAsync<DeckBackupManifest>(
            stream,
            SerializerOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The local deck backup manifest is invalid.");
        if (manifest.BackupId == Guid.Empty ||
            manifest.SchemaVersion != DeckDatabase.SchemaVersion ||
            string.IsNullOrWhiteSpace(manifest.Fingerprint) ||
            manifest.DeckCount < 0)
        {
            throw new InvalidDataException("The local deck backup manifest is invalid.");
        }

        return manifest;
    }

    /// <summary>
    /// Gets the private backup directory owned by this database family.
    /// </summary>
    private string GetBackupDirectory()
    {
        return Path.Combine(
            Path.GetDirectoryName(database.DatabasePath)!,
            "backups",
            "decks");
    }

    /// <summary>
    /// Derives internal paths only from a validated opaque backup ID.
    /// </summary>
    private (string BackupPath, string ManifestPath) GetBackupPaths(Guid backupId)
    {
        string stem = backupId.ToString("N");
        string directory = GetBackupDirectory();
        return (
            Path.Combine(directory, stem + ".db"),
            Path.Combine(directory, stem + ".json"));
    }

    /// <summary>
    /// Removes WAL and shared-memory sidecars before atomically replacing the main database.
    /// </summary>
    private void DeleteSidecarFiles()
    {
        File.Delete(database.DatabasePath + "-wal");
        File.Delete(database.DatabasePath + "-shm");
    }

    /// <summary>
    /// Identifies bounded local failures that may be safely projected.
    /// </summary>
    private static bool IsExpectedFailure(Exception exception)
    {
        return exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            JsonException or
            SqliteException;
    }

    /// <summary>
    /// Maps backup failures into path-free structured outcomes.
    /// </summary>
    private static OperationResult<T> MapFailure<T>(Exception exception)
    {
        return exception switch
        {
            InvalidDataException or JsonException => new OperationUnavailable(
                "deck-backup-corrupt",
                "The local deck backup is corrupt or unsupported."),
            SqliteException { SqliteErrorCode: 5 or 6 } => new OperationUnavailable(
                "deck-database-busy",
                "The local deck database is busy."),
            SqliteException => new OperationUnavailable(
                "deck-database-unavailable",
                "The local deck database is unavailable."),
            IOException or UnauthorizedAccessException => new OperationUnavailable(
                "deck-backup-unavailable",
                "Local deck backup storage is unavailable."),
            _ => new OperationUnavailable(
                "deck-backup-unavailable",
                "Local deck backup storage is unavailable."),
        };
    }

    /// <summary>
    /// Creates a stable invalid backup input result.
    /// </summary>
    private static OperationResult<T> Invalid<T>(string message)
    {
        return new OperationInvalidInput("invalid-deck-backup-input", message);
    }

    /// <summary>
    /// Creates a stable missing-backup result.
    /// </summary>
    private static OperationResult<T> BackupNotFound<T>()
    {
        return new OperationNotFound("deck-backup-not-found", "The local deck backup was not found.");
    }
}

/// <summary>
/// Defines the versioned private manifest persisted beside one SQLite backup.
/// </summary>
internal sealed record DeckBackupManifest(
    Guid BackupId,
    int SchemaVersion,
    string Fingerprint,
    DateTimeOffset CreatedAtUtc,
    int DeckCount,
    bool IsRollback)
{
    /// <summary>
    /// Projects the manifest without its private filesystem location.
    /// </summary>
    internal DeckBackup ToPublic()
    {
        return new DeckBackup(
            BackupId,
            SchemaVersion,
            Fingerprint,
            CreatedAtUtc.ToUniversalTime(),
            DeckCount,
            IsRollback);
    }
}
