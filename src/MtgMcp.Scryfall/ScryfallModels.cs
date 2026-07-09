using System.Text.Json;
using System.Text.Json.Serialization;
using MtgMcp.Core.Evidence;

namespace MtgMcp.Scryfall;

/// <summary>
/// Selects how provider-backed reads use existing evidence or acquire a replacement.
/// </summary>
internal enum ScryfallFreshnessPolicy
{
    /// <summary>
    /// Reuses eligible evidence and acquires a miss when local writes are allowed.
    /// </summary>
    Default,

    /// <summary>
    /// Performs no network access and returns any available stored evidence explicitly.
    /// </summary>
    CacheOnly,

    /// <summary>
    /// Bypasses cache eligibility and acquires a new immutable snapshot.
    /// </summary>
    Refresh,
}

/// <summary>
/// Describes one exact Scryfall card lookup case without fuzzy field inference.
/// </summary>
public sealed record ScryfallCardLookup(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("value")] string? Value = null,
    [property: JsonPropertyName("setCode")] string? SetCode = null,
    [property: JsonPropertyName("collectorNumber")] string? CollectorNumber = null);

/// <summary>
/// Adds an optional exact language requirement for internal deck-identity evidence resolution.
/// </summary>
public sealed record ScryfallEvidenceLookup(
    [property: JsonPropertyName("lookup")] ScryfallCardLookup Lookup,
    [property: JsonPropertyName("requiredLanguage")] string? RequiredLanguage = null);

/// <summary>
/// Preserves one normalized card face alongside its lossless source object.
/// </summary>
public sealed record ScryfallCardFace(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("manaCost")] string? ManaCost,
    [property: JsonPropertyName("typeLine")] string? TypeLine,
    [property: JsonPropertyName("oracleText")] string? OracleText,
    [property: JsonPropertyName("colors")] IReadOnlyList<string> Colors,
    [property: JsonPropertyName("imageUris")] IReadOnlyDictionary<string, string> ImageUris,
    [property: JsonPropertyName("illustrationId")] Guid? IllustrationId,
    [property: JsonPropertyName("raw"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Raw);

/// <summary>
/// Identifies one direct or inherited community-tag assignment supporting a card result.
/// </summary>
public sealed record ScryfallTagEvidence(
    [property: JsonPropertyName("tagId")] Guid TagId,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("tagType")] string TagType,
    [property: JsonPropertyName("weight")] string Weight,
    [property: JsonPropertyName("annotation")] string? Annotation,
    [property: JsonPropertyName("relationship")] string Relationship,
    [property: JsonPropertyName("hierarchyPath")] IReadOnlyList<Guid> HierarchyPath,
    [property: JsonPropertyName("evidence")] EvidenceDescriptor Evidence);

/// <summary>
/// Preserves one provider price field with its currency, finish, freshness, and retrieval evidence.
/// </summary>
public sealed record ScryfallPriceEvidence(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("amount")] string? Amount,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("finish")] string Finish,
    [property: JsonPropertyName("context")] string Context,
    [property: JsonPropertyName("freshness")] string Freshness,
    [property: JsonPropertyName("evidence")] EvidenceDescriptor Evidence);

/// <summary>
/// Preserves one provider popularity rank as contextual evidence rather than a quality score.
/// </summary>
public sealed record ScryfallRankEvidence(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("rank")] long Rank,
    [property: JsonPropertyName("context")] string Context,
    [property: JsonPropertyName("freshness")] string Freshness,
    [property: JsonPropertyName("evidence")] EvidenceDescriptor Evidence);

