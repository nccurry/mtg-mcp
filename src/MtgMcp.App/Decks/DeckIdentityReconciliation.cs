using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using MtgMcp.App.Configuration;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Decks;

/// <summary>
/// Captures only the provider-neutral identity fields reconciliation may change.
/// </summary>
internal sealed record DeckEntryIdentity(
    [property: JsonPropertyName("cardName")] string CardName,
    [property: JsonPropertyName("oracleId")] Guid? OracleId,
    [property: JsonPropertyName("printingId")] Guid? PrintingId,
    [property: JsonPropertyName("setCode")] string? SetCode,
    [property: JsonPropertyName("collectorNumber")] string? CollectorNumber,
    [property: JsonPropertyName("language")] string Language);

/// <summary>
/// Reports one deck entry's exact identity outcome in original deck order.
/// </summary>
internal sealed record DeckIdentityReconciliationRow(
    [property: JsonPropertyName("entryId")] Guid EntryId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("matchedBy")] string? MatchedBy,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("before")] DeckEntryIdentity Before,
    [property: JsonPropertyName("after")] DeckEntryIdentity? After,
    [property: JsonPropertyName("evidenceOrigin")] string? EvidenceOrigin,
    [property: JsonPropertyName("corpusGenerationId")] Guid? CorpusGenerationId,
    [property: JsonPropertyName("snapshot")] ScryfallSnapshotReference? Snapshot);

/// <summary>
/// Returns a deterministic identity-only proposal and the retained evidence required to apply it.
/// </summary>
internal sealed record DeckIdentityReconciliationPreview(
    [property: JsonPropertyName("deckId")] Guid DeckId,
    [property: JsonPropertyName("deckRevision")] long DeckRevision,
    [property: JsonPropertyName("rows")] IReadOnlyList<DeckIdentityReconciliationRow> Rows,
    [property: JsonPropertyName("isComplete")] bool IsComplete,
    [property: JsonPropertyName("proposedChangeCount")] int ProposedChangeCount,
    [property: JsonPropertyName("evidence")] ScryfallCollectionEvidenceBinding? Evidence,
    [property: JsonPropertyName("previewFingerprint")] string PreviewFingerprint,
    [property: JsonPropertyName("applyToken")] string ApplyToken);

/// <summary>
/// Carries the checksummed replay state encoded into one opaque apply token.
/// </summary>
internal sealed record DeckIdentityApplyTokenPayload(
    int Version,
    Guid DeckId,
    long DeckRevision,
    IReadOnlyList<Guid> EntryIds,
    ScryfallCollectionEvidenceBinding? Evidence,
    string PreviewFingerprint);

/// <summary>
/// Associates one selected entry with its strongest exact lookup and deduplicated evidence row.
/// </summary>
internal sealed record DeckIdentityLookupPlan(
    DeckEntry Entry,
    string? MatchMethod,
    ScryfallEvidenceLookup? Lookup,
    int? EvidenceIndex);

/// <summary>
/// Encodes and validates opaque identity-apply state without treating client data as mutation authority.
/// </summary>
internal sealed class DeckIdentityApplyToken
{
    /// <summary>
    /// Uses deterministic web JSON for checksum generation and token decoding.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Authenticates tokens only within the server process that produced their preview.
    /// </summary>
    private readonly byte[] key = RandomNumberGenerator.GetBytes(32);

