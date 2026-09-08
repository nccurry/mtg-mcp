using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MtgMcp.Core.Evidence;
using MtgMcp.Core.Results;

namespace MtgMcp.Scryfall;

/// <summary>
/// Reads and changes installed Scryfall card data.
/// </summary>
internal sealed class ScryfallCardDataStore
{
    /// <summary>
    /// Stores the shared SQLite connection and schema support.
    /// </summary>
    private readonly ScryfallDatabase database;

    /// <summary>
    /// Creates card-data storage around the shared SQLite support.
    /// </summary>
    internal ScryfallCardDataStore(ScryfallDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        this.database = database;
    }

    /// <summary>
    /// Gets the active generation identifier when complete card data is installed.
    /// </summary>
    internal async Task<Guid?> GetActiveGenerationIdAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection? connection = await database.OpenReadAsync(cancellationToken).ConfigureAwait(false);
        if (connection is null)
        {
            return null;
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT active_generation_id FROM corpus_state WHERE singleton = 1;";
        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is string text ? ScryfallSql.ParseGuid(text) : null;
    }

    /// <summary>
    /// Returns local card-data status without creating storage.
    /// </summary>
    internal async Task<OperationResult<ScryfallCorpusStatus>> GetStatusAsync(
        DateTimeOffset nowUtc,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection? connection = await database.OpenReadAsync(cancellationToken).ConfigureAwait(false);
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
    /// Finds an installed card through one validated lookup case.
    /// </summary>
    internal async Task<StoredCardDataObject?> FindCardAsync(
        ScryfallCardLookup lookup,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection? connection = await database.OpenReadAsync(cancellationToken).ConfigureAwait(false);
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

        return await FindCardOnConnectionAsync(connection, generationId.Value, lookup, null, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Finds an installed card in one exact retained generation for stable cursor replay.
    /// </summary>
    internal async Task<StoredCardDataObject?> FindCardInGenerationAsync(
        ScryfallCardLookup lookup,
        Guid generationId,
        CancellationToken cancellationToken)
    {
        return await FindCardInGenerationAsync(
            lookup,
            generationId,
            null,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Finds an installed card in one retained generation with an optional exact printing language.
    /// </summary>
    internal async Task<StoredCardDataObject?> FindCardInGenerationAsync(
        ScryfallCardLookup lookup,
        Guid generationId,
        string? requiredLanguage,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection? connection = await database.OpenReadAsync(cancellationToken).ConfigureAwait(false);
        return connection is null
            ? null
            : await FindCardOnConnectionAsync(
                connection,
                generationId,
                lookup,
                requiredLanguage,
                cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reports whether one complete card-data generation remains available for cursor replay.
    /// </summary>
    internal async Task<bool> ContainsCompleteGenerationAsync(
        Guid generationId,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection? connection = await database.OpenReadAsync(cancellationToken).ConfigureAwait(false);
        if (connection is null)
        {
            return false;
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT 1 FROM corpus_generations WHERE generation_id = $generation AND status = 'complete' LIMIT 1;";
        command.Parameters.AddWithValue("$generation", ScryfallSql.FormatGuid(generationId));
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    /// <summary>
    /// Executes one validated card lookup against a caller-selected card-data generation.
    /// </summary>
    private static async Task<StoredCardDataObject?> FindCardOnConnectionAsync(
        SqliteConnection connection,
        Guid generationId,
        ScryfallCardLookup lookup,
        string? requiredLanguage,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Parameters.AddWithValue("$generation", ScryfallSql.FormatGuid(generationId));
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
                    "AND ($language IS NULL OR c.lang = $language) " +
                    "ORDER BY c.lang = 'en' DESC, c.card_id LIMIT 1;";
                command.Parameters.AddWithValue("$set", lookup.SetCode!.Trim().ToLowerInvariant());
                command.Parameters.AddWithValue("$collector", lookup.CollectorNumber!.Trim());
                command.Parameters.AddWithValue(
                    "$language",
                    string.IsNullOrWhiteSpace(requiredLanguage)
                        ? DBNull.Value
                        : requiredLanguage.Trim().ToLowerInvariant());
                break;
            default:
                return null;
        }

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new StoredCardDataObject(
                generationId,
                ScryfallSql.ParseUtc(reader.GetString(1)),
                ScryfallSql.ParseUtc(reader.GetString(2)),
                reader.GetString(0))
            : null;
    }

    /// <summary>
    /// Returns every printing for one Oracle identity in stable provider order.
    /// </summary>
    internal async Task<StoredCardDataCollection?> GetPrintsAsync(
        Guid oracleId,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection? connection = await database.OpenReadAsync(cancellationToken).ConfigureAwait(false);
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
        command.Parameters.AddWithValue("$generation", ScryfallSql.FormatGuid(generationId.Value));
        command.Parameters.AddWithValue("$oracle", ScryfallSql.FormatGuid(oracleId));
        IReadOnlyList<string> items = await ReadStringsAsync(command, cancellationToken).ConfigureAwait(false);
        ScryfallCorpusGenerationStatus generation = await ReadGenerationAsync(connection, generationId.Value, cancellationToken)
            .ConfigureAwait(false);
        DateTimeOffset providerUpdatedAtUtc = generation.Datasets
            .Single(value => value.Type == "all_cards")
            .ProviderUpdatedAtUtc;
        return new StoredCardDataCollection(generationId.Value, generation.CreatedAtUtc, providerUpdatedAtUtc, items);
    }

    /// <summary>
    /// Returns every ruling for one Oracle identity in provider order.
    /// </summary>
    internal async Task<StoredCardDataCollection?> GetRulingsAsync(
        Guid oracleId,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection? connection = await database.OpenReadAsync(cancellationToken).ConfigureAwait(false);
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
        command.Parameters.AddWithValue("$generation", ScryfallSql.FormatGuid(generationId.Value));
        command.Parameters.AddWithValue("$oracle", ScryfallSql.FormatGuid(oracleId));
        IReadOnlyList<string> items = await ReadStringsAsync(command, cancellationToken).ConfigureAwait(false);
        ScryfallCorpusGenerationStatus generation = await ReadGenerationAsync(connection, generationId.Value, cancellationToken)
            .ConfigureAwait(false);
        DateTimeOffset providerUpdatedAtUtc = generation.Datasets
            .Single(value => value.Type == "rulings")
            .ProviderUpdatedAtUtc;
        return new StoredCardDataCollection(generationId.Value, generation.CreatedAtUtc, providerUpdatedAtUtc, items);
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

        await using SqliteConnection? connection = await database.OpenReadAsync(cancellationToken).ConfigureAwait(false);
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
    /// Resolves exact tag identities in the active installed card-data generation.
    /// </summary>
    internal async Task<OperationResult<ScryfallTagResolution>> ResolveTagIdentitiesAsync(
        IReadOnlyList<ScryfallTagIdentity>? identities,
        CancellationToken cancellationToken)
    {
        if (identities is null || identities.Count == 0)
        {
            return new OperationInvalidInput(
                "invalid-scryfall-tag-identity",
                "At least one exact Scryfall tag identity is required.");
        }

        await using SqliteConnection? connection = await database.OpenReadAsync(cancellationToken).ConfigureAwait(false);
        if (connection is null)
        {
            return CardDataMissing();
        }

        Guid? generationId = await ReadStateGuidAsync(connection, "active_generation_id", cancellationToken)
            .ConfigureAwait(false);
        if (generationId is null || !await ContainsCompleteGenerationOnConnectionAsync(
                connection,
                generationId.Value,
                cancellationToken).ConfigureAwait(false))
        {
            return CardDataMissing();
        }

        List<Guid> resolved = [];
        foreach (ScryfallTagIdentity identity in identities)
        {
            OperationInvalidInput? failure = ValidateTagIdentity(identity);
            if (failure is not null)
            {
                return failure;
            }

            IReadOnlyList<Guid> matches = await FindExactTagIdsAsync(
                connection,
                generationId.Value,
                identity,
                cancellationToken).ConfigureAwait(false);
            if (matches.Count == 0)
            {
                return new OperationNotFound(
                    "scryfall-tag-not-found",
                    "An exact Scryfall tag used by the category rule was not found.");
            }

            if (matches.Count > 1)
            {
                return new OperationUnavailable(
                    "scryfall-tag-ambiguous",
                    "An exact Scryfall tag used by the category rule matched more than one installed tag.");
            }

            resolved.Add(matches[0]);
        }

        return new OperationSuccess<ScryfallTagResolution>(new ScryfallTagResolution(generationId.Value, resolved));
    }

    /// <summary>
    /// Reads direct tag assignments and all source ancestors for ordered deck card lookups.
    /// </summary>
    internal async Task<OperationResult<ScryfallDeckTagEvidence>> ReadDeckTagEvidenceAsync(
        Guid generationId,
        IReadOnlyList<ScryfallCardLookup>? lookups,
        CancellationToken cancellationToken)
    {
        if (lookups is null)
        {
            return new OperationInvalidInput("invalid-card-lookup", "Deck card lookups are required.");
        }

        foreach (ScryfallCardLookup lookup in lookups)
        {
            OperationInvalidInput? failure = ScryfallCardEvidenceOperations.ValidateLookup(lookup);
            if (failure is not null)
            {
                return failure;
            }
        }

        await using SqliteConnection? connection = await database.OpenReadAsync(cancellationToken).ConfigureAwait(false);
        if (connection is null || !await ContainsCompleteGenerationOnConnectionAsync(
                connection,
                generationId,
                cancellationToken).ConfigureAwait(false))
        {
            return CardDataMissing();
        }

        List<(bool IsComplete, IReadOnlyList<ScryfallTagEvidence> Tags)> directEvidence = [];
        HashSet<Guid> directTagIds = [];
        foreach (ScryfallCardLookup lookup in lookups)
        {
            StoredCardDataObject? card = await FindCardOnConnectionAsync(
                connection,
                generationId,
                lookup,
                null,
                cancellationToken).ConfigureAwait(false);
            if (card is null)
            {
                directEvidence.Add((false, []));
                continue;
            }

            using JsonDocument document = JsonDocument.Parse(card.RawJson);
            ScryfallTagTargets targets = ReadTagTargets(document.RootElement);
            IReadOnlyList<ScryfallTagEvidence> tags = await GetDirectTagsOnConnectionAsync(
                connection,
                generationId,
                targets.OracleId,
                targets.IllustrationIds,
                card.RetrievedAtUtc,
                cancellationToken).ConfigureAwait(false);
            List<ScryfallTagEvidence> strongestByTag = tags
                .GroupBy(value => value.TagId)
                .Select(group => group
                    .OrderByDescending(value => ScryfallTagWeight.Rank(value.Weight))
                    .ThenBy(value => value.TagType, StringComparer.Ordinal)
                    .ThenBy(value => value.Slug, StringComparer.Ordinal)
                    .First())
                .OrderBy(value => value.TagType, StringComparer.Ordinal)
                .ThenBy(value => value.Slug, StringComparer.Ordinal)
                .ThenBy(value => value.TagId)
                .ToList();
            foreach (ScryfallTagEvidence tag in strongestByTag)
            {
                directTagIds.Add(tag.TagId);
            }

            directEvidence.Add((true, strongestByTag));
        }

        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> ancestors = await GetAncestorTagIdsAsync(
            connection,
            generationId,
            directTagIds,
            cancellationToken).ConfigureAwait(false);
        List<ScryfallDeckTagEntryEvidence> entries = [];
        foreach ((bool isComplete, IReadOnlyList<ScryfallTagEvidence> tags) in directEvidence)
        {
            entries.Add(new ScryfallDeckTagEntryEvidence(
                isComplete,
                tags.Select(tag => new ScryfallDirectTagEvidence(
                    tag.TagId,
                    tag.TagType,
                    tag.Slug,
                    tag.Weight,
                    ancestors[tag.TagId])).ToArray()));
        }

        return new OperationSuccess<ScryfallDeckTagEvidence>(new ScryfallDeckTagEvidence(generationId, entries));
    }

    /// <summary>
    /// Reads one card's Oracle and illustration identities from its source object.
    /// </summary>
    internal static ScryfallTagTargets ReadTagTargets(JsonElement raw)
    {
        Guid? oracleId = ScryfallMapper.OptionalGuid(raw, "oracle_id");
        List<Guid> illustrationIds = [];
        if (ScryfallMapper.OptionalGuid(raw, "illustration_id") is Guid illustrationId)
        {
            illustrationIds.Add(illustrationId);
        }

        if (raw.TryGetProperty("card_faces", out JsonElement faces) && faces.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement face in faces.EnumerateArray())
            {
                if (ScryfallMapper.OptionalGuid(face, "illustration_id") is Guid faceIllustrationId &&
                    !illustrationIds.Contains(faceIllustrationId))
                {
                    illustrationIds.Add(faceIllustrationId);
                }
            }
        }

        return new ScryfallTagTargets(oracleId, illustrationIds);
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
        command.Parameters.AddWithValue("$generation", ScryfallSql.FormatGuid(generationId));
        command.Parameters.AddWithValue("$oracle", oracleId is Guid oracle ? ScryfallSql.FormatGuid(oracle) : DBNull.Value);
        for (int index = 0; index < illustrationIds.Count; index++)
        {
            command.Parameters.AddWithValue($"$illustration{index}", ScryfallSql.FormatGuid(illustrationIds[index]));
        }
        List<ScryfallTagEvidence> results = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            Guid tagId = ScryfallSql.ParseGuid(reader.GetString(0));
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
                    ScryfallSql.FormatGuid(tagId),
                    ScryfallSql.FormatGuid(generationId))));
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
        await using SqliteConnection? connection = await database.OpenReadAsync(cancellationToken).ConfigureAwait(false);
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
        string checksum = ScryfallHash.Compute(scope);
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
        await using SqliteConnection? connection = await database.OpenReadAsync(cancellationToken).ConfigureAwait(false);
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
            command.Parameters.AddWithValue("$generation", ScryfallSql.FormatGuid(generationId.Value));
            command.Parameters.AddWithValue("$tag", ScryfallSql.FormatGuid(tagId));
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                string weight = reader.GetString(1);
                if (ScryfallTagWeight.Rank(weight) < ScryfallTagWeight.Rank(minimumWeight))
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
    /// Begins one invisible staging generation.
    /// </summary>
    internal async Task<Guid> BeginGenerationAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await database.OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        Guid generationId = Guid.NewGuid();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO corpus_generations (generation_id, created_at_utc, status) VALUES ($id, $created, 'staging');";
        command.Parameters.AddWithValue("$id", ScryfallSql.FormatGuid(generationId));
        command.Parameters.AddWithValue("$created", ScryfallSql.FormatUtc(nowUtc));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return generationId;
    }

    /// <summary>
    /// Removes staging generations abandoned by an earlier process after its synchronization lease expired.
    /// </summary>
    internal async Task RemoveAbandonedStagingGenerationsAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await database.OpenWriteAsync(cancellationToken).ConfigureAwait(false);
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
        await using SqliteConnection connection = await database.OpenWriteAsync(cancellationToken).ConfigureAwait(false);
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
            await InsertCardDataObjectAsync(
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
        dataset.Parameters.AddWithValue("$generation", ScryfallSql.FormatGuid(generationId));
        dataset.Parameters.AddWithValue("$type", metadata.Type);
        dataset.Parameters.AddWithValue("$provider", ScryfallSql.FormatGuid(metadata.Id));
        dataset.Parameters.AddWithValue("$updated", ScryfallSql.FormatUtc(metadata.UpdatedAtUtc));
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
        await using SqliteConnection connection = await database.OpenWriteAsync(cancellationToken).ConfigureAwait(false);
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
            generation.Parameters.AddWithValue("$activated", ScryfallSql.FormatUtc(checkedAtUtc));
            generation.Parameters.AddWithValue("$generation", ScryfallSql.FormatGuid(generationId));
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
            state.Parameters.AddWithValue("$active", ScryfallSql.FormatGuid(generationId));
            state.Parameters.AddWithValue("$previous", oldActive is Guid active ? ScryfallSql.FormatGuid(active) : DBNull.Value);
            state.Parameters.AddWithValue("$checked", ScryfallSql.FormatUtc(checkedAtUtc));
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
        await using SqliteConnection connection = await database.OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        await DeleteGenerationOnConnectionAsync(connection, generationId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reports whether official metadata exactly matches the active generation.
    /// </summary>
    internal async Task<bool> ActiveMetadataMatchesAsync(
        IReadOnlyList<ScryfallBulkData> datasets,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection? connection = await database.OpenReadAsync(cancellationToken).ConfigureAwait(false);
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
        command.Parameters.AddWithValue("$generation", ScryfallSql.FormatGuid(active.Value));
        Dictionary<string, (Guid Id, DateTimeOffset Updated)> installed = new(StringComparer.Ordinal);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            installed.Add(reader.GetString(0), (ScryfallSql.ParseGuid(reader.GetString(1)), ScryfallSql.ParseUtc(reader.GetString(2))));
        }

        return datasets.Count == installed.Count && datasets.All(dataset =>
            installed.TryGetValue(dataset.Type, out (Guid Id, DateTimeOffset Updated) value) &&
            value.Id == dataset.Id && value.Updated == dataset.UpdatedAtUtc.ToUniversalTime());
    }

    /// <summary>
    /// Records a successful metadata check without replacing installed card data.
    /// </summary>
    internal async Task RecordMetadataCheckAsync(DateTimeOffset checkedAtUtc, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await database.OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE corpus_state SET last_metadata_check_utc = $checked WHERE singleton = 1;";
        command.Parameters.AddWithValue("$checked", ScryfallSql.FormatUtc(checkedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Swaps current and previous complete generations under exact identity guards.
    /// </summary>
    internal async Task<OperationResult<ScryfallCorpusMutationResult>> RollbackAsync(
        Guid expectedActive,
        Guid expectedPrevious,
        bool acknowledgeActivationChange,
        CancellationToken cancellationToken)
    {
        if (!acknowledgeActivationChange)
        {
            return new OperationInvalidInput("activation-change-not-acknowledged", "Corpus rollback requires explicit acknowledgement.");
        }

        await using SqliteConnection connection = await database.OpenWriteAsync(cancellationToken).ConfigureAwait(false);
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
        command.Parameters.AddWithValue("$active", ScryfallSql.FormatGuid(expectedPrevious));
        command.Parameters.AddWithValue("$previous", ScryfallSql.FormatGuid(expectedActive));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return new OperationSuccess<ScryfallCorpusMutationResult>(
            new ScryfallCorpusMutationResult("rolled-back", expectedPrevious, expectedActive));
    }

    /// <summary>
    /// Deletes active and previous card-data generations under an exact active-identity guard.
    /// </summary>
    internal async Task<OperationResult<ScryfallCorpusMutationResult>> DeleteAsync(
        Guid expectedActive,
        bool acknowledgeDataLoss,
        CancellationToken cancellationToken)
    {
        if (!acknowledgeDataLoss)
        {
            return new OperationInvalidInput("data-loss-not-acknowledged", "Corpus deletion requires explicit acknowledgement.");
        }

        await using SqliteConnection connection = await database.OpenWriteAsync(cancellationToken).ConfigureAwait(false);
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
    /// Inserts one dataset object into its generation-owned normalized tables.
    /// </summary>
    private static async Task InsertCardDataObjectAsync(
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
        command.Parameters.AddWithValue("$generation", ScryfallSql.FormatGuid(generationId));
        command.Parameters.AddWithValue("$card", ScryfallSql.FormatGuid(cardId));
        command.Parameters.AddWithValue("$oracle", ScryfallSql.OptionalGuid(ScryfallMapper.OptionalGuid(raw, "oracle_id")));
        command.Parameters.AddWithValue("$illustration", ScryfallSql.OptionalGuid(ScryfallMapper.OptionalGuid(raw, "illustration_id")));
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
            faceCommand.Parameters.AddWithValue("$generation", ScryfallSql.FormatGuid(generationId));
            faceCommand.Parameters.AddWithValue("$card", ScryfallSql.FormatGuid(cardId));
            faceCommand.Parameters.AddWithValue("$ordinal", index++);
            faceCommand.Parameters.AddWithValue(
                "$nameKey",
                (ScryfallMapper.OptionalString(face, "name") ?? name).ToUpperInvariant());
            faceCommand.Parameters.AddWithValue("$illustration", ScryfallSql.OptionalGuid(ScryfallMapper.OptionalGuid(face, "illustration_id")));
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
        command.Parameters.AddWithValue("$generation", ScryfallSql.FormatGuid(generationId));
        command.Parameters.AddWithValue("$ordinal", ordinal);
        command.Parameters.AddWithValue("$oracle", ScryfallSql.FormatGuid(ScryfallMapper.RequiredGuid(raw, "oracle_id")));
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
            command.Parameters.AddWithValue("$generation", ScryfallSql.FormatGuid(generationId));
            command.Parameters.AddWithValue("$tag", ScryfallSql.FormatGuid(tagId));
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
            aliasCommand.Parameters.AddWithValue("$generation", ScryfallSql.FormatGuid(generationId));
            aliasCommand.Parameters.AddWithValue("$tag", ScryfallSql.FormatGuid(tagId));
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
            assignment.Parameters.AddWithValue("$generation", ScryfallSql.FormatGuid(generationId));
            assignment.Parameters.AddWithValue("$tag", ScryfallSql.FormatGuid(tagId));
            assignment.Parameters.AddWithValue("$type", tagType);
            assignment.Parameters.AddWithValue("$target", ScryfallSql.FormatGuid(ScryfallMapper.RequiredGuid(tagging, targetField)));
            string weight = ScryfallMapper.RequiredString(tagging, "weight");
            if (ScryfallTagWeight.Rank(weight) < 0)
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
        command.Parameters.AddWithValue("$generation", ScryfallSql.FormatGuid(generationId));
        command.Parameters.AddWithValue("$parent", ScryfallSql.FormatGuid(parentId));
        command.Parameters.AddWithValue("$child", ScryfallSql.FormatGuid(childId));
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
            datasets.Parameters.AddWithValue("$generation", ScryfallSql.FormatGuid(generationId));
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
            command.Parameters.AddWithValue("$generation", ScryfallSql.FormatGuid(generationId));
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
            edges.Parameters.AddWithValue("$generation", ScryfallSql.FormatGuid(generationId));
            await using SqliteDataReader reader = await edges.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                Guid parent = ScryfallSql.ParseGuid(reader.GetString(0));
                if (!graph.TryGetValue(parent, out List<Guid>? children))
                {
                    children = [];
                    graph.Add(parent, children);
                }

                children.Add(ScryfallSql.ParseGuid(reader.GetString(1)));
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
        generation.Parameters.AddWithValue("$generation", ScryfallSql.FormatGuid(generationId));
        object? created = await generation.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (created is not string createdText)
        {
            throw new InvalidDataException("The active Scryfall generation record is unavailable.");
        }

        await using SqliteCommand datasets = connection.CreateCommand();
        datasets.CommandText =
            "SELECT dataset_type, provider_id, provider_updated_at_utc, row_count, source_bytes, checksum " +
            "FROM corpus_datasets WHERE generation_id = $generation ORDER BY dataset_type;";
        datasets.Parameters.AddWithValue("$generation", ScryfallSql.FormatGuid(generationId));
        List<ScryfallCorpusDatasetStatus> statuses = [];
        await using SqliteDataReader reader = await datasets.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            statuses.Add(new ScryfallCorpusDatasetStatus(
                reader.GetString(0),
                ScryfallSql.ParseGuid(reader.GetString(1)),
                ScryfallSql.ParseUtc(reader.GetString(2)),
                reader.GetInt64(3),
                reader.GetInt64(4),
                reader.GetString(5)));
        }

        return new ScryfallCorpusGenerationStatus(generationId, ScryfallSql.ParseUtc(createdText), statuses, "valid");
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
        return value is string text ? ScryfallSql.ParseGuid(text) : null;
    }

    /// <summary>
    /// Reports whether a complete generation remains available through an already opened connection.
    /// </summary>
    private static async Task<bool> ContainsCompleteGenerationOnConnectionAsync(
        SqliteConnection connection,
        Guid generationId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT 1 FROM corpus_generations WHERE generation_id = $generation AND status = 'complete' LIMIT 1;";
        command.Parameters.AddWithValue("$generation", ScryfallSql.FormatGuid(generationId));
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    /// <summary>
    /// Finds exact tag identifiers without label or alias matching.
    /// </summary>
    private static async Task<IReadOnlyList<Guid>> FindExactTagIdsAsync(
        SqliteConnection connection,
        Guid generationId,
        ScryfallTagIdentity identity,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        bool usesId = identity.TagId is Guid;
        command.CommandText = usesId
            ? "SELECT tag_id FROM tags WHERE generation_id = $generation AND tag_type = $type AND tag_id = $value ORDER BY tag_id LIMIT 2;"
            : "SELECT tag_id FROM tags WHERE generation_id = $generation AND tag_type = $type AND slug = $value ORDER BY tag_id LIMIT 2;";
        command.Parameters.AddWithValue("$generation", ScryfallSql.FormatGuid(generationId));
        command.Parameters.AddWithValue("$type", identity.TagType);
        command.Parameters.AddWithValue(
            "$value",
            usesId ? ScryfallSql.FormatGuid(identity.TagId!.Value) : identity.ExactSlug!);
        List<Guid> matches = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            matches.Add(ScryfallSql.ParseGuid(reader.GetString(0)));
        }

        return matches;
    }

    /// <summary>
    /// Returns every source parent reachable from each direct tag in deterministic order.
    /// </summary>
    private static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetAncestorTagIdsAsync(
        SqliteConnection connection,
        Guid generationId,
        IEnumerable<Guid> directTagIds,
        CancellationToken cancellationToken)
    {
        List<Guid> requestedTagIds = directTagIds.Distinct().ToList();
        requestedTagIds.Sort();
        Dictionary<Guid, List<Guid>> ancestors = requestedTagIds.ToDictionary(value => value, _ => new List<Guid>());
        if (ancestors.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<Guid>>();
        }

        await using SqliteCommand command = connection.CreateCommand();
        string[] values = requestedTagIds.Select((_, index) => $"($tag{index})").ToArray();
        command.CommandText =
            "WITH RECURSIVE requested(tag_id) AS (VALUES " + string.Join(", ", values) + "), " +
            "ancestors(direct_tag_id, ancestor_tag_id) AS (" +
            "SELECT requested.tag_id, relation.parent_tag_id " +
            "FROM requested JOIN tag_relations relation " +
            "ON relation.generation_id = $generation AND relation.child_tag_id = requested.tag_id " +
            "UNION " +
            "SELECT ancestors.direct_tag_id, relation.parent_tag_id " +
            "FROM ancestors JOIN tag_relations relation " +
            "ON relation.generation_id = $generation AND relation.child_tag_id = ancestors.ancestor_tag_id" +
            ") SELECT direct_tag_id, ancestor_tag_id FROM ancestors " +
            "ORDER BY direct_tag_id, ancestor_tag_id;";
        command.Parameters.AddWithValue("$generation", ScryfallSql.FormatGuid(generationId));
        int parameterIndex = 0;
        foreach (Guid tagId in requestedTagIds)
        {
            command.Parameters.AddWithValue($"$tag{parameterIndex++}", ScryfallSql.FormatGuid(tagId));
        }

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            Guid directTagId = ScryfallSql.ParseGuid(reader.GetString(0));
            ancestors[directTagId].Add(ScryfallSql.ParseGuid(reader.GetString(1)));
        }

        Dictionary<Guid, IReadOnlyList<Guid>> result = [];
        foreach (KeyValuePair<Guid, List<Guid>> entry in ancestors)
        {
            entry.Value.Sort();
            result.Add(entry.Key, entry.Value);
        }

        return result;
    }

    /// <summary>
    /// Validates one exact source tag identity.
    /// </summary>
    private static OperationInvalidInput? ValidateTagIdentity(ScryfallTagIdentity? identity)
    {
        if (identity is null || identity.TagType is not ("oracle" or "art"))
        {
            return new OperationInvalidInput("invalid-scryfall-tag-identity", "Scryfall tag type must be oracle or art.");
        }

        bool hasId = identity.TagId is Guid id && id != Guid.Empty;
        bool hasSlug = !string.IsNullOrWhiteSpace(identity.ExactSlug);
        return hasId == hasSlug
            ? new OperationInvalidInput(
                "invalid-scryfall-tag-identity",
                "A Scryfall tag identity requires exactly one non-empty tag ID or exact slug.")
            : null;
    }

    /// <summary>
    /// Returns the common installed-card-data failure without implying a provider lookup occurred.
    /// </summary>
    private static OperationNotCached CardDataMissing()
    {
        return new OperationNotCached("scryfall-corpus-missing", "Scryfall card data is not installed.");
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
        command.Parameters.AddWithValue("$generation", ScryfallSql.FormatGuid(generationId));
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
        command.Parameters.AddWithValue("$generation", ScryfallSql.FormatGuid(generationId));
        command.Parameters.AddWithValue("$type", tagType);
        command.Parameters.AddWithValue("$identity", identity);
        List<StoredTag> matches = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            matches.Add(new StoredTag(ScryfallSql.ParseGuid(reader.GetString(0)), reader.GetString(1)));
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
        command.Parameters.AddWithValue("$generation", ScryfallSql.FormatGuid(generationId));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            Guid parent = ScryfallSql.ParseGuid(reader.GetString(0));
            if (!edges.TryGetValue(parent, out List<Guid>? children))
            {
                children = [];
                edges.Add(parent, children);
            }

            children.Add(ScryfallSql.ParseGuid(reader.GetString(1)));
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
        command.Parameters.AddWithValue("$generation", ScryfallSql.FormatGuid(generationId));
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
    /// Reads one nullable UUID column.
    /// </summary>
    private static Guid? ReadNullableGuid(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : ScryfallSql.ParseGuid(reader.GetString(ordinal));
    }

    /// <summary>
    /// Reads one nullable UTC timestamp column.
    /// </summary>
    private static DateTimeOffset? ReadNullableUtc(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : ScryfallSql.ParseUtc(reader.GetString(ordinal));
    }
}

/// <summary>
/// Carries one lossless card-data object and its generation.
/// </summary>
internal sealed record StoredCardDataObject(
    Guid GenerationId,
    DateTimeOffset RetrievedAtUtc,
    DateTimeOffset ProviderUpdatedAtUtc,
    string RawJson);

/// <summary>
/// Carries one ordered card-data collection with generation details.
/// </summary>
internal sealed record StoredCardDataCollection(
    Guid GenerationId,
    DateTimeOffset RetrievedAtUtc,
    DateTimeOffset ProviderUpdatedAtUtc,
    IReadOnlyList<string> Items);

/// <summary>
/// Carries one exact installed tag identity.
/// </summary>
internal sealed record StoredTag(Guid Id, string RawJson);

/// <summary>
/// Carries one card assignment and its supporting tag path.
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
/// Carries one tag and all matching stored cards.
/// </summary>
internal sealed record StoredCardsByTag(
    Guid GenerationId,
    DateTimeOffset RetrievedAtUtc,
    DateTimeOffset ProviderUpdatedAtUtc,
    string TagJson,
    IReadOnlyList<StoredTagAssignment> Assignments);

/// <summary>
/// Carries the source identities needed to find direct Oracle and artwork tag assignments.
/// </summary>
internal sealed record ScryfallTagTargets(
    Guid? OracleId,
    IReadOnlyList<Guid> IllustrationIds);