/// <summary>
/// Preserves a lossless Scryfall card object and a stable normalized projection.
/// </summary>
public sealed record ScryfallCard(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("oracleId")] Guid? OracleId,
    [property: JsonPropertyName("illustrationId")] Guid? IllustrationId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("setCode")] string SetCode,
    [property: JsonPropertyName("collectorNumber")] string CollectorNumber,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("releasedAt")] string? ReleasedAt,
    [property: JsonPropertyName("manaCost")] string? ManaCost,
    [property: JsonPropertyName("manaValue")] decimal? ManaValue,
    [property: JsonPropertyName("typeLine")] string? TypeLine,
    [property: JsonPropertyName("oracleText")] string? OracleText,
    [property: JsonPropertyName("colors")] IReadOnlyList<string> Colors,
    [property: JsonPropertyName("colorIdentity")] IReadOnlyList<string> ColorIdentity,
    [property: JsonPropertyName("keywords")] IReadOnlyList<string> Keywords,
    [property: JsonPropertyName("legalities")] IReadOnlyDictionary<string, string> Legalities,
    [property: JsonPropertyName("imageUris")] IReadOnlyDictionary<string, string> ImageUris,
    [property: JsonPropertyName("prices")] IReadOnlyDictionary<string, string?> Prices,
    [property: JsonPropertyName("priceEvidence")] IReadOnlyList<ScryfallPriceEvidence> PriceEvidence,
    [property: JsonPropertyName("rankEvidence")] IReadOnlyList<ScryfallRankEvidence> RankEvidence,
    [property: JsonPropertyName("faces")] IReadOnlyList<ScryfallCardFace> Faces,
    [property: JsonPropertyName("tags")] IReadOnlyList<ScryfallTagEvidence> Tags,
    [property: JsonPropertyName("tagCoverage")] string TagCoverage,
    [property: JsonPropertyName("evidence")] EvidenceDescriptor Evidence,
    [property: JsonPropertyName("raw"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Raw);

/// <summary>
/// Preserves one provider ruling as a source fact.
/// </summary>
public sealed record ScryfallRuling(
    [property: JsonPropertyName("oracleId")] Guid OracleId,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("publishedAt")] string PublishedAt,
    [property: JsonPropertyName("comment")] string Comment,
    [property: JsonPropertyName("evidence")] EvidenceDescriptor Evidence,
    [property: JsonPropertyName("raw"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Raw);

/// <summary>
/// Preserves one Scryfall set object and normalized identity fields.
/// </summary>
public sealed record ScryfallSet(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("setType")] string SetType,
    [property: JsonPropertyName("releasedAt")] string? ReleasedAt,
    [property: JsonPropertyName("cardCount")] int CardCount,
    [property: JsonPropertyName("digital")] bool Digital,
    [property: JsonPropertyName("evidence")] EvidenceDescriptor Evidence,
    [property: JsonPropertyName("raw"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Raw);

/// <summary>
/// Describes one official bulk dataset without exposing a local path.
/// </summary>
public sealed record ScryfallBulkData(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("updatedAtUtc")] DateTimeOffset UpdatedAtUtc,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("contentType")] string ContentType,
    [property: JsonPropertyName("contentEncoding")] string ContentEncoding,
    [property: JsonPropertyName("downloadUri")] string DownloadUri,
    [property: JsonPropertyName("jsonlDownloadUri")] string JsonlDownloadUri,
    [property: JsonPropertyName("raw")] JsonElement Raw);

/// <summary>
/// Identifies the immutable evidence snapshot backing a provider result.
/// </summary>
public sealed record ScryfallSnapshotReference(
    [property: JsonPropertyName("snapshotId")] Guid SnapshotId,
    [property: JsonPropertyName("checksum")] string Checksum,
    [property: JsonPropertyName("retrievedAtUtc")] DateTimeOffset RetrievedAtUtc,
    [property: JsonPropertyName("freshness")] string Freshness,
    [property: JsonPropertyName("predecessorId")] Guid? PredecessorId);

/// <summary>
/// Carries one stable bounded page and its opaque continuation cursor.
/// </summary>
public sealed record ScryfallPage<T>(
    [property: JsonPropertyName("items")] IReadOnlyList<T> Items,
    [property: JsonPropertyName("totalCount")] int TotalCount,
    [property: JsonPropertyName("nextCursor")] string? NextCursor);

/// <summary>
/// Returns one ordered card search page and its replay identity.
/// </summary>
public sealed record ScryfallSearchResult(
    [property: JsonPropertyName("page")] ScryfallPage<ScryfallCard> Page,
    [property: JsonPropertyName("snapshot")] ScryfallSnapshotReference Snapshot,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings);

/// <summary>
/// Returns one resolved card and whether it came from the corpus or request cache.
/// </summary>
public sealed record ScryfallCardResult(
    [property: JsonPropertyName("card")] ScryfallCard Card,
    [property: JsonPropertyName("origin")] string Origin,
    [property: JsonPropertyName("snapshot")] ScryfallSnapshotReference? Snapshot,
    [property: JsonPropertyName("corpusGenerationId")] Guid? CorpusGenerationId);

/// <summary>
/// Preserves the positional outcome for one collection identifier.
/// </summary>
public sealed record ScryfallCollectionRow(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("lookup")] ScryfallCardLookup Lookup,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("origin")] string? Origin,
    [property: JsonPropertyName("card")] ScryfallCard? Card,
    [property: JsonPropertyName("message")] string? Message);