    /// <summary>
    /// Encodes one payload together with its tamper-evident checksum.
    /// </summary>
    internal string Encode(DeckIdentityApplyTokenPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        string payloadJson = JsonSerializer.Serialize(payload, SerializerOptions);
        string checksum = Authenticate(payloadJson);
        string envelope = JsonSerializer.Serialize(new { payload, checksum }, SerializerOptions);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(envelope))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Decodes one token only when its complete payload checksum remains unchanged.
    /// </summary>
    internal bool TryDecode(string? token, out DeckIdentityApplyTokenPayload? payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            string padded = token.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            using JsonDocument document = JsonDocument.Parse(
                Encoding.UTF8.GetString(Convert.FromBase64String(padded)));
            JsonElement root = document.RootElement;
            string? checksum = root.GetProperty("checksum").GetString();
            DeckIdentityApplyTokenPayload? decoded = root.GetProperty("payload")
                .Deserialize<DeckIdentityApplyTokenPayload>(SerializerOptions);
            if (decoded is null || decoded.Version != 1)
            {
                return false;
            }

            string payloadJson = JsonSerializer.Serialize(decoded, SerializerOptions);
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(Authenticate(payloadJson)),
                    Encoding.UTF8.GetBytes(checksum ?? string.Empty)))
            {
                return false;
            }

            payload = decoded;
            return true;
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Produces a lowercase SHA-256 checksum for one canonical token component.
    /// </summary>
    internal static string Hash(string value)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    /// <summary>
    /// Produces a process-local authentication code for one canonical payload.
    /// </summary>
    private string Authenticate(string value)
    {
        return Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value)));
    }
}

/// <summary>
/// Coordinates exact Scryfall evidence with revisioned local deck identity updates.
/// </summary>
internal sealed class DeckIdentityReconciliationCoordinator
{
    /// <summary>
    /// Uses deterministic web JSON for proposal fingerprints.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Authenticates apply tokens for this server-owned reconciliation lifetime.
    /// </summary>
    private readonly DeckIdentityApplyToken tokens = new();

    /// <summary>
    /// Stores the only boundary permitted to write deck state.
    /// </summary>
    private readonly SqliteDeckStore deckStore;

    /// <summary>
    /// Stores the shared Scryfall evidence and replay boundary.
    /// </summary>
    private readonly ScryfallService scryfall;

    /// <summary>
    /// Creates identity composition around existing deck and Scryfall owners.
    /// </summary>
    internal DeckIdentityReconciliationCoordinator(
        SqliteDeckStore deckStore,
        ScryfallService scryfall)
    {
        this.deckStore = deckStore;
        this.scryfall = scryfall;
    }

    /// <summary>
    /// Builds one exact, evidence-bound identity proposal without changing the deck.
    /// </summary>
    internal async Task<OperationResult<DeckIdentityReconciliationPreview>> PreviewAsync(
        Guid deckId,
        long expectedRevision,
        IReadOnlyList<Guid>? entryIds,
        string freshnessPolicy,
        CancellationToken cancellationToken)
    {
        OperationResult<DeckDocument> deckResult = await deckStore.GetAsync(deckId, cancellationToken)
            .ConfigureAwait(false);
        if (deckResult is not OperationSuccess<DeckDocument> deckSuccess)
        {
            return ForwardFailure<DeckDocument, DeckIdentityReconciliationPreview>(deckResult);
        }

        DeckDocument deck = deckSuccess.Data;
        if (expectedRevision <= 0 || deck.Revision != expectedRevision)
        {
            return new OperationConflict(
                "deck-revision-conflict",
                "The local deck revision changed before identity reconciliation.");
        }

        OperationResult<IReadOnlyList<DeckEntry>> selection = SelectEntries(deck, entryIds);
        if (selection is not OperationSuccess<IReadOnlyList<DeckEntry>> selected)
        {
            return ForwardFailure<IReadOnlyList<DeckEntry>, DeckIdentityReconciliationPreview>(selection);
        }

        IReadOnlyList<DeckEntry> entries = selected.Data;
        (IReadOnlyList<DeckIdentityLookupPlan> plans, IReadOnlyList<ScryfallEvidenceLookup> lookups) =
            BuildLookupPlans(entries);
        ScryfallExactCollectionEvidence? evidence = null;
        if (lookups.Count > 0)
        {
            OperationResult<ScryfallExactCollectionEvidence> evidenceResult =
                await scryfall.ResolveExactCollectionAsync(lookups, freshnessPolicy, cancellationToken)
                    .ConfigureAwait(false);
            if (evidenceResult is not OperationSuccess<ScryfallExactCollectionEvidence> evidenceSuccess)
            {
                return ForwardFailure<ScryfallExactCollectionEvidence, DeckIdentityReconciliationPreview>(evidenceResult);
            }

            evidence = evidenceSuccess.Data;
        }

        return new OperationSuccess<DeckIdentityReconciliationPreview>(
            BuildPreview(deck, entries, plans, evidence));
    }

