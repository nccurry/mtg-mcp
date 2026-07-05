using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MtgMcp.Core.Evidence;
using MtgMcp.Core.Results;

namespace MtgMcp.Scryfall;

/// <summary>
/// Owns the versioned unified SQLite store for corpus evidence, request snapshots, leases, and pacing.
/// </summary>
internal sealed class ScryfallDatabase : IDisposable
{
    /// <summary>
    /// Defines the initial clean-break Scryfall schema.
    /// </summary>
    private const int SchemaVersion = 1;

    /// <summary>
    /// Detects an existing schema whose authored migration no longer matches version one.
    /// </summary>
    private const string SchemaChecksum = "93f5f609eff2ec0b7cf25cc0155075e8b05e100c39ed1b44fa8319ede3fbed9c";

    /// <summary>
    /// Serializes first-time schema initialization inside one process.
    /// </summary>
    private readonly SemaphoreSlim initializationGate = new(1, 1);

    /// <summary>
    /// Stores the private database path.
    /// </summary>
    private readonly string databasePath;

    /// <summary>
    /// Tracks whether this process has verified the schema.
    /// </summary>
    private bool schemaReady;

    /// <summary>
    /// Creates the database boundary without creating a directory or file.
    /// </summary>
    internal ScryfallDatabase(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        SQLitePCL.Batteries_V2.Init();
        databasePath = Path.Combine(dataRoot, "scryfall.db");
    }

    /// <summary>
    /// Reports whether the database already exists without creating it.
    /// </summary>
    internal bool Exists => File.Exists(databasePath);

    /// <summary>
    /// Gets the active generation identifier when a complete corpus is installed.
    /// </summary>
    internal async Task<Guid?> GetActiveGenerationIdAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection? connection = await OpenReadAsync(cancellationToken).ConfigureAwait(false);
        if (connection is null)
        {
            return null;
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT active_generation_id FROM corpus_state WHERE singleton = 1;";
        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is string text ? ParseGuid(text) : null;
    }

    /// <summary>
    /// Returns network-free corpus status without creating storage.
    /// </summary>
    internal async Task<OperationResult<ScryfallCorpusStatus>> GetCorpusStatusAsync(
        DateTimeOffset nowUtc,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection? connection = await OpenReadAsync(cancellationToken).ConfigureAwait(false);
        if (connection is null)
        {
            return new OperationSuccess<ScryfallCorpusStatus>(
                new ScryfallCorpusStatus("not-cached", null, null, null, null, true));
        }

        await using SqliteCommand stateCommand = connection.CreateCommand();
        stateCommand.CommandText =
            "SELECT active_generation_id, previous_generation_id, last_metadata_check_utc " +
            "FROM corpus_state WHERE singleton = 1;";
        await using SqliteDataReader reader = await stateCommand.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return new OperationSuccess<ScryfallCorpusStatus>(
                new ScryfallCorpusStatus("not-cached", null, null, null, null, true));
        }