/// <summary>
/// Returns ordered collection rows with optional provider snapshot lineage.
/// </summary>
public sealed record ScryfallCollectionResult(
    [property: JsonPropertyName("page")] ScryfallPage<ScryfallCollectionRow> Page,
    [property: JsonPropertyName("snapshot")] ScryfallSnapshotReference? Snapshot,
    [property: JsonPropertyName("corpusGenerationId")] Guid? CorpusGenerationId);

/// <summary>
/// Binds an exact collection result to retained corpus and provider evidence.
/// </summary>
public sealed record ScryfallCollectionEvidenceBinding(
    [property: JsonPropertyName("corpusGenerationId")] Guid? CorpusGenerationId,
    [property: JsonPropertyName("snapshot")] ScryfallSnapshotReference? Snapshot,
    [property: JsonPropertyName("evidenceChecksum")] string EvidenceChecksum);

/// <summary>
/// Returns every ordered exact-resolution row with the evidence required for deterministic replay.
/// </summary>
public sealed record ScryfallExactCollectionEvidence(
    [property: JsonPropertyName("rows")] IReadOnlyList<ScryfallCollectionRow> Rows,
    [property: JsonPropertyName("binding")] ScryfallCollectionEvidenceBinding Binding);

/// <summary>
/// Returns card printings from either the active corpus or an immutable request snapshot.
/// </summary>
public sealed record ScryfallPrintsResult(
    [property: JsonPropertyName("page")] ScryfallPage<ScryfallCard> Page,
    [property: JsonPropertyName("snapshot")] ScryfallSnapshotReference? Snapshot,
    [property: JsonPropertyName("corpusGenerationId")] Guid? CorpusGenerationId);

/// <summary>
/// Returns ordered rulings from either the active corpus or a request snapshot.
/// </summary>
public sealed record ScryfallRulingsResult(
    [property: JsonPropertyName("page")] ScryfallPage<ScryfallRuling> Page,
    [property: JsonPropertyName("snapshot")] ScryfallSnapshotReference? Snapshot,
    [property: JsonPropertyName("corpusGenerationId")] Guid? CorpusGenerationId);

/// <summary>
/// Returns one bounded set page and replay identity when provider-backed.
/// </summary>
public sealed record ScryfallSetsResult(
    [property: JsonPropertyName("page")] ScryfallPage<ScryfallSet> Page,
    [property: JsonPropertyName("snapshot")] ScryfallSnapshotReference Snapshot);

/// <summary>
/// Returns one bounded catalog value page and replay identity.
/// </summary>
public sealed record ScryfallCatalogResult(
    [property: JsonPropertyName("catalog")] string Catalog,
    [property: JsonPropertyName("page")] ScryfallPage<string> Page,
    [property: JsonPropertyName("snapshot")] ScryfallSnapshotReference Snapshot);

/// <summary>
/// Returns ordered autocomplete suggestions and replay identity.
/// </summary>
public sealed record ScryfallAutocompleteResult(
    [property: JsonPropertyName("page")] ScryfallPage<string> Page,
    [property: JsonPropertyName("snapshot")] ScryfallSnapshotReference Snapshot);

/// <summary>
/// Returns the fixed official bulk profile and its immutable request snapshot.
/// </summary>
public sealed record ScryfallBulkMetadataResult(
    [property: JsonPropertyName("datasets")] IReadOnlyList<ScryfallBulkData> Datasets,
    [property: JsonPropertyName("snapshot")] ScryfallSnapshotReference Snapshot);

/// <summary>
/// Reports one installed corpus dataset without a local path.
/// </summary>
public sealed record ScryfallCorpusDatasetStatus(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("providerId")] Guid ProviderId,
    [property: JsonPropertyName("providerUpdatedAtUtc")] DateTimeOffset ProviderUpdatedAtUtc,
    [property: JsonPropertyName("rowCount")] long RowCount,
    [property: JsonPropertyName("sourceBytes")] long SourceBytes,
    [property: JsonPropertyName("checksum")] string Checksum);