    /// <summary>
    /// Replays preview evidence and applies only verified identity field changes in one deck revision.
    /// </summary>
    internal async Task<OperationResult<DeckDocument>> ApplyAsync(
        Guid deckId,
        long expectedRevision,
        string previewFingerprint,
        string applyToken,
        bool allowPartial,
        CancellationToken cancellationToken)
    {
        if (!tokens.TryDecode(applyToken, out DeckIdentityApplyTokenPayload? token) ||
            token is null ||
            token.DeckId != deckId ||
            token.DeckRevision != expectedRevision ||
            !string.Equals(token.PreviewFingerprint, previewFingerprint, StringComparison.Ordinal))
        {
            return new OperationInvalidInput(
                "invalid-identity-apply-token",
                "The identity reconciliation apply token does not match this request.");
        }

        OperationResult<DeckDocument> deckResult = await deckStore.GetAsync(deckId, cancellationToken)
            .ConfigureAwait(false);
        if (deckResult is not OperationSuccess<DeckDocument> deckSuccess)
        {
            return deckResult;
        }

        DeckDocument deck = deckSuccess.Data;
        if (deck.Revision != expectedRevision)
        {
            return new OperationConflict(
                "deck-revision-conflict",
                "The local deck revision changed after identity preview.");
        }

        OperationResult<IReadOnlyList<DeckEntry>> selection = SelectEntries(deck, token.EntryIds);
        if (selection is not OperationSuccess<IReadOnlyList<DeckEntry>> selected)
        {
            return ForwardFailure<IReadOnlyList<DeckEntry>, DeckDocument>(selection);
        }

        IReadOnlyList<DeckEntry> entries = selected.Data;
        (IReadOnlyList<DeckIdentityLookupPlan> plans, IReadOnlyList<ScryfallEvidenceLookup> lookups) =
            BuildLookupPlans(entries);
        ScryfallExactCollectionEvidence? evidence = null;
        if (lookups.Count > 0)
        {
            OperationResult<ScryfallExactCollectionEvidence> replay =
                await scryfall.ReplayExactCollectionAsync(lookups, token.Evidence, cancellationToken)
                    .ConfigureAwait(false);
            if (replay is not OperationSuccess<ScryfallExactCollectionEvidence> replaySuccess)
            {
                return ForwardFailure<ScryfallExactCollectionEvidence, DeckDocument>(replay);
            }

            evidence = replaySuccess.Data;
        }
        else if (token.Evidence is not null)
        {
            return new OperationInvalidInput(
                "invalid-identity-apply-token",
                "The identity reconciliation evidence does not match the selected entries.");
        }

        DeckIdentityReconciliationPreview preview = BuildPreview(deck, entries, plans, evidence);
        if (!string.Equals(preview.PreviewFingerprint, previewFingerprint, StringComparison.Ordinal))
        {
            return new OperationInvalidInput(
                "identity-preview-mismatch",
                "The retained evidence no longer reproduces the accepted identity preview.");
        }

        if (!preview.IsComplete && !allowPartial)
        {
            return new OperationInvalidInput(
                "partial-identity-reconciliation-not-allowed",
                "The identity preview is incomplete; set allowPartial to apply only resolved rows.");
        }

        Dictionary<Guid, DeckEntry> entriesById = deck.Entries.ToDictionary(value => value.EntryId);
        List<DeckChange> changes = [];
        foreach (DeckIdentityReconciliationRow row in preview.Rows)
        {
            if (row.Status != "resolved" || row.After is null)
            {
                continue;
            }

            DeckEntry entry = entriesById[row.EntryId];
            changes.Add(new UpdateDeckEntryChange(entry with
            {
                CardName = row.After.CardName,
                OracleId = row.After.OracleId,
                PrintingId = row.After.PrintingId,
                SetCode = row.After.SetCode,
                CollectorNumber = row.After.CollectorNumber,
                Language = row.After.Language,
            }));
        }

        return changes.Count == 0
            ? new OperationSuccess<DeckDocument>(deck)
            : await deckStore.ApplyChangesAsync(
                deckId,
                expectedRevision,
                changes,
                cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Selects at most 150 unique entries in canonical deck order.
    /// </summary>
    private static OperationResult<IReadOnlyList<DeckEntry>> SelectEntries(
        DeckDocument deck,
        IReadOnlyList<Guid>? entryIds)
    {
        if (entryIds is null)
        {
            return deck.Entries.Count is < 1 or > 150
                ? InvalidSelection()
                : new OperationSuccess<IReadOnlyList<DeckEntry>>(deck.Entries);
        }

        if (entryIds.Count is < 1 or > 150 ||
            entryIds.Any(value => value == Guid.Empty) ||
            entryIds.Distinct().Count() != entryIds.Count)
        {
            return InvalidSelection();
        }

        HashSet<Guid> requested = [.. entryIds];
        DeckEntry[] selected = deck.Entries.Where(value => requested.Contains(value.EntryId)).ToArray();
        return selected.Length != requested.Count
            ? InvalidSelection()
            : new OperationSuccess<IReadOnlyList<DeckEntry>>(selected);
    }

    /// <summary>
    /// Creates the bounded selection failure shared by every invalid entry-ID set.
    /// </summary>
    private static OperationInvalidInput InvalidSelection()
    {
        return new OperationInvalidInput(
            "invalid-identity-entry-selection",
            "Identity reconciliation requires 1 through 150 unique existing entry IDs.");
    }

    /// <summary>
    /// Builds strongest-first lookups while deduplicating identical evidence requests.
    /// </summary>
    private static (
        IReadOnlyList<DeckIdentityLookupPlan> Plans,
        IReadOnlyList<ScryfallEvidenceLookup> Lookups) BuildLookupPlans(IReadOnlyList<DeckEntry> entries)
    {
        Dictionary<string, int> indexes = new(StringComparer.Ordinal);
        List<ScryfallEvidenceLookup> lookups = [];
        List<DeckIdentityLookupPlan> plans = [];
        foreach (DeckEntry entry in entries)
        {
            (string? method, ScryfallEvidenceLookup? lookup) = BuildLookup(entry);
            int? evidenceIndex = null;
            if (lookup is not null)
            {
                string key = LookupKey(lookup);
                if (!indexes.TryGetValue(key, out int existingIndex))
                {
                    existingIndex = lookups.Count;
                    indexes.Add(key, existingIndex);
                    lookups.Add(lookup);
                }

                evidenceIndex = existingIndex;
            }

            plans.Add(new DeckIdentityLookupPlan(entry, method, lookup, evidenceIndex));
        }

        return (plans, lookups);
    }

    /// <summary>
    /// Selects the first complete exact identity case without fuzzy fallback.
    /// </summary>
    private static (string? Method, ScryfallEvidenceLookup? Lookup) BuildLookup(DeckEntry entry)
    {
        if (entry.PrintingId is Guid printingId)
        {
            return ("scryfall-printing-id", new ScryfallEvidenceLookup(
                new ScryfallCardLookup("scryfall-id", printingId.ToString("D"))));
        }

        if (!string.IsNullOrWhiteSpace(entry.SetCode) &&
            !string.IsNullOrWhiteSpace(entry.CollectorNumber) &&
            !string.IsNullOrWhiteSpace(entry.Language))
        {
            return ("set-collector-language", new ScryfallEvidenceLookup(
                new ScryfallCardLookup(
                    "printing",
                    SetCode: entry.SetCode.Trim().ToLowerInvariant(),
                    CollectorNumber: entry.CollectorNumber.Trim()),
                entry.Language.Trim().ToLowerInvariant()));
        }

        if (entry.OracleId is Guid oracleId)
        {
            return ("oracle-id", new ScryfallEvidenceLookup(
                new ScryfallCardLookup("oracle-id", oracleId.ToString("D"))));
        }

        return string.IsNullOrWhiteSpace(entry.CardName)
            ? (null, null)
            : ("exact-name", new ScryfallEvidenceLookup(
                new ScryfallCardLookup("exact-name", entry.CardName.Trim())));
    }

    /// <summary>
    /// Produces a stable deduplication key including the exact language constraint.
    /// </summary>
    private static string LookupKey(ScryfallEvidenceLookup lookup)
    {
        ScryfallCardLookup value = lookup.Lookup;
        return string.Join(
            '|',
            value.Kind,
            value.Value?.Trim() ?? string.Empty,
            value.SetCode?.Trim().ToLowerInvariant() ?? string.Empty,
            value.CollectorNumber?.Trim() ?? string.Empty,
            lookup.RequiredLanguage ?? string.Empty);
    }

    /// <summary>
    /// Builds the caller-visible proposal and its evidence-bound replay token.
    /// </summary>
    private DeckIdentityReconciliationPreview BuildPreview(
        DeckDocument deck,
        IReadOnlyList<DeckEntry> entries,
        IReadOnlyList<DeckIdentityLookupPlan> plans,
        ScryfallExactCollectionEvidence? evidence)
    {
        List<DeckIdentityReconciliationRow> rows = [];
        for (int index = 0; index < plans.Count; index++)
        {
            DeckIdentityLookupPlan plan = plans[index];
            ScryfallCollectionRow? evidenceRow = plan.EvidenceIndex is int evidenceIndex &&
                evidence is not null &&
                evidenceIndex < evidence.Rows.Count
                    ? evidence.Rows[evidenceIndex]
                    : null;
            rows.Add(BuildRow(plan, evidenceRow, evidence?.Binding));
        }

        bool isComplete = rows.All(value => value.Status is "resolved" or "unchanged");
        int proposedChangeCount = rows.Count(value => value.Status == "resolved");
        Guid[] selectedIds = entries.Select(value => value.EntryId).ToArray();
        string fingerprint = DeckIdentityApplyToken.Hash(JsonSerializer.Serialize(
            new
            {
                deckId = deck.DeckId,
                deckRevision = deck.Revision,
                entryIds = selectedIds,
                rows,
                evidence = evidence?.Binding,
            },
            SerializerOptions));
        string token = tokens.Encode(new DeckIdentityApplyTokenPayload(
            1,
            deck.DeckId,
            deck.Revision,
            selectedIds,
            evidence?.Binding,
            fingerprint));
        return new DeckIdentityReconciliationPreview(
            deck.DeckId,
            deck.Revision,
            rows,
            isComplete,
            proposedChangeCount,
            evidence?.Binding,
            fingerprint,
            token);
    }

    /// <summary>
    /// Classifies one exact evidence row and limits proposals to identity fields.
    /// </summary>
    private static DeckIdentityReconciliationRow BuildRow(
        DeckIdentityLookupPlan plan,
        ScryfallCollectionRow? evidenceRow,
        ScryfallCollectionEvidenceBinding? binding)
    {
        DeckEntryIdentity before = Identity(plan.Entry);
        if (plan.Lookup is null || evidenceRow is null)
        {
            return Row(plan, "unresolved", "No complete exact identity is available.", before, null, null, binding);
        }

        if (evidenceRow.Status != "found" || evidenceRow.Card is null)
        {
            string status = evidenceRow.Status is "not-cached" ? "not-cached" : "unresolved";
            return Row(plan, status, evidenceRow.Message ?? "Exact identity evidence is unavailable.", before, null,
                evidenceRow.Origin, binding);
        }

        ScryfallCard card = evidenceRow.Card;
        if (!IdentityFieldsAgree(plan.Entry, plan.MatchMethod, card))
        {
            return Row(
                plan,
                "conflict",
                "Stored identity fields disagree with the strongest exact Scryfall match.",
                before,
                null,
                evidenceRow.Origin,
                binding);
        }

        DeckEntryIdentity after = plan.MatchMethod is "scryfall-printing-id" or "set-collector-language"
            ? new DeckEntryIdentity(
                card.Name,
                card.OracleId,
                card.Id,
                card.SetCode,
                card.CollectorNumber,
                card.Language)
            : before with
            {
                CardName = card.Name,
                OracleId = card.OracleId,
            };
        bool changed = before != after;
        return Row(
            plan,
            changed ? "resolved" : "unchanged",
            changed ? "Exact Scryfall evidence supplies canonical identity fields." : "Identity fields are already canonical.",
            before,
            after,
            evidenceRow.Origin,
            binding);
    }

    /// <summary>
    /// Creates one ordered result row with shared evidence lineage.
    /// </summary>
    private static DeckIdentityReconciliationRow Row(
        DeckIdentityLookupPlan plan,
        string status,
        string message,
        DeckEntryIdentity before,
        DeckEntryIdentity? after,
        string? origin,
        ScryfallCollectionEvidenceBinding? binding)
    {
        return new DeckIdentityReconciliationRow(
            plan.Entry.EntryId,
            status,
            plan.MatchMethod,
            message,
            before,
            after,
            origin,
            binding?.CorpusGenerationId,
            binding?.Snapshot);
    }

    /// <summary>
    /// Requires all stored stronger and weaker identity fields to agree with the exact match.
    /// </summary>
    private static bool IdentityFieldsAgree(
        DeckEntry entry,
        string? matchMethod,
        ScryfallCard card)
    {
        bool baseIdentity = (entry.PrintingId is null || entry.PrintingId == card.Id) &&
            (entry.OracleId is null || entry.OracleId == card.OracleId) &&
            (string.IsNullOrWhiteSpace(entry.CardName) ||
             string.Equals(entry.CardName.Trim(), card.Name, StringComparison.OrdinalIgnoreCase));
        if (!baseIdentity || matchMethod is not ("scryfall-printing-id" or "set-collector-language"))
        {
            return baseIdentity;
        }

        return
            (string.IsNullOrWhiteSpace(entry.SetCode) ||
             string.Equals(entry.SetCode.Trim(), card.SetCode, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(entry.CollectorNumber) ||
             string.Equals(entry.CollectorNumber.Trim(), card.CollectorNumber, StringComparison.Ordinal)) &&
            (string.IsNullOrWhiteSpace(entry.Language) ||
             string.Equals(entry.Language.Trim(), card.Language, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Projects one entry's editable identity fields.
    /// </summary>
    private static DeckEntryIdentity Identity(DeckEntry entry)
    {
        return new DeckEntryIdentity(
            entry.CardName,
            entry.OracleId,
            entry.PrintingId,
            entry.SetCode,
            entry.CollectorNumber,
            entry.Language);
    }

    /// <summary>
    /// Preserves every non-success operation case while changing only its generic success type.
    /// </summary>
    private static OperationResult<TTarget> ForwardFailure<TSource, TTarget>(
        OperationResult<TSource> result)
    {
        return result switch
        {
            OperationNotFound value => value,
            OperationNotCached value => value,
            OperationUnsupported value => value,
            OperationUnavailable value => value,
            OperationConflict value => value,
            OperationInvalidInput value => value,
            OperationSuccess<TSource> => new OperationUnavailable(
                "unexpected-success-shape",
                "The operation returned an unexpected success shape."),
        };
    }
}

/// <summary>
/// Exposes exact identity preview and revision-guarded apply as deck-owned MCP operations.
/// </summary>
internal sealed class DeckIdentityReconciliationReadTools
{
    /// <summary>
    /// Stores the identity-only workflow coordinator.
    /// </summary>
    private readonly DeckIdentityReconciliationCoordinator coordinator;

    /// <summary>
    /// Creates the identity preview tool around one workflow coordinator.
    /// </summary>
    internal DeckIdentityReconciliationReadTools(DeckIdentityReconciliationCoordinator coordinator)
    {
        this.coordinator = coordinator;
    }

    /// <summary>
    /// Resolves exact Scryfall identities and returns an evidence-bound proposal without mutation.
    /// </summary>
    [McpServerTool(
        Name = "deck_identity_reconcile_preview",
        Title = "Preview Local Deck Identity Reconciliation",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description("Previews exact Scryfall-backed identity normalization without fuzzy matching, legality checks, or deck mutation.")]
    internal Task<OperationResult<DeckIdentityReconciliationPreview>> PreviewAsync(
        [Description("Stable local deck UUID.")] Guid deckId,
        [Description("Current deck revision required for optimistic concurrency.")] long expectedRevision,
        [Description("Optional unique entry UUIDs; omit to select every entry, up to 150.")]
        IReadOnlyList<Guid>? entryIds = null,
        [Description("Scryfall evidence policy: default, cache-only, or refresh.")]
        string freshnessPolicy = "default",
        CancellationToken cancellationToken = default)
    {
        return coordinator.PreviewAsync(
            deckId,
            expectedRevision,
            entryIds,
            freshnessPolicy,
            cancellationToken);
    }

}

/// <summary>
/// Exposes identity apply only when the effective operation mode permits local writes.
/// </summary>
internal sealed class DeckIdentityReconciliationWriteTools
{
    /// <summary>
    /// Stores the identity-only workflow coordinator.
    /// </summary>
    private readonly DeckIdentityReconciliationCoordinator coordinator;

    /// <summary>
    /// Stores the effective operation mode for apply defense in depth.
    /// </summary>
    private readonly OperationMode mode;

    /// <summary>
    /// Creates the identity apply tool around one coordinator and validated operation mode.
    /// </summary>
    internal DeckIdentityReconciliationWriteTools(
        DeckIdentityReconciliationCoordinator coordinator,
        OperationMode mode)
    {
        this.coordinator = coordinator;
        this.mode = mode;
    }

    /// <summary>
    /// Replays retained evidence and atomically applies only accepted identity field changes.
    /// </summary>
    [McpServerTool(
        Name = "deck_identity_reconcile_apply",
        Title = "Apply Local Deck Identity Reconciliation",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Applies an unchanged exact identity preview in one revision; all non-identity deck fields are preserved.")]
    internal Task<OperationResult<DeckDocument>> ApplyAsync(
        [Description("Stable local deck UUID used for preview.")] Guid deckId,
        [Description("Deck revision used for preview.")] long expectedRevision,
        [Description("Evidence-bound fingerprint returned by deck_identity_reconcile_preview.")]
        string previewFingerprint,
        [Description("Opaque apply token returned by deck_identity_reconcile_preview.")]
        string applyToken,
        [Description("Whether resolved rows may be applied when other selected identities remain incomplete.")]
        bool allowPartial = false,
        CancellationToken cancellationToken = default)
    {
        if (!OperationModeGuard.Allows(mode, OperationRequirement.LocalWrite))
        {
            return Task.FromResult<OperationResult<DeckDocument>>(
                new OperationUnsupported(
                    "operation-mode-denied",
                    "The effective operation mode does not permit local writes."));
        }

        return coordinator.ApplyAsync(
            deckId,
            expectedRevision,
            previewFingerprint,
            applyToken,
            allowPartial,
            cancellationToken);
    }
}