        Guid? activeId = ReadNullableGuid(reader, 0);
        Guid? previousId = ReadNullableGuid(reader, 1);
        DateTimeOffset? checkedAt = ReadNullableUtc(reader, 2);
        await reader.DisposeAsync().ConfigureAwait(false);
        ScryfallCorpusGenerationStatus? active = activeId is Guid activeValue
            ? await ReadGenerationAsync(connection, activeValue, cancellationToken).ConfigureAwait(false)
            : null;
        ScryfallCorpusGenerationStatus? previous = previousId is Guid previousValue
            ? await ReadGenerationAsync(connection, previousValue, cancellationToken).ConfigureAwait(false)
            : null;
        bool eligible = activeId is null || checkedAt is null || nowUtc - checkedAt.Value >= ttl;
        long? ageSeconds = active is null
            ? null
            : Math.Max(0, (long)(nowUtc - active.CreatedAtUtc).TotalSeconds);
        return new OperationSuccess<ScryfallCorpusStatus>(
            new ScryfallCorpusStatus(
                active is null ? "not-cached" : "available",
                active,
                previous,
                checkedAt,
                ageSeconds,
                eligible));
    }

    /// <summary>
    /// Finds a corpus card through one validated lookup case.
    /// </summary>
    internal async Task<StoredCorpusObject?> FindCardAsync(
        ScryfallCardLookup lookup,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection? connection = await OpenReadAsync(cancellationToken).ConfigureAwait(false);
        if (connection is null)
        {
            return null;
        }

        Guid? generationId = await ReadStateGuidAsync(
            connection,
            "active_generation_id",
            cancellationToken).ConfigureAwait(false);
        if (generationId is null)
        {
            return null;
        }

        return await FindCardOnConnectionAsync(connection, generationId.Value, lookup, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Finds a corpus card in one exact retained generation for stable cursor replay.
    /// </summary>
    internal async Task<StoredCorpusObject?> FindCardInGenerationAsync(
        ScryfallCardLookup lookup,
        Guid generationId,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection? connection = await OpenReadAsync(cancellationToken).ConfigureAwait(false);
        return connection is null
            ? null
            : await FindCardOnConnectionAsync(connection, generationId, lookup, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reports whether one complete corpus generation remains available for cursor replay.
    /// </summary>
    internal async Task<bool> ContainsCompleteGenerationAsync(
        Guid generationId,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection? connection = await OpenReadAsync(cancellationToken).ConfigureAwait(false);
        if (connection is null)
        {
            return false;
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT 1 FROM corpus_generations WHERE generation_id = $generation AND status = 'complete' LIMIT 1;";
        command.Parameters.AddWithValue("$generation", FormatGuid(generationId));
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    /// <summary>
    /// Executes one validated card lookup against a caller-selected corpus generation.
    /// </summary>
    private static async Task<StoredCorpusObject?> FindCardOnConnectionAsync(
        SqliteConnection connection,
        Guid generationId,
        ScryfallCardLookup lookup,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Parameters.AddWithValue("$generation", FormatGuid(generationId));
        switch (lookup.Kind)
        {
            case "scryfall-id":
                command.CommandText =
                    "SELECT c.raw_json, g.created_at_utc, d.provider_updated_at_utc FROM card_objects c " +
                    "JOIN corpus_generations g ON g.generation_id = c.generation_id " +
                    "JOIN corpus_datasets d ON d.generation_id = c.generation_id AND d.dataset_type = 'all_cards' " +
                    "WHERE c.generation_id = $generation AND c.card_id = $value;";
                command.Parameters.AddWithValue("$value", lookup.Value!);
                break;
            case "oracle-id":
                command.CommandText =
                    "SELECT c.raw_json, g.created_at_utc, d.provider_updated_at_utc FROM card_objects c " +
                    "JOIN corpus_generations g ON g.generation_id = c.generation_id " +
                    "JOIN corpus_datasets d ON d.generation_id = c.generation_id AND d.dataset_type = 'all_cards' " +
                    "WHERE c.generation_id = $generation AND c.oracle_id = $value " +
                    "ORDER BY c.released_at DESC, c.set_code, c.collector_number LIMIT 1;";
                command.Parameters.AddWithValue("$value", lookup.Value!);
                break;
            case "exact-name":
            case "fuzzy-name":
                command.CommandText =
                    "SELECT c.raw_json, g.created_at_utc, d.provider_updated_at_utc FROM card_objects c " +
                    "JOIN corpus_generations g ON g.generation_id = c.generation_id " +
                    "JOIN corpus_datasets d ON d.generation_id = c.generation_id AND d.dataset_type = 'all_cards' " +
                    "WHERE c.generation_id = $generation AND (c.name_key = $value OR EXISTS " +
                    "(SELECT 1 FROM card_faces f WHERE f.generation_id = c.generation_id " +
                    "AND f.card_id = c.card_id AND f.name_key = $value)) " +
                    "ORDER BY c.name_key = $value DESC, c.lang = 'en' DESC, c.released_at DESC, " +
                    "c.set_code, c.collector_number LIMIT 1;";
                command.Parameters.AddWithValue("$value", lookup.Value!.Trim().ToUpperInvariant());
                break;
            case "printing":
                command.CommandText =
                    "SELECT c.raw_json, g.created_at_utc, d.provider_updated_at_utc FROM card_objects c " +
                    "JOIN corpus_generations g ON g.generation_id = c.generation_id " +
                    "JOIN corpus_datasets d ON d.generation_id = c.generation_id AND d.dataset_type = 'all_cards' " +
                    "WHERE c.generation_id = $generation AND c.set_code = $set AND c.collector_number = $collector " +
                    "ORDER BY c.lang = 'en' DESC, c.card_id LIMIT 1;";
                command.Parameters.AddWithValue("$set", lookup.SetCode!.Trim().ToLowerInvariant());
                command.Parameters.AddWithValue("$collector", lookup.CollectorNumber!.Trim());
                break;
            default:
                return null;
        }

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new StoredCorpusObject(
                generationId,
                ParseUtc(reader.GetString(1)),
                ParseUtc(reader.GetString(2)),
                reader.GetString(0))
            : null;
    }

    /// <summary>
    /// Returns every printing for one Oracle identity in stable provider order.
    /// </summary>
    internal async Task<StoredCorpusCollection?> GetPrintsAsync(
        Guid oracleId,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection? connection = await OpenReadAsync(cancellationToken).ConfigureAwait(false);
        if (connection is null)
        {
            return null;
        }

        Guid? generationId = await ReadStateGuidAsync(connection, "active_generation_id", cancellationToken)
            .ConfigureAwait(false);
        if (generationId is null)
        {
            return null;
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT raw_json FROM card_objects WHERE generation_id = $generation AND oracle_id = $oracle " +
            "ORDER BY released_at, set_code, collector_number, lang, card_id;";
        command.Parameters.AddWithValue("$generation", FormatGuid(generationId.Value));
        command.Parameters.AddWithValue("$oracle", FormatGuid(oracleId));
        IReadOnlyList<string> items = await ReadStringsAsync(command, cancellationToken).ConfigureAwait(false);
        ScryfallCorpusGenerationStatus generation = await ReadGenerationAsync(connection, generationId.Value, cancellationToken)
            .ConfigureAwait(false);
        DateTimeOffset providerUpdatedAtUtc = generation.Datasets
            .Single(value => value.Type == "all_cards")
            .ProviderUpdatedAtUtc;
        return new StoredCorpusCollection(generationId.Value, generation.CreatedAtUtc, providerUpdatedAtUtc, items);
    }

    /// <summary>
    /// Returns every ruling for one Oracle identity in provider order.
    /// </summary>
    internal async Task<StoredCorpusCollection?> GetRulingsAsync(
        Guid oracleId,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection? connection = await OpenReadAsync(cancellationToken).ConfigureAwait(false);
        if (connection is null)
        {
            return null;
        }

        Guid? generationId = await ReadStateGuidAsync(connection, "active_generation_id", cancellationToken)
            .ConfigureAwait(false);
        if (generationId is null)
        {
            return null;
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT raw_json FROM rulings WHERE generation_id = $generation AND oracle_id = $oracle " +
            "ORDER BY published_at, ordinal;";
        command.Parameters.AddWithValue("$generation", FormatGuid(generationId.Value));
        command.Parameters.AddWithValue("$oracle", FormatGuid(oracleId));
        IReadOnlyList<string> items = await ReadStringsAsync(command, cancellationToken).ConfigureAwait(false);
        ScryfallCorpusGenerationStatus generation = await ReadGenerationAsync(connection, generationId.Value, cancellationToken)
            .ConfigureAwait(false);
        DateTimeOffset providerUpdatedAtUtc = generation.Datasets
            .Single(value => value.Type == "rulings")
            .ProviderUpdatedAtUtc;
        return new StoredCorpusCollection(generationId.Value, generation.CreatedAtUtc, providerUpdatedAtUtc, items);
    }

    /// <summary>
    /// Returns direct community-tag evidence for a card projection.
    /// </summary>
    internal async Task<IReadOnlyList<ScryfallTagEvidence>> GetDirectTagsAsync(
        Guid? oracleId,
        IReadOnlyList<Guid> illustrationIds,
        DateTimeOffset retrievedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(illustrationIds);
        if (oracleId is null && illustrationIds.Count == 0)
        {
            return [];
        }

        await using SqliteConnection? connection = await OpenReadAsync(cancellationToken).ConfigureAwait(false);
        if (connection is null)
        {
            return [];
        }

        Guid? generationId = await ReadStateGuidAsync(connection, "active_generation_id", cancellationToken)
            .ConfigureAwait(false);
        if (generationId is null)
        {
            return [];
        }

        return await GetDirectTagsOnConnectionAsync(
            connection,
            generationId.Value,
            oracleId,
            illustrationIds,
            retrievedAtUtc,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns direct community tags from one retained generation for stable collection replay.
    /// </summary>
    internal async Task<IReadOnlyList<ScryfallTagEvidence>> GetDirectTagsInGenerationAsync(
        Guid generationId,
        Guid? oracleId,
        IReadOnlyList<Guid> illustrationIds,
        DateTimeOffset retrievedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(illustrationIds);
        if (oracleId is null && illustrationIds.Count == 0)
        {
            return [];
        }

        await using SqliteConnection? connection = await OpenReadAsync(cancellationToken).ConfigureAwait(false);
        return connection is null
            ? []
            : await GetDirectTagsOnConnectionAsync(
                connection,
                generationId,
                oracleId,
                illustrationIds,
                retrievedAtUtc,
                cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads direct community tags using one already opened database connection.
    /// </summary>
    private static async Task<IReadOnlyList<ScryfallTagEvidence>> GetDirectTagsOnConnectionAsync(
        SqliteConnection connection,
        Guid generationId,
        Guid? oracleId,
        IReadOnlyList<Guid> illustrationIds,
        DateTimeOffset retrievedAtUtc,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT t.tag_id, t.label, t.slug, t.tag_type, a.weight, a.annotation " +
            "FROM tag_assignments a JOIN tags t ON t.generation_id = a.generation_id AND t.tag_id = a.tag_id " +
            "WHERE a.generation_id = $generation AND " +
            "((a.target_type = 'oracle' AND a.target_id = $oracle) OR " +
            $"(a.target_type = 'art' AND a.target_id IN ({IllustrationParameters(illustrationIds.Count)}))) " +
            "ORDER BY t.tag_type, t.slug, t.tag_id;";
        command.Parameters.AddWithValue("$generation", FormatGuid(generationId));
        command.Parameters.AddWithValue("$oracle", oracleId is Guid oracle ? FormatGuid(oracle) : DBNull.Value);
        for (int index = 0; index < illustrationIds.Count; index++)
        {
            command.Parameters.AddWithValue($"$illustration{index}", FormatGuid(illustrationIds[index]));
        }
        List<ScryfallTagEvidence> results = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            Guid tagId = ParseGuid(reader.GetString(0));
            results.Add(new ScryfallTagEvidence(
                tagId,
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                "direct",
                [tagId],
                new SourceEvidenceDescriptor(
                    "scryfall-community-tags",
                    retrievedAtUtc,
                    FormatGuid(tagId),
                    FormatGuid(generationId))));
        }

        return results;
    }

    /// <summary>
    /// Finds tags by exact ID/slug or bounded label/slug search.
    /// </summary>
    internal async Task<OperationResult<ScryfallPage<ScryfallTag>>> SearchTagsAsync(
        string query,
        string? tagType,
        bool includeRaw,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection? connection = await OpenReadAsync(cancellationToken).ConfigureAwait(false);
        if (connection is null)
        {
            return new OperationNotCached("scryfall-corpus-missing", "Scryfall corpus data is not installed.");
        }

        Guid? generationId = await ReadStateGuidAsync(connection, "active_generation_id", cancellationToken)
            .ConfigureAwait(false);
        if (generationId is null)
        {
            return new OperationNotCached("scryfall-corpus-missing", "Scryfall corpus data is not installed.");
        }

        string scope = $"tags:{generationId:D}:{tagType ?? "all"}:{query}";
        string checksum = Hash(scope);
        if (!ScryfallCursor.TryDecode(cursor, scope, checksum, out int offset))
        {
            return new OperationInvalidInput("invalid-cursor", "The tag cursor is invalid for this request.");
        }

        await using SqliteCommand countCommand = connection.CreateCommand();
        ConfigureTagSearch(countCommand, generationId.Value, query, tagType);
        countCommand.CommandText = "SELECT COUNT(*) FROM tags t " + countCommand.CommandText;
        int total = Convert.ToInt32(
            await countCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
        await using SqliteCommand command = connection.CreateCommand();
        ConfigureTagSearch(command, generationId.Value, query, tagType);
        command.CommandText =
            "SELECT t.raw_json FROM tags t " + command.CommandText +
            " ORDER BY t.slug, t.tag_id LIMIT $limit OFFSET $offset;";
        command.Parameters.AddWithValue("$limit", pageSize);
        command.Parameters.AddWithValue("$offset", offset);
        IReadOnlyList<string> rawTags = await ReadStringsAsync(command, cancellationToken).ConfigureAwait(false);
        List<ScryfallTag> tags = [];
        foreach (string raw in rawTags)
        {
            using JsonDocument document = JsonDocument.Parse(raw);
            tags.Add(ScryfallMapper.Tag(document.RootElement, generationId.Value, includeRaw));
        }

        string? next = offset + tags.Count < total
            ? ScryfallCursor.Encode(scope, checksum, offset + tags.Count)
            : null;
        return new OperationSuccess<ScryfallPage<ScryfallTag>>(new ScryfallPage<ScryfallTag>(tags, total, next));
    }

    /// <summary>
    /// Resolves one tag and the cards assigned directly or through descendants.
    /// </summary>
    internal async Task<OperationResult<StoredCardsByTag>> GetCardsByTagAsync(
        string tagIdentity,
        string tagType,
        bool includeDescendants,
        string minimumWeight,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection? connection = await OpenReadAsync(cancellationToken).ConfigureAwait(false);
        if (connection is null)
        {
            return new OperationNotCached("scryfall-corpus-missing", "Scryfall corpus data is not installed.");
        }

        Guid? generationId = await ReadStateGuidAsync(connection, "active_generation_id", cancellationToken)
            .ConfigureAwait(false);
        if (generationId is null)
        {
            return new OperationNotCached("scryfall-corpus-missing", "Scryfall corpus data is not installed.");
        }

        StoredTag? root = await FindTagAsync(connection, generationId.Value, tagIdentity, tagType, cancellationToken)
            .ConfigureAwait(false);
        if (root is null)
        {
            return new OperationNotFound("scryfall-tag-not-found", "The requested Scryfall tag was not found.");
        }

        Dictionary<Guid, IReadOnlyList<Guid>> paths = includeDescendants
            ? await DescendantPathsAsync(connection, generationId.Value, root.Id, cancellationToken)
                .ConfigureAwait(false)
            : new Dictionary<Guid, IReadOnlyList<Guid>> { [root.Id] = [root.Id] };
        List<StoredTagAssignment> assignments = [];
        foreach ((Guid tagId, IReadOnlyList<Guid> path) in paths)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT a.target_id, a.weight, a.annotation, c.raw_json, t.label, t.slug " +
                "FROM tag_assignments a " +
                "JOIN tags t ON t.generation_id = a.generation_id AND t.tag_id = a.tag_id " +
                "JOIN card_objects c ON c.generation_id = a.generation_id AND " +
                "((a.target_type = 'oracle' AND c.oracle_id = a.target_id) OR " +
                "(a.target_type = 'art' AND (c.illustration_id = a.target_id OR EXISTS " +
                "(SELECT 1 FROM card_faces f WHERE f.generation_id = c.generation_id " +
                "AND f.card_id = c.card_id AND f.illustration_id = a.target_id)))) " +
                "WHERE a.generation_id = $generation AND a.tag_id = $tag " +
                "ORDER BY c.name_key, c.set_code, c.collector_number, c.card_id;";
            command.Parameters.AddWithValue("$generation", FormatGuid(generationId.Value));
            command.Parameters.AddWithValue("$tag", FormatGuid(tagId));
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                string weight = reader.GetString(1);
                if (WeightRank(weight) < WeightRank(minimumWeight))
                {
                    continue;
                }

                assignments.Add(new StoredTagAssignment(
                    tagId,
                    reader.GetString(4),
                    reader.GetString(5),
                    tagType,
                    weight,
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    tagId == root.Id ? "direct" : "inherited",
                    path,
                    reader.GetString(3)));
            }
        }

        assignments.Sort(static (left, right) =>
        {
            using JsonDocument leftDocument = JsonDocument.Parse(left.CardJson);
            using JsonDocument rightDocument = JsonDocument.Parse(right.CardJson);
            int name = string.Compare(
                ScryfallMapper.RequiredString(leftDocument.RootElement, "name"),
                ScryfallMapper.RequiredString(rightDocument.RootElement, "name"),
                StringComparison.Ordinal);
            return name != 0 ? name : string.Compare(left.CardJson, right.CardJson, StringComparison.Ordinal);
        });
        ScryfallCorpusGenerationStatus generation = await ReadGenerationAsync(
            connection,
            generationId.Value,
            cancellationToken).ConfigureAwait(false);
        DateTimeOffset providerUpdatedAtUtc = generation.Datasets
            .Single(value => value.Type == "all_cards")
            .ProviderUpdatedAtUtc;
        return new OperationSuccess<StoredCardsByTag>(
            new StoredCardsByTag(
                generationId.Value,
                generation.CreatedAtUtc,
                providerUpdatedAtUtc,
                root.RawJson,
                assignments));
    }

    /// <summary>
    /// Finds an eligible exact-request snapshot and its complete raw membership.
    /// </summary>
    internal async Task<StoredSnapshot?> FindSnapshotAsync(
        string fingerprint,
        DateTimeOffset? minimumRetrievedAtUtc,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection? connection = await OpenReadAsync(cancellationToken).ConfigureAwait(false);
        if (connection is null)
        {
            return null;
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT snapshot_id, operation, request_json, retrieved_at_utc, checksum, total_count, predecessor_id " +
            "FROM request_snapshots WHERE fingerprint = $fingerprint " +
            "AND ($minimum IS NULL OR retrieved_at_utc >= $minimum) " +
            "ORDER BY retrieved_at_utc DESC, snapshot_id DESC LIMIT 1;";
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        command.Parameters.AddWithValue(
            "$minimum",
            minimumRetrievedAtUtc is DateTimeOffset minimum ? FormatUtc(minimum) : DBNull.Value);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        StoredSnapshotHeader header = ReadSnapshotHeader(reader);
        await reader.DisposeAsync().ConfigureAwait(false);
        IReadOnlyList<string> members = await ReadSnapshotMembersAsync(
            connection,
            header.SnapshotId,
            cancellationToken).ConfigureAwait(false);
        return new StoredSnapshot(header, members);
    }

    /// <summary>
    /// Finds one immutable snapshot by exact identity for cursor-bound replay.
    /// </summary>
    internal async Task<StoredSnapshot?> FindSnapshotByIdAsync(
        Guid snapshotId,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection? connection = await OpenReadAsync(cancellationToken).ConfigureAwait(false);
        if (connection is null)
        {
            return null;
        }

        StoredSnapshotHeader? header = await ReadSnapshotHeaderAsync(connection, snapshotId, cancellationToken)
            .ConfigureAwait(false);
        if (header is null)
        {
            return null;
        }

        IReadOnlyList<string> members = await ReadSnapshotMembersAsync(
            connection,
            snapshotId,
            cancellationToken).ConfigureAwait(false);
        return new StoredSnapshot(header, members);
    }

    /// <summary>
    /// Stores one completed provider request atomically with raw pages and ordered members.
    /// </summary>
    internal async Task<StoredSnapshot> SaveSnapshotAsync(
        string operation,
        string requestJson,
        string fingerprint,
        IReadOnlyList<string> rawPages,
        IReadOnlyList<string> members,
        DateTimeOffset retrievedAtUtc,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        Guid snapshotId = Guid.NewGuid();
        StoredSnapshot? predecessor = await FindSnapshotOnConnectionAsync(
            connection,
            fingerprint,
            cancellationToken).ConfigureAwait(false);
        string checksum = Hash(string.Join('\n', rawPages.Select(Hash)));
        await using SqliteTransaction transaction = connection.BeginTransaction();
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                "INSERT INTO request_snapshots " +
                "(snapshot_id, operation, request_json, fingerprint, retrieved_at_utc, checksum, total_count, predecessor_id) " +
                "VALUES ($id, $operation, $request, $fingerprint, $retrieved, $checksum, $count, $predecessor);";
            command.Parameters.AddWithValue("$id", FormatGuid(snapshotId));
            command.Parameters.AddWithValue("$operation", operation);
            command.Parameters.AddWithValue("$request", requestJson);
            command.Parameters.AddWithValue("$fingerprint", fingerprint);
            command.Parameters.AddWithValue("$retrieved", FormatUtc(retrievedAtUtc));
            command.Parameters.AddWithValue("$checksum", checksum);
            command.Parameters.AddWithValue("$count", members.Count);
            command.Parameters.AddWithValue(
                "$predecessor",
                predecessor is null ? DBNull.Value : FormatGuid(predecessor.Header.SnapshotId));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        for (int index = 0; index < rawPages.Count; index++)
        {
            string payloadChecksum = Hash(rawPages[index]);
            await InsertSnapshotPayloadAsync(
                connection,
                transaction,
                payloadChecksum,
                rawPages[index],
                cancellationToken).ConfigureAwait(false);
            await using SqliteCommand page = connection.CreateCommand();
            page.Transaction = transaction;
            page.CommandText =
                "INSERT INTO snapshot_pages (snapshot_id, ordinal, checksum) " +
                "VALUES ($id, $ordinal, $checksum);";
            page.Parameters.AddWithValue("$id", FormatGuid(snapshotId));
            page.Parameters.AddWithValue("$ordinal", index);
            page.Parameters.AddWithValue("$checksum", payloadChecksum);
            await page.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        for (int index = 0; index < members.Count; index++)
        {
            string payloadChecksum = Hash(members[index]);
            await InsertSnapshotPayloadAsync(
                connection,
                transaction,
                payloadChecksum,
                members[index],
                cancellationToken).ConfigureAwait(false);
            await using SqliteCommand member = connection.CreateCommand();
            member.Transaction = transaction;
            member.CommandText =
                "INSERT INTO snapshot_members (snapshot_id, ordinal, checksum) " +
                "VALUES ($id, $ordinal, $checksum);";
            member.Parameters.AddWithValue("$id", FormatGuid(snapshotId));
            member.Parameters.AddWithValue("$ordinal", index);
            member.Parameters.AddWithValue("$checksum", payloadChecksum);
            await member.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new StoredSnapshot(
            new StoredSnapshotHeader(
                snapshotId,
                operation,
                requestJson,
                retrievedAtUtc.ToUniversalTime(),
                checksum,
                members.Count,
                predecessor?.Header.SnapshotId),
            members);
    }

    /// <summary>
    /// Lists immutable request snapshots in stable reverse-retrieval order.
    /// </summary>
    internal async Task<OperationResult<ScryfallPage<ScryfallSnapshotSummary>>> ListSnapshotsAsync(
        string? operation,
        DateTimeOffset? retrievedAfterUtc,
        DateTimeOffset? retrievedBeforeUtc,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection? connection = await OpenReadAsync(cancellationToken).ConfigureAwait(false);
        if (connection is null)
        {
            return new OperationSuccess<ScryfallPage<ScryfallSnapshotSummary>>(
                new ScryfallPage<ScryfallSnapshotSummary>([], 0, null));
        }

        string scope =
            $"snapshots:{operation ?? "all"}:{FormatOptionalUtc(retrievedAfterUtc)}:{FormatOptionalUtc(retrievedBeforeUtc)}";
        string checksum = await SnapshotCollectionChecksumAsync(
            connection,
            operation,
            retrievedAfterUtc,
            retrievedBeforeUtc,
            cancellationToken)
            .ConfigureAwait(false);
        if (!ScryfallCursor.TryDecode(cursor, scope, checksum, out int offset))
        {
            return new OperationInvalidInput("invalid-cursor", "The snapshot cursor is invalid for this request.");
        }

        await using SqliteCommand count = connection.CreateCommand();
        count.CommandText =
            "SELECT COUNT(*) FROM request_snapshots WHERE ($operation IS NULL OR operation = $operation) " +
            "AND ($after IS NULL OR retrieved_at_utc >= $after) " +
            "AND ($before IS NULL OR retrieved_at_utc <= $before);";
        AddSnapshotFilters(count, operation, retrievedAfterUtc, retrievedBeforeUtc);
        int total = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT snapshot_id, operation, request_json, retrieved_at_utc, checksum, total_count, predecessor_id " +
            "FROM request_snapshots WHERE ($operation IS NULL OR operation = $operation) " +
            "AND ($after IS NULL OR retrieved_at_utc >= $after) " +
            "AND ($before IS NULL OR retrieved_at_utc <= $before) " +
            "ORDER BY retrieved_at_utc DESC, snapshot_id DESC LIMIT $limit OFFSET $offset;";
        AddSnapshotFilters(command, operation, retrievedAfterUtc, retrievedBeforeUtc);
        command.Parameters.AddWithValue("$limit", pageSize);
        command.Parameters.AddWithValue("$offset", offset);
        List<ScryfallSnapshotSummary> summaries = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            StoredSnapshotHeader header = ReadSnapshotHeader(reader);
            summaries.Add(ToSummary(header));
        }

        string? next = offset + summaries.Count < total
            ? ScryfallCursor.Encode(scope, checksum, offset + summaries.Count)
            : null;
        return new OperationSuccess<ScryfallPage<ScryfallSnapshotSummary>>(
            new ScryfallPage<ScryfallSnapshotSummary>(summaries, total, next));
    }

    /// <summary>
    /// Replays one immutable snapshot page using a checksum-bound cursor.
    /// </summary>
    internal async Task<OperationResult<ScryfallSnapshotPage>> GetSnapshotAsync(
        Guid snapshotId,
        string? cursor,
        int pageSize,
        bool includeRaw,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection? connection = await OpenReadAsync(cancellationToken).ConfigureAwait(false);
        if (connection is null)
        {
            return new OperationNotFound("scryfall-snapshot-not-found", "The requested snapshot does not exist.");
        }

        StoredSnapshotHeader? header = await ReadSnapshotHeaderAsync(connection, snapshotId, cancellationToken)
            .ConfigureAwait(false);
        if (header is null)
        {
            return new OperationNotFound("scryfall-snapshot-not-found", "The requested snapshot does not exist.");
        }

        string scope = $"snapshot:{snapshotId:D}";
        if (!ScryfallCursor.TryDecode(cursor, scope, header.Checksum, out int offset) || offset > header.TotalCount)
        {
            return new OperationInvalidInput("invalid-cursor", "The snapshot cursor is invalid for this request.");
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT m.ordinal, m.checksum, p.raw_json FROM snapshot_members m " +
            "JOIN snapshot_payloads p ON p.checksum = m.checksum " +
            "WHERE m.snapshot_id = $id ORDER BY m.ordinal LIMIT $limit OFFSET $offset;";
        command.Parameters.AddWithValue("$id", FormatGuid(snapshotId));
        command.Parameters.AddWithValue("$limit", pageSize);
        command.Parameters.AddWithValue("$offset", offset);
        List<ScryfallSnapshotMember> items = [];
        await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                JsonElement? raw = null;
                if (includeRaw)
                {
                    using JsonDocument document = JsonDocument.Parse(reader.GetString(2));
                    raw = document.RootElement.Clone();
                }

                items.Add(new ScryfallSnapshotMember(reader.GetInt32(0), reader.GetString(1), raw));
            }
        }

        string? next = offset + items.Count < header.TotalCount
            ? ScryfallCursor.Encode(scope, header.Checksum, offset + items.Count)
            : null;
        using JsonDocument requestDocument = JsonDocument.Parse(header.RequestJson);
        return new OperationSuccess<ScryfallSnapshotPage>(
            new ScryfallSnapshotPage(ToSummary(header), requestDocument.RootElement.Clone(), items, next));
    }

    /// <summary>
    /// Deletes one snapshot only when its checksum and explicit acknowledgement match.
    /// </summary>
    internal async Task<OperationResult<ScryfallSnapshotDeleteResult>> DeleteSnapshotAsync(
        Guid snapshotId,
        string expectedChecksum,
        bool acknowledgeDataLoss,
        CancellationToken cancellationToken)
    {
        if (!acknowledgeDataLoss)
        {
            return new OperationInvalidInput("evidence-loss-not-acknowledged", "Snapshot deletion requires explicit acknowledgement.");
        }

        await using SqliteConnection connection = await OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        StoredSnapshotHeader? header = await ReadSnapshotHeaderAsync(connection, snapshotId, cancellationToken)
            .ConfigureAwait(false);
        if (header is null)
        {
            return new OperationNotFound("scryfall-snapshot-not-found", "The requested snapshot does not exist.");
        }

        if (!string.Equals(header.Checksum, expectedChecksum, StringComparison.Ordinal))
        {
            return new OperationConflict("stale-scryfall-snapshot", "The snapshot checksum changed before deletion.");
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM request_snapshots WHERE snapshot_id = $id; " +
            "DELETE FROM snapshot_payloads WHERE NOT EXISTS " +
            "(SELECT 1 FROM snapshot_pages p WHERE p.checksum = snapshot_payloads.checksum) " +
            "AND NOT EXISTS (SELECT 1 FROM snapshot_members m WHERE m.checksum = snapshot_payloads.checksum);";
        command.Parameters.AddWithValue("$id", FormatGuid(snapshotId));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return new OperationSuccess<ScryfallSnapshotDeleteResult>(
            new ScryfallSnapshotDeleteResult(snapshotId, header.Checksum));
    }

    /// <summary>
    /// Begins one invisible staging generation.
    /// </summary>
    internal async Task<Guid> BeginGenerationAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        Guid generationId = Guid.NewGuid();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO corpus_generations (generation_id, created_at_utc, status) VALUES ($id, $created, 'staging');";
        command.Parameters.AddWithValue("$id", FormatGuid(generationId));
        command.Parameters.AddWithValue("$created", FormatUtc(nowUtc));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return generationId;
    }

    /// <summary>
    /// Removes staging generations abandoned by an earlier process after its corpus lease expired.
    /// </summary>
    internal async Task RemoveAbandonedStagingGenerationsAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM corpus_generations WHERE status = 'staging';";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Imports one compressed-JSONL dataset into generation-owned staging rows with bounded memory.
    /// </summary>
    internal async Task<ScryfallCorpusDatasetStatus> ImportDatasetAsync(
        Guid generationId,
        ScryfallBulkData metadata,
        Stream jsonlStream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jsonlStream);
        await using SqliteConnection connection = await OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using StreamReader reader = new(jsonlStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        long count = 0;
        long bytes = 0;
        long maximumBytes = metadata.Size + Math.Max(1_048_576, metadata.Size / 10);
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            byte[] lineBytes = Encoding.UTF8.GetBytes(line);
            hash.AppendData(lineBytes);
            hash.AppendData("\n"u8);
            bytes += lineBytes.Length + 1;
            if (bytes > maximumBytes)
            {
                throw new InvalidDataException("The bulk dataset exceeded its bounded declared-size allowance.");
            }

            using JsonDocument document = JsonDocument.Parse(line);
            await InsertCorpusObjectAsync(
                connection,
                transaction,
                generationId,
                metadata.Type,
                document.RootElement,
                count,
                cancellationToken).ConfigureAwait(false);
            count++;
        }

        string checksum = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        await using SqliteCommand dataset = connection.CreateCommand();
        dataset.Transaction = transaction;
        dataset.CommandText =
            "INSERT INTO corpus_datasets " +
            "(generation_id, dataset_type, provider_id, provider_updated_at_utc, source_bytes, row_count, checksum) " +
            "VALUES ($generation, $type, $provider, $updated, $bytes, $count, $checksum);";
        dataset.Parameters.AddWithValue("$generation", FormatGuid(generationId));
        dataset.Parameters.AddWithValue("$type", metadata.Type);
        dataset.Parameters.AddWithValue("$provider", FormatGuid(metadata.Id));
        dataset.Parameters.AddWithValue("$updated", FormatUtc(metadata.UpdatedAtUtc));
        dataset.Parameters.AddWithValue("$bytes", bytes);
        dataset.Parameters.AddWithValue("$count", count);
        dataset.Parameters.AddWithValue("$checksum", checksum);
        await dataset.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new ScryfallCorpusDatasetStatus(metadata.Type, metadata.Id, metadata.UpdatedAtUtc, count, bytes, checksum);
    }

    /// <summary>
    /// Validates and atomically activates one complete four-dataset generation.
    /// </summary>
    internal async Task<OperationResult<ScryfallCorpusSyncResult>> ActivateGenerationAsync(
        Guid generationId,
        DateTimeOffset checkedAtUtc,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        string? validationFailure = await ValidateGenerationAsync(connection, generationId, cancellationToken)
            .ConfigureAwait(false);
        if (validationFailure is not null)
        {
            await DeleteGenerationOnConnectionAsync(connection, generationId, cancellationToken).ConfigureAwait(false);
            return new OperationUnavailable("invalid-scryfall-corpus", validationFailure);
        }

        Guid? oldActive = await ReadStateGuidAsync(connection, "active_generation_id", cancellationToken)
            .ConfigureAwait(false);
        Guid? oldPrevious = await ReadStateGuidAsync(connection, "previous_generation_id", cancellationToken)
            .ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        await using (SqliteCommand generation = connection.CreateCommand())
        {
            generation.Transaction = transaction;
            generation.CommandText =
                "UPDATE corpus_generations SET status = 'complete', activated_at_utc = $activated " +
                "WHERE generation_id = $generation AND status = 'staging';";
            generation.Parameters.AddWithValue("$activated", FormatUtc(checkedAtUtc));
            generation.Parameters.AddWithValue("$generation", FormatGuid(generationId));
            if (await generation.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return new OperationConflict("stale-scryfall-generation", "The staging corpus generation is no longer activatable.");
            }
        }

        await using (SqliteCommand state = connection.CreateCommand())
        {
            state.Transaction = transaction;
            state.CommandText =
                "UPDATE corpus_state SET active_generation_id = $active, previous_generation_id = $previous, " +
                "last_metadata_check_utc = $checked WHERE singleton = 1;";
            state.Parameters.AddWithValue("$active", FormatGuid(generationId));
            state.Parameters.AddWithValue("$previous", oldActive is Guid active ? FormatGuid(active) : DBNull.Value);
            state.Parameters.AddWithValue("$checked", FormatUtc(checkedAtUtc));
            await state.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        if (oldPrevious is Guid prune && prune != oldActive)
        {
            await DeleteGenerationOnConnectionAsync(connection, prune, cancellationToken).ConfigureAwait(false);
        }

        ScryfallCorpusGenerationStatus activated = await ReadGenerationAsync(connection, generationId, cancellationToken)
            .ConfigureAwait(false);
        return new OperationSuccess<ScryfallCorpusSyncResult>(
            new ScryfallCorpusSyncResult("activated", generationId, oldActive, activated.Datasets));
    }

    /// <summary>
    /// Deletes an abandoned staging generation and all generation-owned rows.
    /// </summary>
    internal async Task DeleteGenerationAsync(Guid generationId, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        await DeleteGenerationOnConnectionAsync(connection, generationId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reports whether official metadata exactly matches the active generation.
    /// </summary>
    internal async Task<bool> ActiveMetadataMatchesAsync(
        IReadOnlyList<ScryfallBulkData> datasets,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection? connection = await OpenReadAsync(cancellationToken).ConfigureAwait(false);
        if (connection is null)
        {
            return false;
        }

        Guid? active = await ReadStateGuidAsync(connection, "active_generation_id", cancellationToken)
            .ConfigureAwait(false);
        if (active is null)
        {
            return false;
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT dataset_type, provider_id, provider_updated_at_utc FROM corpus_datasets " +
            "WHERE generation_id = $generation ORDER BY dataset_type;";
        command.Parameters.AddWithValue("$generation", FormatGuid(active.Value));
        Dictionary<string, (Guid Id, DateTimeOffset Updated)> installed = new(StringComparer.Ordinal);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            installed.Add(reader.GetString(0), (ParseGuid(reader.GetString(1)), ParseUtc(reader.GetString(2))));
        }

        return datasets.Count == installed.Count && datasets.All(dataset =>
            installed.TryGetValue(dataset.Type, out (Guid Id, DateTimeOffset Updated) value) &&
            value.Id == dataset.Id && value.Updated == dataset.UpdatedAtUtc.ToUniversalTime());
    }

    /// <summary>
    /// Records a successful metadata check without replacing corpus evidence.
    /// </summary>
    internal async Task RecordMetadataCheckAsync(DateTimeOffset checkedAtUtc, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE corpus_state SET last_metadata_check_utc = $checked WHERE singleton = 1;";
        command.Parameters.AddWithValue("$checked", FormatUtc(checkedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Swaps current and previous complete generations under exact identity guards.
    /// </summary>
    internal async Task<OperationResult<ScryfallCorpusMutationResult>> RollbackCorpusAsync(
        Guid expectedActive,
        Guid expectedPrevious,
        bool acknowledgeActivationChange,
        CancellationToken cancellationToken)
    {
        if (!acknowledgeActivationChange)
        {
            return new OperationInvalidInput("activation-change-not-acknowledged", "Corpus rollback requires explicit acknowledgement.");
        }

        await using SqliteConnection connection = await OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        Guid? active = await ReadStateGuidAsync(connection, "active_generation_id", cancellationToken)
            .ConfigureAwait(false);
        Guid? previous = await ReadStateGuidAsync(connection, "previous_generation_id", cancellationToken)
            .ConfigureAwait(false);
        if (active != expectedActive || previous != expectedPrevious)
        {
            return new OperationConflict("stale-scryfall-generation", "The active corpus generation changed before rollback.");
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "UPDATE corpus_state SET active_generation_id = $active, previous_generation_id = $previous " +
            "WHERE singleton = 1;";
        command.Parameters.AddWithValue("$active", FormatGuid(expectedPrevious));
        command.Parameters.AddWithValue("$previous", FormatGuid(expectedActive));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return new OperationSuccess<ScryfallCorpusMutationResult>(
            new ScryfallCorpusMutationResult("rolled-back", expectedPrevious, expectedActive));
    }

    /// <summary>
    /// Deletes active and previous corpus generations under an exact active-identity guard.
    /// </summary>
    internal async Task<OperationResult<ScryfallCorpusMutationResult>> DeleteCorpusAsync(
        Guid expectedActive,
        bool acknowledgeDataLoss,
        CancellationToken cancellationToken)
    {
        if (!acknowledgeDataLoss)
        {
            return new OperationInvalidInput("data-loss-not-acknowledged", "Corpus deletion requires explicit acknowledgement.");
        }

        await using SqliteConnection connection = await OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        Guid? active = await ReadStateGuidAsync(connection, "active_generation_id", cancellationToken)
            .ConfigureAwait(false);
        Guid? previous = await ReadStateGuidAsync(connection, "previous_generation_id", cancellationToken)
            .ConfigureAwait(false);
        if (active != expectedActive)
        {
            return new OperationConflict("stale-scryfall-generation", "The active corpus generation changed before deletion.");
        }

        await using SqliteTransaction transaction = connection.BeginTransaction();
        await using (SqliteCommand state = connection.CreateCommand())
        {
            state.Transaction = transaction;
            state.CommandText =
                "UPDATE corpus_state SET active_generation_id = NULL, previous_generation_id = NULL, " +
                "last_metadata_check_utc = NULL WHERE singleton = 1;";
            await state.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await DeleteGenerationOnConnectionAsync(connection, active.Value, cancellationToken, transaction)
            .ConfigureAwait(false);
        if (previous is Guid previousValue && previousValue != active)
        {
            await DeleteGenerationOnConnectionAsync(connection, previousValue, cancellationToken, transaction)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new OperationSuccess<ScryfallCorpusMutationResult>(
            new ScryfallCorpusMutationResult("deleted", null, null));
    }

    /// <summary>
    /// Acquires one expiring acquisition lease without blocking unrelated reads.
    /// </summary>
    internal async Task<bool> TryAcquireLeaseAsync(
        string key,
        string owner,
        DateTimeOffset nowUtc,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        await using (SqliteCommand cleanup = connection.CreateCommand())
        {
            cleanup.Transaction = transaction;
            cleanup.CommandText = "DELETE FROM acquisition_leases WHERE expires_at_utc <= $now;";
            cleanup.Parameters.AddWithValue("$now", FormatUtc(nowUtc));
            await cleanup.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT OR IGNORE INTO acquisition_leases (lease_key, owner_id, expires_at_utc) " +
            "VALUES ($key, $owner, $expires);";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$owner", owner);
        command.Parameters.AddWithValue("$expires", FormatUtc(nowUtc + duration));
        bool acquired = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return acquired;
    }

    /// <summary>
    /// Releases a lease only for its exact owner.
    /// </summary>
    internal async Task ReleaseLeaseAsync(string key, string owner, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM acquisition_leases WHERE lease_key = $key AND owner_id = $owner;";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$owner", owner);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reserves the next globally paced provider start and returns its required delay.
    /// </summary>
    internal async Task<TimeSpan> ReserveProviderStartAsync(
        DateTimeOffset nowUtc,
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        await using SqliteCommand read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "SELECT next_start_utc FROM provider_pacing WHERE singleton = 1;";
        object? current = await read.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        DateTimeOffset reserved = current is string text && ParseUtc(text) > nowUtc
            ? ParseUtc(text)
            : nowUtc;
        await using SqliteCommand write = connection.CreateCommand();
        write.Transaction = transaction;
        write.CommandText = "UPDATE provider_pacing SET next_start_utc = $next WHERE singleton = 1;";
        write.Parameters.AddWithValue("$next", FormatUtc(reserved + interval));
        await write.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return reserved > nowUtc ? reserved - nowUtc : TimeSpan.Zero;
    }

    /// <summary>
    /// Opens the existing database read-only or reports its absence without creating anything.
    /// </summary>
    private async Task<SqliteConnection?> OpenReadAsync(CancellationToken cancellationToken)
    {
        if (!Exists)
        {
            return null;
        }

        SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ValidateExistingSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Opens the writable database and verifies its transactional schema.
    /// </summary>
    private async Task<SqliteConnection> OpenWriteAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Creates and validates schema version one once per process.
    /// </summary>
    private async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (schemaReady)
        {
            return;
        }

        await initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (schemaReady)
            {
                return;
            }

            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = SchemaSql;
            try
            {
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                await ValidateSchemaRowAsync(connection, cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException exception)
            {
                throw new InvalidDataException("The Scryfall database schema is unsupported or corrupt.", exception);
            }

            schemaReady = true;
        }
        finally
        {
            initializationGate.Release();
        }
    }

    /// <summary>
    /// Validates an existing database without creating tables or changing migration history.
    /// </summary>
    private async Task ValidateExistingSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        if (schemaReady)
        {
            return;
        }

        await initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (schemaReady)
            {
                return;
            }

            try
            {
                await ValidateSchemaRowAsync(connection, cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException exception)
            {
                throw new InvalidDataException("The Scryfall database schema is unsupported or corrupt.", exception);
            }

            schemaReady = true;
        }
        finally
        {
            initializationGate.Release();
        }
    }

    /// <summary>
    /// Verifies the one clean-break migration identity and checksum.
    /// </summary>
    private static async Task ValidateSchemaRowAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT version, checksum FROM schema_migrations ORDER BY version;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        bool valid = await reader.ReadAsync(cancellationToken).ConfigureAwait(false) &&
            reader.GetInt32(0) == SchemaVersion &&
            string.Equals(reader.GetString(1), SchemaChecksum, StringComparison.Ordinal) &&
            !await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (!valid)
        {
            throw new InvalidDataException("The Scryfall database schema is unsupported or corrupt.");
        }
    }

    /// <summary>
    /// Stores one immutable JSON payload once, keyed by its SHA-256 checksum.
    /// </summary>
    private static async Task InsertSnapshotPayloadAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string checksum,
        string rawJson,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT OR IGNORE INTO snapshot_payloads (checksum, raw_json) VALUES ($checksum, $raw);";
        command.Parameters.AddWithValue("$checksum", checksum);
        command.Parameters.AddWithValue("$raw", rawJson);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts one dataset object into its generation-owned normalized tables.
    /// </summary>
    private static async Task InsertCorpusObjectAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid generationId,
        string datasetType,
        JsonElement raw,
        long ordinal,
        CancellationToken cancellationToken)
    {
        switch (datasetType)
        {
            case "all_cards":
                await InsertCardAsync(connection, transaction, generationId, raw, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case "rulings":
                await InsertRulingAsync(connection, transaction, generationId, raw, ordinal, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case "oracle_tags":
                await InsertTagAsync(connection, transaction, generationId, raw, "oracle", cancellationToken)
                    .ConfigureAwait(false);
                break;
            case "art_tags":
                await InsertTagAsync(connection, transaction, generationId, raw, "art", cancellationToken)
                    .ConfigureAwait(false);
                break;
            default:
                throw new InvalidDataException("The bulk dataset type is not part of the fixed corpus profile.");
        }
    }

    /// <summary>
    /// Inserts one lossless card and its optional faces.
    /// </summary>
    private static async Task InsertCardAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid generationId,
        JsonElement raw,
        CancellationToken cancellationToken)
    {
        Guid cardId = ScryfallMapper.RequiredGuid(raw, "id");
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO card_objects " +
            "(generation_id, card_id, oracle_id, illustration_id, name, name_key, set_code, collector_number, lang, released_at, raw_json) " +
            "VALUES ($generation, $card, $oracle, $illustration, $name, $nameKey, $set, $collector, $lang, $released, $raw);";
        command.Parameters.AddWithValue("$generation", FormatGuid(generationId));
        command.Parameters.AddWithValue("$card", FormatGuid(cardId));
        command.Parameters.AddWithValue("$oracle", DbGuid(ScryfallMapper.OptionalGuid(raw, "oracle_id")));
        command.Parameters.AddWithValue("$illustration", DbGuid(ScryfallMapper.OptionalGuid(raw, "illustration_id")));
        string name = ScryfallMapper.RequiredString(raw, "name");
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$nameKey", name.ToUpperInvariant());
        command.Parameters.AddWithValue("$set", ScryfallMapper.RequiredString(raw, "set").ToLowerInvariant());
        command.Parameters.AddWithValue("$collector", ScryfallMapper.RequiredString(raw, "collector_number"));
        command.Parameters.AddWithValue("$lang", ScryfallMapper.RequiredString(raw, "lang"));
        command.Parameters.AddWithValue("$released", ScryfallMapper.OptionalString(raw, "released_at") ?? string.Empty);
        command.Parameters.AddWithValue("$raw", raw.GetRawText());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (!raw.TryGetProperty("card_faces", out JsonElement faces) || faces.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        int index = 0;
        foreach (JsonElement face in faces.EnumerateArray())
        {
            await using SqliteCommand faceCommand = connection.CreateCommand();
            faceCommand.Transaction = transaction;
            faceCommand.CommandText =
                "INSERT INTO card_faces (generation_id, card_id, ordinal, name_key, illustration_id, raw_json) " +
                "VALUES ($generation, $card, $ordinal, $nameKey, $illustration, $raw);";
            faceCommand.Parameters.AddWithValue("$generation", FormatGuid(generationId));
            faceCommand.Parameters.AddWithValue("$card", FormatGuid(cardId));
            faceCommand.Parameters.AddWithValue("$ordinal", index++);
            faceCommand.Parameters.AddWithValue(
                "$nameKey",
                (ScryfallMapper.OptionalString(face, "name") ?? name).ToUpperInvariant());
            faceCommand.Parameters.AddWithValue("$illustration", DbGuid(ScryfallMapper.OptionalGuid(face, "illustration_id")));
            faceCommand.Parameters.AddWithValue("$raw", face.GetRawText());
            await faceCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Inserts one lossless ruling row.
    /// </summary>
    private static async Task InsertRulingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid generationId,
        JsonElement raw,
        long ordinal,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO rulings " +
            "(generation_id, ordinal, oracle_id, source, published_at, comment, raw_json) " +
            "VALUES ($generation, $ordinal, $oracle, $source, $published, $comment, $raw);";
        command.Parameters.AddWithValue("$generation", FormatGuid(generationId));
        command.Parameters.AddWithValue("$ordinal", ordinal);
        command.Parameters.AddWithValue("$oracle", FormatGuid(ScryfallMapper.RequiredGuid(raw, "oracle_id")));
        command.Parameters.AddWithValue("$source", ScryfallMapper.RequiredString(raw, "source"));
        command.Parameters.AddWithValue("$published", ScryfallMapper.RequiredString(raw, "published_at"));
        command.Parameters.AddWithValue("$comment", ScryfallMapper.RequiredStringAllowEmpty(raw, "comment"));
        command.Parameters.AddWithValue("$raw", raw.GetRawText());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts one tag, its hierarchy, aliases, and direct assignments.
    /// </summary>
    private static async Task InsertTagAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid generationId,
        JsonElement raw,
        string expectedType,
        CancellationToken cancellationToken)
    {
        Guid tagId = ScryfallMapper.RequiredGuid(raw, "id");
        string providerType = ScryfallMapper.RequiredString(raw, "type");
        string tagType = providerType == "illustration" ? "art" : providerType;
        if (!string.Equals(tagType, expectedType, StringComparison.Ordinal))
        {
            throw new InvalidDataException("A tag object appeared in the wrong fixed bulk dataset.");
        }

        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                "INSERT INTO tags (generation_id, tag_id, label, slug, tag_type, description, raw_json) " +
                "VALUES ($generation, $tag, $label, $slug, $type, $description, $raw);";
            command.Parameters.AddWithValue("$generation", FormatGuid(generationId));
            command.Parameters.AddWithValue("$tag", FormatGuid(tagId));
            command.Parameters.AddWithValue("$label", ScryfallMapper.RequiredString(raw, "label"));
            command.Parameters.AddWithValue("$slug", ScryfallMapper.RequiredString(raw, "slug"));
            command.Parameters.AddWithValue("$type", tagType);
            command.Parameters.AddWithValue("$description", ScryfallMapper.OptionalString(raw, "description") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$raw", raw.GetRawText());
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (Guid parentId in ScryfallMapper.Guids(raw, "parent_ids"))
        {
            await InsertTagRelationAsync(connection, transaction, generationId, parentId, tagId, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (string alias in ScryfallMapper.Strings(raw, "aliases"))
        {
            await using SqliteCommand aliasCommand = connection.CreateCommand();
            aliasCommand.Transaction = transaction;
            aliasCommand.CommandText =
                "INSERT INTO tag_aliases (generation_id, tag_id, alias) VALUES ($generation, $tag, $alias);";
            aliasCommand.Parameters.AddWithValue("$generation", FormatGuid(generationId));
            aliasCommand.Parameters.AddWithValue("$tag", FormatGuid(tagId));
            aliasCommand.Parameters.AddWithValue("$alias", alias);
            await aliasCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!raw.TryGetProperty("taggings", out JsonElement taggings) || taggings.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement tagging in taggings.EnumerateArray())
        {
            string targetField = tagType == "oracle" ? "oracle_id" : "illustration_id";
            await using SqliteCommand assignment = connection.CreateCommand();
            assignment.Transaction = transaction;
            assignment.CommandText =
                "INSERT INTO tag_assignments " +
                "(generation_id, tag_id, target_type, target_id, weight, annotation) " +
                "VALUES ($generation, $tag, $type, $target, $weight, $annotation);";
            assignment.Parameters.AddWithValue("$generation", FormatGuid(generationId));
            assignment.Parameters.AddWithValue("$tag", FormatGuid(tagId));
            assignment.Parameters.AddWithValue("$type", tagType);
            assignment.Parameters.AddWithValue("$target", FormatGuid(ScryfallMapper.RequiredGuid(tagging, targetField)));
            string weight = ScryfallMapper.RequiredString(tagging, "weight");
            if (WeightRank(weight) < 0)
            {
                throw new InvalidDataException("A tag assignment contains an unsupported weight.");
            }

            assignment.Parameters.AddWithValue("$weight", weight);
            assignment.Parameters.AddWithValue("$annotation", ScryfallMapper.OptionalString(tagging, "annotation") ?? (object)DBNull.Value);
            await assignment.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Inserts one parent-to-child tag relationship once.
    /// </summary>
    private static async Task InsertTagRelationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid generationId,
        Guid parentId,
        Guid childId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT OR IGNORE INTO tag_relations (generation_id, parent_tag_id, child_tag_id) " +
            "VALUES ($generation, $parent, $child);";
        command.Parameters.AddWithValue("$generation", FormatGuid(generationId));
        command.Parameters.AddWithValue("$parent", FormatGuid(parentId));
        command.Parameters.AddWithValue("$child", FormatGuid(childId));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Validates fixed dataset completeness, joins, and hierarchy acyclicity.
    /// </summary>
    private static async Task<string?> ValidateGenerationAsync(
        SqliteConnection connection,
        Guid generationId,
        CancellationToken cancellationToken)
    {
        await using (SqliteCommand datasets = connection.CreateCommand())
        {
            datasets.CommandText =
                "SELECT dataset_type, row_count FROM corpus_datasets WHERE generation_id = $generation ORDER BY dataset_type;";
            datasets.Parameters.AddWithValue("$generation", FormatGuid(generationId));
            Dictionary<string, long> counts = new(StringComparer.Ordinal);
            await using SqliteDataReader reader = await datasets.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                counts.Add(reader.GetString(0), reader.GetInt64(1));
            }

            string[] required = ["all_cards", "art_tags", "oracle_tags", "rulings"];
            if (required.Any(type => !counts.TryGetValue(type, out long count) || count <= 0))
            {
                return "The Scryfall corpus is missing one or more required datasets.";
            }
        }

        string[] danglingQueries =
        [
            "SELECT 1 FROM rulings r LEFT JOIN card_objects c ON c.generation_id = r.generation_id AND c.oracle_id = r.oracle_id WHERE r.generation_id = $generation AND c.card_id IS NULL LIMIT 1;",
            "SELECT 1 FROM tag_relations x LEFT JOIN tags p ON p.generation_id = x.generation_id AND p.tag_id = x.parent_tag_id LEFT JOIN tags c ON c.generation_id = x.generation_id AND c.tag_id = x.child_tag_id WHERE x.generation_id = $generation AND (p.tag_id IS NULL OR c.tag_id IS NULL) LIMIT 1;",
            "SELECT 1 FROM tag_assignments a WHERE a.generation_id = $generation AND a.target_type = 'oracle' AND NOT EXISTS (SELECT 1 FROM card_objects c WHERE c.generation_id = a.generation_id AND c.oracle_id = a.target_id) LIMIT 1;",
            "SELECT 1 FROM tag_assignments a WHERE a.generation_id = $generation AND a.target_type = 'art' AND NOT EXISTS (SELECT 1 FROM card_objects c WHERE c.generation_id = a.generation_id AND c.illustration_id = a.target_id) AND NOT EXISTS (SELECT 1 FROM card_faces f WHERE f.generation_id = a.generation_id AND f.illustration_id = a.target_id) LIMIT 1;",
        ];
        foreach (string query in danglingQueries)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = query;
            command.Parameters.AddWithValue("$generation", FormatGuid(generationId));
            if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
            {
                return "The Scryfall corpus contains a dangling identity relationship.";
            }
        }

        Dictionary<Guid, List<Guid>> graph = [];
        await using (SqliteCommand edges = connection.CreateCommand())
        {
            edges.CommandText =
                "SELECT parent_tag_id, child_tag_id FROM tag_relations WHERE generation_id = $generation;";
            edges.Parameters.AddWithValue("$generation", FormatGuid(generationId));
            await using SqliteDataReader reader = await edges.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                Guid parent = ParseGuid(reader.GetString(0));
                if (!graph.TryGetValue(parent, out List<Guid>? children))
                {
                    children = [];
                    graph.Add(parent, children);
                }

                children.Add(ParseGuid(reader.GetString(1)));
            }
        }

        HashSet<Guid> visited = [];
        HashSet<Guid> active = [];
        foreach (Guid node in graph.Keys)
        {
            if (HasCycle(node, graph, visited, active))
            {
                return "The Scryfall tag hierarchy contains a cycle.";
            }
        }

        return null;
    }

    /// <summary>
    /// Detects one cycle through depth-first traversal.
    /// </summary>
    private static bool HasCycle(
        Guid node,
        IReadOnlyDictionary<Guid, List<Guid>> graph,
        ISet<Guid> visited,
        ISet<Guid> active)
    {
        if (active.Contains(node))
        {
            return true;
        }

        if (!visited.Add(node))
        {
            return false;
        }

        active.Add(node);
        if (graph.TryGetValue(node, out List<Guid>? children))
        {
            foreach (Guid child in children)
            {
                if (HasCycle(child, graph, visited, active))
                {
                    return true;
                }
            }
        }

        active.Remove(node);
        return false;
    }

    /// <summary>
    /// Reads one generation and its dataset statuses.
    /// </summary>
    private static async Task<ScryfallCorpusGenerationStatus> ReadGenerationAsync(
        SqliteConnection connection,
        Guid generationId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand generation = connection.CreateCommand();
        generation.CommandText =
            "SELECT created_at_utc FROM corpus_generations WHERE generation_id = $generation AND status = 'complete';";
        generation.Parameters.AddWithValue("$generation", FormatGuid(generationId));
        object? created = await generation.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (created is not string createdText)
        {
            throw new InvalidDataException("The active Scryfall generation record is unavailable.");
        }

        await using SqliteCommand datasets = connection.CreateCommand();
        datasets.CommandText =
            "SELECT dataset_type, provider_id, provider_updated_at_utc, row_count, source_bytes, checksum " +
            "FROM corpus_datasets WHERE generation_id = $generation ORDER BY dataset_type;";
        datasets.Parameters.AddWithValue("$generation", FormatGuid(generationId));
        List<ScryfallCorpusDatasetStatus> statuses = [];
        await using SqliteDataReader reader = await datasets.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            statuses.Add(new ScryfallCorpusDatasetStatus(
                reader.GetString(0),
                ParseGuid(reader.GetString(1)),
                ParseUtc(reader.GetString(2)),
                reader.GetInt64(3),
                reader.GetInt64(4),
                reader.GetString(5)));
        }

        return new ScryfallCorpusGenerationStatus(generationId, ParseUtc(createdText), statuses, "valid");
    }

    /// <summary>
    /// Reads one state GUID column.
    /// </summary>
    private static async Task<Guid?> ReadStateGuidAsync(
        SqliteConnection connection,
        string column,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT {column} FROM corpus_state WHERE singleton = 1;";
        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is string text ? ParseGuid(text) : null;
    }

    /// <summary>
    /// Deletes generation-owned rows, optionally inside an existing transaction.
    /// </summary>
    private static async Task DeleteGenerationOnConnectionAsync(
        SqliteConnection connection,
        Guid generationId,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM corpus_generations WHERE generation_id = $generation;";
        command.Parameters.AddWithValue("$generation", FormatGuid(generationId));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Finds one exact tag identity without fuzzy matching.
    /// </summary>
    private static async Task<StoredTag?> FindTagAsync(
        SqliteConnection connection,
        Guid generationId,
        string identity,
        string tagType,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT tag_id, raw_json FROM tags WHERE generation_id = $generation AND tag_type = $type " +
            "AND (tag_id = $identity OR slug = $identity) ORDER BY tag_id LIMIT 2;";
        command.Parameters.AddWithValue("$generation", FormatGuid(generationId));
        command.Parameters.AddWithValue("$type", tagType);
        command.Parameters.AddWithValue("$identity", identity);
        List<StoredTag> matches = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            matches.Add(new StoredTag(ParseGuid(reader.GetString(0)), reader.GetString(1)));
        }

        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>
    /// Computes deterministic shortest descendant paths from one root tag.
    /// </summary>
    private static async Task<Dictionary<Guid, IReadOnlyList<Guid>>> DescendantPathsAsync(
        SqliteConnection connection,
        Guid generationId,
        Guid rootId,
        CancellationToken cancellationToken)
    {
        Dictionary<Guid, List<Guid>> edges = [];
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT parent_tag_id, child_tag_id FROM tag_relations WHERE generation_id = $generation " +
            "ORDER BY parent_tag_id, child_tag_id;";
        command.Parameters.AddWithValue("$generation", FormatGuid(generationId));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            Guid parent = ParseGuid(reader.GetString(0));
            if (!edges.TryGetValue(parent, out List<Guid>? children))
            {
                children = [];
                edges.Add(parent, children);
            }

            children.Add(ParseGuid(reader.GetString(1)));
        }

        Dictionary<Guid, IReadOnlyList<Guid>> paths = new() { [rootId] = [rootId] };
        Queue<Guid> pending = new();
        pending.Enqueue(rootId);
        while (pending.TryDequeue(out Guid parent))
        {
            if (!edges.TryGetValue(parent, out List<Guid>? children))
            {
                continue;
            }

            foreach (Guid child in children)
            {
                IReadOnlyList<Guid> path = [.. paths[parent], child];
                if (!paths.TryGetValue(child, out IReadOnlyList<Guid>? existing) || path.Count < existing.Count)
                {
                    paths[child] = path;
                    pending.Enqueue(child);
                }
            }
        }

        return paths;
    }

    /// <summary>
    /// Configures the shared tag-search predicate and parameters.
    /// </summary>
    private static void ConfigureTagSearch(
        SqliteCommand command,
        Guid generationId,
        string query,
        string? tagType)
    {
        command.CommandText =
            "WHERE t.generation_id = $generation AND ($type IS NULL OR t.tag_type = $type) " +
            "AND (t.tag_id = $exact OR t.slug = $exact OR t.label LIKE $pattern ESCAPE '\\' " +
            "OR t.slug LIKE $pattern ESCAPE '\\' OR EXISTS " +
            "(SELECT 1 FROM tag_aliases a WHERE a.generation_id = t.generation_id " +
            "AND a.tag_id = t.tag_id AND a.alias LIKE $pattern ESCAPE '\\'))";
        command.Parameters.AddWithValue("$generation", FormatGuid(generationId));
        command.Parameters.AddWithValue("$type", tagType is null ? DBNull.Value : tagType);
        command.Parameters.AddWithValue("$exact", query);
        string escaped = query.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        command.Parameters.AddWithValue("$pattern", $"%{escaped}%");
    }

    /// <summary>
    /// Creates a bounded placeholder list for face and root illustration identities.
    /// </summary>
    private static string IllustrationParameters(int count)
    {
        return count == 0
            ? "NULL"
            : string.Join(",", Enumerable.Range(0, count).Select(index => $"$illustration{index}"));
    }

    /// <summary>
    /// Finds the latest snapshot using an existing writable connection.
    /// </summary>
    private static async Task<StoredSnapshot?> FindSnapshotOnConnectionAsync(
        SqliteConnection connection,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT snapshot_id, operation, request_json, retrieved_at_utc, checksum, total_count, predecessor_id " +
            "FROM request_snapshots WHERE fingerprint = $fingerprint " +
            "ORDER BY retrieved_at_utc DESC, snapshot_id DESC LIMIT 1;";
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        StoredSnapshotHeader header = ReadSnapshotHeader(reader);
        await reader.DisposeAsync().ConfigureAwait(false);
        IReadOnlyList<string> members = await ReadSnapshotMembersAsync(connection, header.SnapshotId, cancellationToken)
            .ConfigureAwait(false);
        return new StoredSnapshot(header, members);
    }

    /// <summary>
    /// Reads one snapshot header by exact ID.
    /// </summary>
    private static async Task<StoredSnapshotHeader?> ReadSnapshotHeaderAsync(
        SqliteConnection connection,
        Guid snapshotId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT snapshot_id, operation, request_json, retrieved_at_utc, checksum, total_count, predecessor_id " +
            "FROM request_snapshots WHERE snapshot_id = $id;";
        command.Parameters.AddWithValue("$id", FormatGuid(snapshotId));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadSnapshotHeader(reader)
            : null;
    }

    /// <summary>
    /// Reads one snapshot header from the current data-reader row.
    /// </summary>
    private static StoredSnapshotHeader ReadSnapshotHeader(SqliteDataReader reader)
    {
        return new StoredSnapshotHeader(
            ParseGuid(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            ParseUtc(reader.GetString(3)),
            reader.GetString(4),
            reader.GetInt32(5),
            ReadNullableGuid(reader, 6));
    }

    /// <summary>
    /// Reads all ordered members for one snapshot.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ReadSnapshotMembersAsync(
        SqliteConnection connection,
        Guid snapshotId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT p.raw_json FROM snapshot_members m " +
            "JOIN snapshot_payloads p ON p.checksum = m.checksum " +
            "WHERE m.snapshot_id = $id ORDER BY m.ordinal;";
        command.Parameters.AddWithValue("$id", FormatGuid(snapshotId));
        return await ReadStringsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Hashes the current snapshot summary collection for cursor invalidation.
    /// </summary>
    private static async Task<string> SnapshotCollectionChecksumAsync(
        SqliteConnection connection,
        string? operation,
        DateTimeOffset? retrievedAfterUtc,
        DateTimeOffset? retrievedBeforeUtc,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT COALESCE(GROUP_CONCAT(snapshot_id || ':' || checksum, '|'), '') " +
            "FROM (SELECT snapshot_id, checksum FROM request_snapshots " +
            "WHERE ($operation IS NULL OR operation = $operation) " +
            "AND ($after IS NULL OR retrieved_at_utc >= $after) " +
            "AND ($before IS NULL OR retrieved_at_utc <= $before) " +
            "ORDER BY retrieved_at_utc, snapshot_id);";
        AddSnapshotFilters(command, operation, retrievedAfterUtc, retrievedBeforeUtc);
        string value = (string)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty);
        return Hash(value);
    }

    /// <summary>
    /// Adds the shared optional operation and UTC retrieval bounds to a snapshot query.
    /// </summary>
    private static void AddSnapshotFilters(
        SqliteCommand command,
        string? operation,
        DateTimeOffset? retrievedAfterUtc,
        DateTimeOffset? retrievedBeforeUtc)
    {
        command.Parameters.AddWithValue("$operation", operation is null ? DBNull.Value : operation);
        command.Parameters.AddWithValue(
            "$after",
            retrievedAfterUtc is DateTimeOffset after ? FormatUtc(after) : DBNull.Value);
        command.Parameters.AddWithValue(
            "$before",
            retrievedBeforeUtc is DateTimeOffset before ? FormatUtc(before) : DBNull.Value);
    }

    /// <summary>
    /// Formats an optional UTC bound into a stable cursor-scope component.
    /// </summary>
    private static string FormatOptionalUtc(DateTimeOffset? value)
    {
        return value is DateTimeOffset present ? FormatUtc(present) : "all";
    }

    /// <summary>
    /// Projects an internal header into its safe public summary.
    /// </summary>
    private static ScryfallSnapshotSummary ToSummary(StoredSnapshotHeader header)
    {
        return new ScryfallSnapshotSummary(
            header.SnapshotId,
            header.Operation,
            header.RetrievedAtUtc,
            header.Checksum,
            header.TotalCount,
            header.PredecessorId);
    }

    /// <summary>
    /// Reads a command's first string column into an ordered list.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ReadStringsAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        List<string> results = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }

    /// <summary>
    /// Maps community-tag weights into their exact comparison order.
    /// </summary>
    internal static int WeightRank(string weight)
    {
        return weight switch
        {
            "weak" => 0,
            "median" => 1,
            "strong" => 2,
            "very_strong" or "very-strong" => 3,
            _ => -1,
        };
    }

    /// <summary>
    /// Computes a lowercase SHA-256 digest for immutable identity fields.
    /// </summary>
    internal static string Hash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    /// <summary>
    /// Formats one stable UUID for ordinal SQLite comparisons.
    /// </summary>
    private static string FormatGuid(Guid value)
    {
        return value.ToString("D", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Converts one optional UUID to its SQLite parameter value.
    /// </summary>
    private static object DbGuid(Guid? value)
    {
        return value is Guid id ? FormatGuid(id) : DBNull.Value;
    }

    /// <summary>
    /// Parses one stored UUID or treats the database as corrupt.
    /// </summary>
    private static Guid ParseGuid(string value)
    {
        return Guid.TryParse(value, out Guid parsed)
            ? parsed
            : throw new InvalidDataException("The Scryfall database contains an invalid UUID.");
    }

    /// <summary>
    /// Formats one timestamp in canonical UTC round-trip form.
    /// </summary>
    private static string FormatUtc(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Parses one stored timestamp or treats the database as corrupt.
    /// </summary>
    private static DateTimeOffset ParseUtc(string value)
    {
        return DateTimeOffset.TryParseExact(
            value,
            "O",
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out DateTimeOffset parsed)
            ? parsed.ToUniversalTime()
            : throw new InvalidDataException("The Scryfall database contains an invalid timestamp.");
    }

    /// <summary>
    /// Reads one nullable UUID column.
    /// </summary>
    private static Guid? ReadNullableGuid(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : ParseGuid(reader.GetString(ordinal));
    }

    /// <summary>
    /// Reads one nullable UTC timestamp column.
    /// </summary>
    private static DateTimeOffset? ReadNullableUtc(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : ParseUtc(reader.GetString(ordinal));
    }

    /// <summary>
    /// Releases the process-local schema initialization gate.
    /// </summary>
    public void Dispose()
    {
        initializationGate.Dispose();
    }

    /// <summary>
    /// Declares schema version one and all generation-owned cascade boundaries.
    /// </summary>
    private const string SchemaSql = """
        PRAGMA foreign_keys = ON;
        PRAGMA journal_mode = WAL;
        BEGIN IMMEDIATE;
        CREATE TABLE IF NOT EXISTS schema_migrations (
            version INTEGER PRIMARY KEY,
            applied_at_utc TEXT NOT NULL,
            checksum TEXT NOT NULL
        );
        INSERT OR IGNORE INTO schema_migrations (version, applied_at_utc, checksum)
        VALUES (1, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'),
            '93f5f609eff2ec0b7cf25cc0155075e8b05e100c39ed1b44fa8319ede3fbed9c');
        CREATE TABLE IF NOT EXISTS corpus_generations (
            generation_id TEXT PRIMARY KEY,
            created_at_utc TEXT NOT NULL,
            activated_at_utc TEXT NULL,
            status TEXT NOT NULL CHECK (status IN ('staging', 'complete'))
        );
        CREATE TABLE IF NOT EXISTS corpus_state (
            singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
            active_generation_id TEXT NULL REFERENCES corpus_generations(generation_id),
            previous_generation_id TEXT NULL REFERENCES corpus_generations(generation_id),
            last_metadata_check_utc TEXT NULL
        );
        INSERT OR IGNORE INTO corpus_state (singleton) VALUES (1);
        CREATE TABLE IF NOT EXISTS corpus_datasets (
            generation_id TEXT NOT NULL REFERENCES corpus_generations(generation_id) ON DELETE CASCADE,
            dataset_type TEXT NOT NULL,
            provider_id TEXT NOT NULL,
            provider_updated_at_utc TEXT NOT NULL,
            source_bytes INTEGER NOT NULL,
            row_count INTEGER NOT NULL,
            checksum TEXT NOT NULL,
            PRIMARY KEY (generation_id, dataset_type)
        );
        CREATE TABLE IF NOT EXISTS card_objects (
            generation_id TEXT NOT NULL REFERENCES corpus_generations(generation_id) ON DELETE CASCADE,
            card_id TEXT NOT NULL,
            oracle_id TEXT NULL,
            illustration_id TEXT NULL,
            name TEXT NOT NULL,
            name_key TEXT NOT NULL,
            set_code TEXT NOT NULL,
            collector_number TEXT NOT NULL,
            lang TEXT NOT NULL,
            released_at TEXT NOT NULL,
            raw_json TEXT NOT NULL,
            PRIMARY KEY (generation_id, card_id)
        );
        CREATE INDEX IF NOT EXISTS ix_cards_oracle ON card_objects(generation_id, oracle_id);
        CREATE INDEX IF NOT EXISTS ix_cards_name ON card_objects(generation_id, name_key);
        CREATE INDEX IF NOT EXISTS ix_cards_printing ON card_objects(generation_id, set_code, collector_number);
        CREATE INDEX IF NOT EXISTS ix_cards_illustration ON card_objects(generation_id, illustration_id);
        CREATE TABLE IF NOT EXISTS card_faces (
            generation_id TEXT NOT NULL,
            card_id TEXT NOT NULL,
            ordinal INTEGER NOT NULL,
            name_key TEXT NOT NULL,
            illustration_id TEXT NULL,
            raw_json TEXT NOT NULL,
            PRIMARY KEY (generation_id, card_id, ordinal),
            FOREIGN KEY (generation_id, card_id) REFERENCES card_objects(generation_id, card_id) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS ix_faces_name ON card_faces(generation_id, name_key);
        CREATE INDEX IF NOT EXISTS ix_faces_illustration ON card_faces(generation_id, illustration_id);
        CREATE TABLE IF NOT EXISTS rulings (
            generation_id TEXT NOT NULL REFERENCES corpus_generations(generation_id) ON DELETE CASCADE,
            ordinal INTEGER NOT NULL,
            oracle_id TEXT NOT NULL,
            source TEXT NOT NULL,
            published_at TEXT NOT NULL,
            comment TEXT NOT NULL,
            raw_json TEXT NOT NULL,
            PRIMARY KEY (generation_id, ordinal)
        );
        CREATE INDEX IF NOT EXISTS ix_rulings_oracle ON rulings(generation_id, oracle_id, published_at);
        CREATE TABLE IF NOT EXISTS tags (
            generation_id TEXT NOT NULL REFERENCES corpus_generations(generation_id) ON DELETE CASCADE,
            tag_id TEXT NOT NULL,
            label TEXT NOT NULL,
            slug TEXT NOT NULL,
            tag_type TEXT NOT NULL CHECK (tag_type IN ('oracle', 'art')),
            description TEXT NULL,
            raw_json TEXT NOT NULL,
            PRIMARY KEY (generation_id, tag_id),
            UNIQUE (generation_id, tag_type, slug)
        );
        CREATE TABLE IF NOT EXISTS tag_relations (
            generation_id TEXT NOT NULL REFERENCES corpus_generations(generation_id) ON DELETE CASCADE,
            parent_tag_id TEXT NOT NULL,
            child_tag_id TEXT NOT NULL,
            PRIMARY KEY (generation_id, parent_tag_id, child_tag_id)
        );
        CREATE TABLE IF NOT EXISTS tag_aliases (
            generation_id TEXT NOT NULL REFERENCES corpus_generations(generation_id) ON DELETE CASCADE,
            tag_id TEXT NOT NULL,
            alias TEXT NOT NULL,
            PRIMARY KEY (generation_id, tag_id, alias)
        );
        CREATE TABLE IF NOT EXISTS tag_assignments (
            generation_id TEXT NOT NULL REFERENCES corpus_generations(generation_id) ON DELETE CASCADE,
            tag_id TEXT NOT NULL,
            target_type TEXT NOT NULL CHECK (target_type IN ('oracle', 'art')),
            target_id TEXT NOT NULL,
            weight TEXT NOT NULL,
            annotation TEXT NULL,
            PRIMARY KEY (generation_id, tag_id, target_type, target_id)
        );
        CREATE INDEX IF NOT EXISTS ix_tag_assignment_target ON tag_assignments(generation_id, target_type, target_id);
        CREATE TABLE IF NOT EXISTS request_snapshots (
            snapshot_id TEXT PRIMARY KEY,
            operation TEXT NOT NULL,
            request_json TEXT NOT NULL,
            fingerprint TEXT NOT NULL,
            retrieved_at_utc TEXT NOT NULL,
            checksum TEXT NOT NULL,
            total_count INTEGER NOT NULL,
            predecessor_id TEXT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_snapshot_fingerprint ON request_snapshots(fingerprint, retrieved_at_utc DESC);
        CREATE TABLE IF NOT EXISTS snapshot_payloads (
            checksum TEXT PRIMARY KEY,
            raw_json TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS snapshot_pages (
            snapshot_id TEXT NOT NULL REFERENCES request_snapshots(snapshot_id) ON DELETE CASCADE,
            ordinal INTEGER NOT NULL,
            checksum TEXT NOT NULL REFERENCES snapshot_payloads(checksum),
            PRIMARY KEY (snapshot_id, ordinal)
        );
        CREATE TABLE IF NOT EXISTS snapshot_members (
            snapshot_id TEXT NOT NULL REFERENCES request_snapshots(snapshot_id) ON DELETE CASCADE,
            ordinal INTEGER NOT NULL,
            checksum TEXT NOT NULL REFERENCES snapshot_payloads(checksum),
            PRIMARY KEY (snapshot_id, ordinal)
        );
        CREATE TABLE IF NOT EXISTS acquisition_leases (
            lease_key TEXT PRIMARY KEY,
            owner_id TEXT NOT NULL,
            expires_at_utc TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS provider_pacing (
            singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
            next_start_utc TEXT NULL
        );
        INSERT OR IGNORE INTO provider_pacing (singleton) VALUES (1);
        COMMIT;
        """;
}

/// <summary>
/// Carries one lossless corpus object and its active generation.
/// </summary>
internal sealed record StoredCorpusObject(
    Guid GenerationId,
    DateTimeOffset RetrievedAtUtc,
    DateTimeOffset ProviderUpdatedAtUtc,
    string RawJson);

/// <summary>
/// Carries one ordered corpus result collection with generation provenance.
/// </summary>
internal sealed record StoredCorpusCollection(
    Guid GenerationId,
    DateTimeOffset RetrievedAtUtc,
    DateTimeOffset ProviderUpdatedAtUtc,
    IReadOnlyList<string> Items);

/// <summary>
/// Carries one immutable request snapshot header.
/// </summary>
internal sealed record StoredSnapshotHeader(
    Guid SnapshotId,
    string Operation,
    string RequestJson,
    DateTimeOffset RetrievedAtUtc,
    string Checksum,
    int TotalCount,
    Guid? PredecessorId);

/// <summary>
/// Carries one complete stored request snapshot.
/// </summary>
internal sealed record StoredSnapshot(StoredSnapshotHeader Header, IReadOnlyList<string> Members);

/// <summary>
/// Carries one exact installed tag identity.
/// </summary>
internal sealed record StoredTag(Guid Id, string RawJson);

/// <summary>
/// Carries one card assignment while preserving its supporting tag path.
/// </summary>
internal sealed record StoredTagAssignment(
    Guid TagId,
    string Label,
    string Slug,
    string TagType,
    string Weight,
    string? Annotation,
    string Relationship,
    IReadOnlyList<Guid> Path,
    string CardJson);

/// <summary>
/// Carries one tag root and all matching stored cards.
/// </summary>
internal sealed record StoredCardsByTag(
    Guid GenerationId,
    DateTimeOffset RetrievedAtUtc,
    DateTimeOffset ProviderUpdatedAtUtc,
    string TagJson,
    IReadOnlyList<StoredTagAssignment> Assignments);