/// <summary>
/// Reports one complete corpus generation and its fixed dataset inventory.
/// </summary>
public sealed record ScryfallCorpusGenerationStatus(
    [property: JsonPropertyName("generationId")] Guid GenerationId,
    [property: JsonPropertyName("createdAtUtc")] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("datasets")] IReadOnlyList<ScryfallCorpusDatasetStatus> Datasets,
    [property: JsonPropertyName("integrity")] string Integrity);

/// <summary>
/// Reports the network-free state of the shared Scryfall corpus.
/// </summary>
public sealed record ScryfallCorpusStatus(
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("active")] ScryfallCorpusGenerationStatus? Active,
    [property: JsonPropertyName("previous")] ScryfallCorpusGenerationStatus? Previous,
    [property: JsonPropertyName("lastMetadataCheckAtUtc")] DateTimeOffset? LastMetadataCheckAtUtc,
    [property: JsonPropertyName("corpusAgeSeconds")] long? CorpusAgeSeconds,
    [property: JsonPropertyName("refreshEligible")] bool RefreshEligible);

/// <summary>
/// Describes a completed corpus activation or unchanged metadata check.
/// </summary>
public sealed record ScryfallCorpusSyncResult(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("activeGenerationId")] Guid GenerationId,
    [property: JsonPropertyName("previousGenerationId")] Guid? PreviousGenerationId,
    [property: JsonPropertyName("datasets")] IReadOnlyList<ScryfallCorpusDatasetStatus> Datasets);

/// <summary>
/// Describes the result of a guarded corpus rollback or deletion.
/// </summary>
public sealed record ScryfallCorpusMutationResult(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("activeGenerationId")] Guid? ActiveGenerationId,
    [property: JsonPropertyName("previousGenerationId")] Guid? PreviousGenerationId);

/// <summary>
/// Summarizes one immutable request snapshot without returning its raw payload.
/// </summary>
public sealed record ScryfallSnapshotSummary(
    [property: JsonPropertyName("snapshotId")] Guid SnapshotId,
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("retrievedAtUtc")] DateTimeOffset RetrievedAtUtc,
    [property: JsonPropertyName("checksum")] string Checksum,
    [property: JsonPropertyName("totalCount")] int TotalCount,
    [property: JsonPropertyName("predecessorId")] Guid? PredecessorId);

/// <summary>
/// Identifies one ordered member of an immutable request snapshot, with optional lossless source data.
/// </summary>
public sealed record ScryfallSnapshotMember(
    [property: JsonPropertyName("ordinal")] int Ordinal,
    [property: JsonPropertyName("checksum")] string Checksum,
    [property: JsonPropertyName("raw"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Raw);

/// <summary>
/// Replays one bounded page from an immutable provider request snapshot.
/// </summary>
public sealed record ScryfallSnapshotPage(
    [property: JsonPropertyName("summary")] ScryfallSnapshotSummary Summary,
    [property: JsonPropertyName("request")] JsonElement Request,
    [property: JsonPropertyName("items")] IReadOnlyList<ScryfallSnapshotMember> Items,
    [property: JsonPropertyName("nextCursor")] string? NextCursor);

/// <summary>
/// Describes a verified snapshot deletion.
/// </summary>
public sealed record ScryfallSnapshotDeleteResult(
    [property: JsonPropertyName("snapshotId")] Guid SnapshotId,
    [property: JsonPropertyName("checksum")] string Checksum);

/// <summary>
/// Preserves one community tag and its hierarchy metadata.
/// </summary>
public sealed record ScryfallTag(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("tagType")] string TagType,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("parentIds")] IReadOnlyList<Guid> ParentIds,
    [property: JsonPropertyName("childIds")] IReadOnlyList<Guid> ChildIds,
    [property: JsonPropertyName("aliases")] IReadOnlyList<string> Aliases,
    [property: JsonPropertyName("generationId")] Guid GenerationId,
    [property: JsonPropertyName("raw"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Raw);

/// <summary>
/// Returns cards assigned to one requested tag with direct/inherited evidence.
/// </summary>
public sealed record ScryfallCardsByTagResult(
    [property: JsonPropertyName("tag")] ScryfallTag Tag,
    [property: JsonPropertyName("page")] ScryfallPage<ScryfallCard> Page,
    [property: JsonPropertyName("assignments")] IReadOnlyList<ScryfallTagEvidence> Assignments,
    [property: JsonPropertyName("generationId")] Guid GenerationId);
