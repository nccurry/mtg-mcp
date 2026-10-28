using System.Text.Json.Serialization;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Scryfall;

/// <summary>
/// Returns the sections of one card's details that the caller asked for, with the IDs the lookup resolved to.
/// A section not asked for is absent.
/// </summary>
internal sealed record ScryfallCardDetailsResult(
    [property: JsonPropertyName("oracleId")] Guid OracleId,
    [property: JsonPropertyName("scryfallId")] Guid ScryfallId,
    [property: JsonPropertyName("prints")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ScryfallPrintsResult? Prints,
    [property: JsonPropertyName("rulings")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ScryfallRulingsResult? Rulings);

/// <summary>
/// Returns one reference result whose <c>kind</c> matches the requested <c>kind</c>: sets, catalog, or bulk.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ScryfallSetsReference), "sets")]
[JsonDerivedType(typeof(ScryfallCatalogReference), "catalog")]
[JsonDerivedType(typeof(ScryfallBulkReference), "bulk")]
internal abstract record ScryfallReferenceResult;

/// <summary>
/// Holds the set list or one set for <c>kind</c> sets.
/// </summary>
internal sealed record ScryfallSetsReference(
    [property: JsonPropertyName("sets")] ScryfallSetsResult Sets) : ScryfallReferenceResult;

/// <summary>
/// Holds one catalog page for <c>kind</c> catalog.
/// </summary>
internal sealed record ScryfallCatalogReference(
    [property: JsonPropertyName("catalog")] ScryfallCatalogResult Catalog) : ScryfallReferenceResult;

/// <summary>
/// Holds the bulk data metadata for <c>kind</c> bulk.
/// </summary>
internal sealed record ScryfallBulkReference(
    [property: JsonPropertyName("bulk")] ScryfallBulkMetadataResult Bulk) : ScryfallReferenceResult;

/// <summary>
/// Returns one search snapshot result whose <c>kind</c> matches the requested <c>kind</c>: list or get.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ScryfallSnapshotListReference), "list")]
[JsonDerivedType(typeof(ScryfallSnapshotGetReference), "get")]
internal abstract record ScryfallSnapshotsResult;

/// <summary>
/// Holds one page of snapshot summaries for <c>kind</c> list.
/// </summary>
internal sealed record ScryfallSnapshotListReference(
    [property: JsonPropertyName("snapshots")] ScryfallPage<ScryfallSnapshotSummary> Snapshots)
    : ScryfallSnapshotsResult;

/// <summary>
/// Holds one page of one snapshot's members for <c>kind</c> get.
/// </summary>
internal sealed record ScryfallSnapshotGetReference(
    [property: JsonPropertyName("snapshot")] ScryfallSnapshotPage Snapshot) : ScryfallSnapshotsResult;
