using System.Globalization;
using Microsoft.Data.Sqlite;
using MtgMcp.Core.Decks;

namespace MtgMcp.Decks;

/// <summary>
/// Contains the hand-written SQL primitives used by the single transactional deck store.
/// </summary>
internal static class DeckSql
{
    /// <summary>
    /// Inserts a validated deck graph without merging equivalent entries.
    /// </summary>
    internal static async Task InsertDeckAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DeckDocument deck,
        IReadOnlyDictionary<Guid, string?> baselines,
        CancellationToken cancellationToken)
    {
        await using (SqliteCommand command = CreateCommand(connection, transaction, """
            INSERT INTO decks (
                deck_id, name, description, format, revision, created_at_utc, updated_at_utc)
            VALUES ($deckId, $name, $description, $format, $revision, $createdAtUtc, $updatedAtUtc);
            """))
        {
            command.Parameters.AddWithValue("$deckId", FormatId(deck.DeckId));
            command.Parameters.AddWithValue("$name", deck.Name);
            command.Parameters.AddWithValue("$description", deck.Description);
            command.Parameters.AddWithValue("$format", deck.Format);
            command.Parameters.AddWithValue("$revision", deck.Revision);
            command.Parameters.AddWithValue("$createdAtUtc", DeckDatabase.FormatUtc(deck.CreatedAtUtc));
            command.Parameters.AddWithValue("$updatedAtUtc", DeckDatabase.FormatUtc(deck.UpdatedAtUtc));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (DeckEntry entry in deck.Entries)
        {
            await InsertEntryAsync(
                connection,
                transaction,
                deck.DeckId,
                entry,
                cancellationToken).ConfigureAwait(false);
        }

        foreach (DeckCategory category in deck.Categories)
        {
            await InsertCategoryAsync(
                connection,
                transaction,
                deck.DeckId,
                category,
                cancellationToken).ConfigureAwait(false);
        }

        foreach (DeckCategoryAssignment assignment in deck.CategoryAssignments)
        {
            await AssignCategoryAsync(
                connection,
                transaction,
                deck.DeckId,
                assignment,
                cancellationToken).ConfigureAwait(false);
        }

        foreach (DeckProviderBinding binding in deck.ProviderBindings)
        {
            baselines.TryGetValue(binding.BindingId, out string? baseline);
            await UpsertProviderBindingAsync(
                connection,
                transaction,
                deck.DeckId,
                binding,
                baseline,
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Lists canonically ordered deck summaries for one stable offset page.
    /// </summary>
    internal static async Task<IReadOnlyList<DeckSummary>> ListDecksAsync(
        SqliteConnection connection,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        List<DeckSummary> items = [];
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT deck_id, name, description, format, revision, created_at_utc, updated_at_utc
            FROM decks
            ORDER BY name COLLATE NOCASE, deck_id
            LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new DeckSummary(
                ParseId(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt64(4),
                ParseUtc(reader.GetString(5)),
                ParseUtc(reader.GetString(6))));
        }

        return items;
    }

    /// <summary>
    /// Loads one complete deck in canonical entry, category, assignment, and binding order.
    /// </summary>
    internal static async Task<DeckDocument?> ReadDeckAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid deckId,
        CancellationToken cancellationToken)
    {
        DeckSummary? summary;
        await using (SqliteCommand command = CreateCommand(connection, transaction, """
            SELECT deck_id, name, description, format, revision, created_at_utc, updated_at_utc
            FROM decks
            WHERE deck_id = $deckId;
            """))
        {
            command.Parameters.AddWithValue("$deckId", FormatId(deckId));
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            summary = new DeckSummary(
                ParseId(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt64(4),
                ParseUtc(reader.GetString(5)),
                ParseUtc(reader.GetString(6)));
        }

        IReadOnlyList<DeckEntry> entries = await ReadEntriesAsync(
            connection,
            transaction,
            deckId,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<DeckCategory> categories = await ReadCategoriesAsync(
            connection,
            transaction,
            deckId,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<DeckCategoryAssignment> assignments = await ReadAssignmentsAsync(
            connection,
            transaction,
            deckId,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<DeckProviderBinding> bindings = await ReadBindingsAsync(
            connection,
            transaction,
            deckId,
            cancellationToken).ConfigureAwait(false);
        return new DeckDocument(
            summary.DeckId,
            summary.Name,
            summary.Description,
            summary.Format,
            summary.Revision,
            summary.CreatedAtUtc,
            summary.UpdatedAtUtc,
            entries,
            categories,
            assignments,
            bindings);
    }

    /// <summary>
    /// Loads canonical provider baselines for one deck in stable binding order.
    /// </summary>
    internal static async Task<IReadOnlyList<DeckSyncBaseline>> ReadBaselinesAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid deckId,
        CancellationToken cancellationToken)
    {
        List<DeckSyncBaseline> items = [];
        await using SqliteCommand command = CreateCommand(connection, transaction, """
            SELECT s.binding_id, s.canonical_snapshot
            FROM sync_baselines s
            JOIN provider_bindings b ON b.binding_id = s.binding_id
            WHERE b.deck_id = $deckId
            ORDER BY s.binding_id;
            """);
        command.Parameters.AddWithValue("$deckId", FormatId(deckId));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new DeckSyncBaseline(ParseId(reader.GetString(0)), reader.GetString(1)));
        }

        return items;
    }

    /// <summary>
    /// Reads a deck revision or returns null when the deck does not exist.
    /// </summary>
    internal static async Task<long?> ReadRevisionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid deckId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = CreateCommand(
            connection,
            transaction,
            "SELECT revision FROM decks WHERE deck_id = $deckId;");
        command.Parameters.AddWithValue("$deckId", FormatId(deckId));
        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Increments a deck revision exactly once after all requested changes succeed.
    /// </summary>
    internal static async Task UpdateRevisionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid deckId,
        long revision,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = CreateCommand(connection, transaction, """
            UPDATE decks
            SET revision = $revision, updated_at_utc = $updatedAtUtc
            WHERE deck_id = $deckId;
            """);
        command.Parameters.AddWithValue("$revision", revision);
        command.Parameters.AddWithValue("$updatedAtUtc", DeckDatabase.FormatUtc(updatedAtUtc));
        command.Parameters.AddWithValue("$deckId", FormatId(deckId));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes one revision-checked deck and every dependent row by cascade.
    /// </summary>
    internal static async Task DeleteDeckAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid deckId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = CreateCommand(
            connection,
            transaction,
            "DELETE FROM decks WHERE deck_id = $deckId;");
        command.Parameters.AddWithValue("$deckId", FormatId(deckId));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Replaces editable deck metadata.
    /// </summary>
    internal static async Task UpdateMetadataAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid deckId,
        UpdateDeckMetadataChange change,
        CancellationToken cancellationToken)
    {
        string name = DeckContractValidator.Required(change.Name, "Deck name");
        string description = DeckContractValidator.Optional(change.Description) ?? string.Empty;
        string format = DeckContractValidator.Required(change.Format, "Format").ToLowerInvariant();
        await using SqliteCommand command = CreateCommand(connection, transaction, """
            UPDATE decks
            SET name = $name, description = $description, format = $format
            WHERE deck_id = $deckId;
            """);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$description", description);
        command.Parameters.AddWithValue("$format", format);
        command.Parameters.AddWithValue("$deckId", FormatId(deckId));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds one validated entry to a deck.
    /// </summary>
    internal static async Task InsertEntryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid deckId,
        DeckEntry entry,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = CreateCommand(connection, transaction, """
            INSERT INTO deck_entries (
                entry_id, deck_id, quantity, card_name, oracle_id, printing_id,
                set_code, collector_number, language, finish, zone, sort_order)
            VALUES (
                $entryId, $deckId, $quantity, $cardName, $oracleId, $printingId,
                $setCode, $collectorNumber, $language, $finish, $zone, $sortOrder);
            """);
        AddEntryParameters(command, deckId, entry);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Replaces one entry only when it belongs to the selected deck.
    /// </summary>
    internal static async Task UpdateEntryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid deckId,
        DeckEntry entry,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = CreateCommand(connection, transaction, """
            UPDATE deck_entries
            SET quantity = $quantity,
                card_name = $cardName,
                oracle_id = $oracleId,
                printing_id = $printingId,
                set_code = $setCode,
                collector_number = $collectorNumber,
                language = $language,
                finish = $finish,
                zone = $zone,
                sort_order = $sortOrder
            WHERE deck_id = $deckId AND entry_id = $entryId;
            """);
        AddEntryParameters(command, deckId, entry);
        await RequireOwnedRowAsync(
            command,
            "deck-entry-not-found",
            "The deck entry was not found.",
            cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Removes one entry only when it belongs to the selected deck.
    /// </summary>
    internal static Task RemoveEntryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid deckId,
        Guid entryId,
        CancellationToken cancellationToken)
    {
        return DeleteOwnedRowAsync(
            connection,
            transaction,
            "DELETE FROM deck_entries WHERE deck_id = $deckId AND entry_id = $ownedId;",
            deckId,
            entryId,
            "deck-entry-not-found",
            "The deck entry was not found.",
            cancellationToken);
    }

    /// <summary>
    /// Adds one validated functional category to a deck.
    /// </summary>
    internal static async Task InsertCategoryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid deckId,
        DeckCategory category,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = CreateCommand(connection, transaction, """
            INSERT INTO deck_categories (category_id, deck_id, name, color, sort_order)
            VALUES ($categoryId, $deckId, $name, $color, $sortOrder);
            """);
        AddCategoryParameters(command, deckId, category);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Replaces one category only when it belongs to the selected deck.
    /// </summary>
    internal static async Task UpdateCategoryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid deckId,
        DeckCategory category,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = CreateCommand(connection, transaction, """
            UPDATE deck_categories
            SET name = $name, color = $color, sort_order = $sortOrder
            WHERE deck_id = $deckId AND category_id = $categoryId;
            """);
        AddCategoryParameters(command, deckId, category);
        await RequireOwnedRowAsync(
            command,
            "deck-category-not-found",
            "The deck category was not found.",
            cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Removes one category without changing entries or zones.
    /// </summary>
    internal static Task RemoveCategoryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid deckId,
        Guid categoryId,
        CancellationToken cancellationToken)
    {
        return DeleteOwnedRowAsync(
            connection,
            transaction,
            "DELETE FROM deck_categories WHERE deck_id = $deckId AND category_id = $ownedId;",
            deckId,
            categoryId,
            "deck-category-not-found",
            "The deck category was not found.",
            cancellationToken);
    }

    /// <summary>
    /// Creates or updates one assignment after proving both rows belong to the deck.
    /// </summary>
    internal static async Task AssignCategoryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid deckId,
        DeckCategoryAssignment assignment,
        CancellationToken cancellationToken)
    {
        await EnsureEntryAndCategoryOwnershipAsync(
            connection,
            transaction,
            deckId,
            assignment.EntryId,
            assignment.CategoryId,
            cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = CreateCommand(connection, transaction, """
            INSERT INTO deck_entry_categories (entry_id, category_id, is_primary)
            VALUES ($entryId, $categoryId, $isPrimary)
            ON CONFLICT(entry_id, category_id)
            DO UPDATE SET is_primary = excluded.is_primary;
            """);
        command.Parameters.AddWithValue("$entryId", FormatId(assignment.EntryId));
        command.Parameters.AddWithValue("$categoryId", FormatId(assignment.CategoryId));
        command.Parameters.AddWithValue("$isPrimary", assignment.IsPrimary ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes one assignment after proving both rows belong to the deck.
    /// </summary>
    internal static async Task UnassignCategoryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid deckId,
        Guid entryId,
        Guid categoryId,
        CancellationToken cancellationToken)
    {
        await EnsureEntryAndCategoryOwnershipAsync(
            connection,
            transaction,
            deckId,
            entryId,
            categoryId,
            cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = CreateCommand(connection, transaction, """
            DELETE FROM deck_entry_categories
            WHERE entry_id = $entryId AND category_id = $categoryId;
            """);
        command.Parameters.AddWithValue("$entryId", FormatId(entryId));
        command.Parameters.AddWithValue("$categoryId", FormatId(categoryId));
        await RequireOwnedRowAsync(
            command,
            "deck-category-assignment-not-found",
            "The category assignment was not found.",
            cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Creates or replaces one provider-neutral binding and its canonical baseline.
    /// </summary>
    internal static async Task UpsertProviderBindingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid deckId,
        DeckProviderBinding binding,
        string? canonicalBaseline,
        CancellationToken cancellationToken)
    {
        await using (SqliteCommand command = CreateCommand(connection, transaction, """
            INSERT INTO provider_bindings (
                binding_id, deck_id, provider, remote_id, remote_uri, remote_version,
                baseline_fingerprint, last_pulled_at_utc, last_pushed_at_utc)
            VALUES (
                $bindingId, $deckId, $provider, $remoteId, $remoteUri, $remoteVersion,
                $baselineFingerprint, $lastPulledAtUtc, $lastPushedAtUtc)
            ON CONFLICT(binding_id) DO UPDATE SET
                provider = excluded.provider,
                remote_id = excluded.remote_id,
                remote_uri = excluded.remote_uri,
                remote_version = excluded.remote_version,
                baseline_fingerprint = excluded.baseline_fingerprint,
                last_pulled_at_utc = excluded.last_pulled_at_utc,
                last_pushed_at_utc = excluded.last_pushed_at_utc
            WHERE provider_bindings.deck_id = excluded.deck_id;
            """))
        {
            command.Parameters.AddWithValue("$bindingId", FormatId(binding.BindingId));
            command.Parameters.AddWithValue("$deckId", FormatId(deckId));
            command.Parameters.AddWithValue("$provider", binding.Provider);
            command.Parameters.AddWithValue("$remoteId", binding.RemoteId);
            AddNullable(command, "$remoteUri", binding.RemoteUri);
            AddNullable(command, "$remoteVersion", binding.RemoteVersion);
            AddNullable(command, "$baselineFingerprint", binding.BaselineFingerprint);
            AddNullable(
                command,
                "$lastPulledAtUtc",
                binding.LastPulledAtUtc is null
                    ? null
                    : DeckDatabase.FormatUtc(binding.LastPulledAtUtc.Value));
            AddNullable(
                command,
                "$lastPushedAtUtc",
                binding.LastPushedAtUtc is null
                    ? null
                    : DeckDatabase.FormatUtc(binding.LastPushedAtUtc.Value));
            int affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (affected != 1)
            {
                throw new DeckInputException("The provider binding belongs to another deck.");
            }
        }

        if (canonicalBaseline is null)
        {
            await using SqliteCommand delete = CreateCommand(
                connection,
                transaction,
                "DELETE FROM sync_baselines WHERE binding_id = $bindingId;");
            delete.Parameters.AddWithValue("$bindingId", FormatId(binding.BindingId));
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await using SqliteCommand baselineCommand = CreateCommand(connection, transaction, """
            INSERT INTO sync_baselines (binding_id, canonical_snapshot)
            VALUES ($bindingId, $canonicalSnapshot)
            ON CONFLICT(binding_id)
            DO UPDATE SET canonical_snapshot = excluded.canonical_snapshot;
            """);
        baselineCommand.Parameters.AddWithValue("$bindingId", FormatId(binding.BindingId));
        baselineCommand.Parameters.AddWithValue("$canonicalSnapshot", canonicalBaseline);
        await baselineCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes one provider binding only when it belongs to the selected deck.
    /// </summary>
    internal static Task RemoveProviderBindingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid deckId,
        Guid bindingId,
        CancellationToken cancellationToken)
    {
        return DeleteOwnedRowAsync(
            connection,
            transaction,
            "DELETE FROM provider_bindings WHERE deck_id = $deckId AND binding_id = $ownedId;",
            deckId,
            bindingId,
            "deck-provider-binding-not-found",
            "The provider binding was not found.",
            cancellationToken);
    }

    /// <summary>
    /// Counts stored decks for backup manifests.
    /// </summary>
    internal static async Task<int> CountDecksAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM decks;";
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Loads canonically ordered entry rows.
    /// </summary>
    private static async Task<IReadOnlyList<DeckEntry>> ReadEntriesAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid deckId,
        CancellationToken cancellationToken)
    {
        List<DeckEntry> items = [];
        await using SqliteCommand command = CreateCommand(connection, transaction, """
            SELECT entry_id, quantity, card_name, oracle_id, printing_id, set_code,
                   collector_number, language, finish, zone, sort_order
            FROM deck_entries
            WHERE deck_id = $deckId
            ORDER BY zone COLLATE NOCASE, sort_order, card_name COLLATE NOCASE,
                     COALESCE(set_code, ''), COALESCE(collector_number, ''), finish, entry_id;
            """);
        command.Parameters.AddWithValue("$deckId", FormatId(deckId));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new DeckEntry(
                ParseId(reader.GetString(0)),
                reader.GetInt32(1),
                reader.GetString(2),
                ReadOptionalId(reader, 3),
                ReadOptionalId(reader, 4),
                ReadOptionalString(reader, 5),
                ReadOptionalString(reader, 6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetInt32(10)));
        }

        return items;
    }

    /// <summary>
    /// Loads canonically ordered category rows.
    /// </summary>
    private static async Task<IReadOnlyList<DeckCategory>> ReadCategoriesAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid deckId,
        CancellationToken cancellationToken)
    {
        List<DeckCategory> items = [];
        await using SqliteCommand command = CreateCommand(connection, transaction, """
            SELECT category_id, name, color, sort_order
            FROM deck_categories
            WHERE deck_id = $deckId
            ORDER BY sort_order, name COLLATE NOCASE, category_id;
            """);
        command.Parameters.AddWithValue("$deckId", FormatId(deckId));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new DeckCategory(
                ParseId(reader.GetString(0)),
                reader.GetString(1),
                ReadOptionalString(reader, 2),
                reader.GetInt32(3)));
        }

        return items;
    }

    /// <summary>
    /// Loads assignments with primary categories first and category order preserved.
    /// </summary>
    private static async Task<IReadOnlyList<DeckCategoryAssignment>> ReadAssignmentsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid deckId,
        CancellationToken cancellationToken)
    {
        List<DeckCategoryAssignment> items = [];
        await using SqliteCommand command = CreateCommand(connection, transaction, """
            SELECT ec.entry_id, ec.category_id, ec.is_primary
            FROM deck_entry_categories ec
            JOIN deck_entries e ON e.entry_id = ec.entry_id
            JOIN deck_categories c ON c.category_id = ec.category_id
            WHERE e.deck_id = $deckId AND c.deck_id = $deckId
            ORDER BY ec.entry_id, ec.is_primary DESC, c.sort_order, c.name COLLATE NOCASE, c.category_id;
            """);
        command.Parameters.AddWithValue("$deckId", FormatId(deckId));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new DeckCategoryAssignment(
                ParseId(reader.GetString(0)),
                ParseId(reader.GetString(1)),
                reader.GetBoolean(2)));
        }

        return items;
    }

    /// <summary>
    /// Loads provider-neutral bindings in canonical provider and remote-ID order.
    /// </summary>
    private static async Task<IReadOnlyList<DeckProviderBinding>> ReadBindingsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid deckId,
        CancellationToken cancellationToken)
    {
        List<DeckProviderBinding> items = [];
        await using SqliteCommand command = CreateCommand(connection, transaction, """
            SELECT binding_id, provider, remote_id, remote_uri, remote_version,
                   baseline_fingerprint, last_pulled_at_utc, last_pushed_at_utc
            FROM provider_bindings
            WHERE deck_id = $deckId
            ORDER BY provider COLLATE NOCASE, remote_id COLLATE NOCASE, binding_id;
            """);
        command.Parameters.AddWithValue("$deckId", FormatId(deckId));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new DeckProviderBinding(
                ParseId(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                ReadOptionalString(reader, 3),
                ReadOptionalString(reader, 4),
                ReadOptionalString(reader, 5),
                ReadOptionalUtc(reader, 6),
                ReadOptionalUtc(reader, 7)));
        }

        return items;
    }

    /// <summary>
    /// Adds the common entry parameters used by insert and update statements.
    /// </summary>
    private static void AddEntryParameters(
        SqliteCommand command,
        Guid deckId,
        DeckEntry entry)
    {
        command.Parameters.AddWithValue("$entryId", FormatId(entry.EntryId));
        command.Parameters.AddWithValue("$deckId", FormatId(deckId));
        command.Parameters.AddWithValue("$quantity", entry.Quantity);
        command.Parameters.AddWithValue("$cardName", entry.CardName);
        AddNullable(command, "$oracleId", entry.OracleId?.ToString("D"));
        AddNullable(command, "$printingId", entry.PrintingId?.ToString("D"));
        AddNullable(command, "$setCode", entry.SetCode);
        AddNullable(command, "$collectorNumber", entry.CollectorNumber);
        command.Parameters.AddWithValue("$language", entry.Language);
        command.Parameters.AddWithValue("$finish", entry.Finish);
        command.Parameters.AddWithValue("$zone", entry.Zone);
        command.Parameters.AddWithValue("$sortOrder", entry.SortOrder);
    }

    /// <summary>
    /// Adds the common category parameters used by insert and update statements.
    /// </summary>
    private static void AddCategoryParameters(
        SqliteCommand command,
        Guid deckId,
        DeckCategory category)
    {
        command.Parameters.AddWithValue("$categoryId", FormatId(category.CategoryId));
        command.Parameters.AddWithValue("$deckId", FormatId(deckId));
        command.Parameters.AddWithValue("$name", category.Name);
        AddNullable(command, "$color", category.Color);
        command.Parameters.AddWithValue("$sortOrder", category.SortOrder);
    }

    /// <summary>
    /// Deletes one row constrained by deck ownership and reports missing references.
    /// </summary>
    private static async Task DeleteOwnedRowAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        Guid deckId,
        Guid ownedId,
        string missingReasonCode,
        string missingMessage,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = CreateCommand(connection, transaction, sql);
        command.Parameters.AddWithValue("$deckId", FormatId(deckId));
        command.Parameters.AddWithValue("$ownedId", FormatId(ownedId));
        await RequireOwnedRowAsync(
            command,
            missingReasonCode,
            missingMessage,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Proves an assignment cannot link rows from different decks.
    /// </summary>
    private static async Task EnsureEntryAndCategoryOwnershipAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid deckId,
        Guid entryId,
        Guid categoryId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = CreateCommand(connection, transaction, """
            SELECT COUNT(*)
            FROM deck_entries e, deck_categories c
            WHERE e.entry_id = $entryId
              AND e.deck_id = $deckId
              AND c.category_id = $categoryId
              AND c.deck_id = $deckId;
            """);
        command.Parameters.AddWithValue("$entryId", FormatId(entryId));
        command.Parameters.AddWithValue("$categoryId", FormatId(categoryId));
        command.Parameters.AddWithValue("$deckId", FormatId(deckId));
        long count = (long)(await command.ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false) ?? 0L);
        if (count != 1)
        {
            throw new DeckEntityNotFoundException(
                "deck-entry-or-category-not-found",
                "The entry or category reference was not found in this deck.");
        }
    }

    /// <summary>
    /// Requires exactly one affected row for an ID-addressed mutation.
    /// </summary>
    private static async Task RequireOwnedRowAsync(
        SqliteCommand command,
        string missingReasonCode,
        string missingMessage,
        CancellationToken cancellationToken)
    {
        int affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            throw new DeckEntityNotFoundException(missingReasonCode, missingMessage);
        }
    }

    /// <summary>
    /// Creates a command and attaches an optional transaction.
    /// </summary>
    private static SqliteCommand CreateCommand(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql)
    {
        SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    /// <summary>
    /// Adds a nullable text parameter using the provider database-null sentinel.
    /// </summary>
    private static void AddNullable(SqliteCommand command, string name, string? value)
    {
        command.Parameters.AddWithValue(name, value is null ? DBNull.Value : value);
    }

    /// <summary>
    /// Formats a stable identifier for SQLite text storage.
    /// </summary>
    private static string FormatId(Guid value)
    {
        return value.ToString("D");
    }

    /// <summary>
    /// Parses a validated identifier from durable storage.
    /// </summary>
    private static Guid ParseId(string value)
    {
        return Guid.ParseExact(value, "D");
    }

    /// <summary>
    /// Parses a canonical persisted timestamp.
    /// </summary>
    private static DateTimeOffset ParseUtc(string value)
    {
        return DateTimeOffset.ParseExact(
            value,
            "O",
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind).ToUniversalTime();
    }

    /// <summary>
    /// Reads nullable text without converting database null into empty data.
    /// </summary>
    private static string? ReadOptionalString(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    /// <summary>
    /// Reads a nullable stable identifier.
    /// </summary>
    private static Guid? ReadOptionalId(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : ParseId(reader.GetString(ordinal));
    }

    /// <summary>
    /// Reads a nullable canonical UTC timestamp.
    /// </summary>
    private static DateTimeOffset? ReadOptionalUtc(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : ParseUtc(reader.GetString(ordinal));
    }
}
