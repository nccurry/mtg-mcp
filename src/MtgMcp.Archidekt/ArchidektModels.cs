using System.Text.Json;
using System.Text.Json.Serialization;

namespace MtgMcp.Archidekt;

/// <summary>
/// Reports credential readiness without revealing an account identity or secret location.
/// </summary>
public sealed record ArchidektAuthStatus(
    string State,
    bool CredentialsConfigured,
    bool SessionAuthenticated,
    string Message);

/// <summary>
/// Identifies the observed provider contract and exact retrieval used for evidence.
/// </summary>
public sealed record ArchidektRetrievalEvidence(
    string Source,
    string Method,
    string ContractVersion,
    DateTimeOffset RetrievedAtUtc,
    string SourceChecksum);

/// <summary>
/// Describes one Archidekt deck without loading its complete content.
/// </summary>
public sealed record RemoteDeckSummary(
    string RemoteId,
    string Name,
    string? Description,
    string Format,
    string Visibility,
    string? ParentFolderId,
    string? ParentFolderName,
    string? ParentFolderPath,
    int? CardCount,
    DateTimeOffset? UpdatedAtUtc,
    string Fingerprint)
{
    /// <summary>
    /// Gets true when <c>Description</c> was cut to keep a page of summaries under the size limit.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DescriptionTruncated { get; init; }
}

/// <summary>
/// Preserves one provider category and the flags that influence board membership.
/// </summary>
public sealed record RemoteDeckCategory(
    string ProviderCategoryId,
    string Name,
    bool? IncludedInDeck,
    bool? IncludedInPrice,
    bool IsPremier,
    int SortOrder);

/// <summary>
/// Holds one Archidekt printing as a deck relation or a card search result shows it.
/// </summary>
internal sealed record ArchidektCardPrinting(
    string ProviderCardId,
    string CardName,
    Guid? OracleId,
    Guid? PrintingId,
    string? SetCode,
    string? CollectorNumber);

/// <summary>
/// Preserves one independently addressable card relation from an Archidekt deck.
/// </summary>
public sealed record RemoteDeckEntry(
    string ProviderRelationId,
    string ProviderCardId,
    int Quantity,
    string CardName,
    Guid? OracleId,
    Guid? PrintingId,
    string? SetCode,
    string? CollectorNumber,
    string Language,
    string Finish,
    string Zone,
    IReadOnlyList<string> CategoryNames,
    string? PrimaryCategoryName,
    int SortOrder)
{
    /// <summary>
    /// Gets the immutable category-name snapshot in provider order.
    /// </summary>
    public IReadOnlyList<string> CategoryNames { get; init; } =
        Array.AsReadOnly(CategoryNames.ToArray());
}

/// <summary>
/// Carries a complete canonical remote deck observation with preserved extension fields.
/// </summary>
public sealed record RemoteDeckSnapshot(
    string RemoteId,
    string RemoteUri,
    string Name,
    string Description,
    string Format,
    string Visibility,
    string? ParentFolderId,
    IReadOnlyList<RemoteDeckCategory> Categories,
    IReadOnlyList<RemoteDeckEntry> Entries,
    IReadOnlyDictionary<string, JsonElement> Extensions,
    ArchidektRetrievalEvidence Evidence,
    string ContentFingerprint,
    string RemoteFingerprint)
{
    /// <summary>
    /// Gets an immutable category snapshot in canonical order.
    /// </summary>
    public IReadOnlyList<RemoteDeckCategory> Categories { get; init; } =
        Array.AsReadOnly(Categories.ToArray());

    /// <summary>
    /// Gets an immutable entry snapshot in canonical order.
    /// </summary>
    public IReadOnlyList<RemoteDeckEntry> Entries { get; init; } =
        Array.AsReadOnly(Entries.ToArray());

    /// <summary>
    /// Gets copied provider fields not consumed by the normalized projection.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> Extensions { get; init; } =
        new Dictionary<string, JsonElement>(Extensions, StringComparer.Ordinal);

    /// <summary>
    /// Tells whether Archidekt counts an entry toward the deck: its first category is included in the deck, or it has
    /// no category, or the category has no flag. Worked out from the categories, never stored.
    /// </summary>
    public bool IsInDeck(RemoteDeckEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return ArchidektJsonContract.IsInDeck(
            entry.CategoryNames,
            entry.PrimaryCategoryName,
            CategoryNames.Index(Categories, value => value.Name));
    }
}

/// <summary>
/// Carries every deck the account owns, as summaries in canonical order.
/// </summary>
public sealed record RemoteDeckList(
    IReadOnlyList<RemoteDeckSummary> Items,
    ArchidektRetrievalEvidence Evidence)
{
    /// <summary>
    /// Gets the immutable deck summaries.
    /// </summary>
    public IReadOnlyList<RemoteDeckSummary> Items { get; init; } =
        Array.AsReadOnly(Items.ToArray());
}

/// <summary>
/// Preserves one folder and its direct provider relationships.
/// </summary>
public sealed record RemoteFolderRecord(
    string FolderId,
    string Name,
    string Visibility,
    string? ParentFolderId,
    string Path,
    IReadOnlyList<string> ChildFolderIds,
    IReadOnlyList<RemoteDeckSummary> Decks,
    IReadOnlyDictionary<string, JsonElement> Extensions)
{
    /// <summary>
    /// Gets immutable direct child-folder identifiers.
    /// </summary>
    public IReadOnlyList<string> ChildFolderIds { get; init; } =
        Array.AsReadOnly(ChildFolderIds.ToArray());

    /// <summary>
    /// Gets immutable directly contained deck summaries.
    /// </summary>
    public IReadOnlyList<RemoteDeckSummary> Decks { get; init; } =
        Array.AsReadOnly(Decks.ToArray());

    /// <summary>
    /// Gets copied provider fields not consumed by the normalized folder projection.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> Extensions { get; init; } =
        new Dictionary<string, JsonElement>(Extensions, StringComparer.Ordinal);
}

/// <summary>
/// Carries a complete canonical folder tree or one verified detail result.
/// </summary>
public sealed record RemoteFolderTree(
    IReadOnlyList<RemoteFolderRecord> Items,
    ArchidektRetrievalEvidence Evidence,
    string TreeFingerprint)
{
    /// <summary>
    /// Gets folders in canonical parent/path/id order.
    /// </summary>
    public IReadOnlyList<RemoteFolderRecord> Items { get; init; } =
        Array.AsReadOnly(Items.ToArray());
}

/// <summary>
/// Describes one saved Archidekt deck snapshot without requiring its full deck state.
/// </summary>
public sealed record RemoteNamedSnapshotSummary(
    string SnapshotId,
    string RemoteDeckId,
    string Name,
    string? Description,
    DateTimeOffset? CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string Checksum,
    IReadOnlyDictionary<string, JsonElement> Extensions)
{
    /// <summary>
    /// Gets copied provider fields not consumed by the normalized summary.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> Extensions { get; init; } =
        new Dictionary<string, JsonElement>(Extensions, StringComparer.Ordinal);
}

/// <summary>
/// Carries a named snapshot collection and its evidence checksum.
/// </summary>
public sealed record RemoteNamedSnapshotPage(
    IReadOnlyList<RemoteNamedSnapshotSummary> Items,
    ArchidektRetrievalEvidence Evidence,
    string CollectionChecksum)
{
    /// <summary>
    /// Gets immutable snapshot summaries in canonical order.
    /// </summary>
    public IReadOnlyList<RemoteNamedSnapshotSummary> Items { get; init; } =
        Array.AsReadOnly(Items.ToArray());
}

/// <summary>
/// Carries one named snapshot together with its complete saved deck state.
/// </summary>
public sealed record RemoteNamedSnapshot(
    RemoteNamedSnapshotSummary Summary,
    RemoteDeckSnapshot Deck,
    ArchidektRetrievalEvidence Evidence);

/// <summary>
/// Names one comparison path and its state; the full and the concise difference rows both start with these two fields.
/// </summary>
[JsonDerivedType(typeof(ArchidektDifference))]
[JsonDerivedType(typeof(ArchidektConciseDifference))]
public abstract record ArchidektDifferenceRow(string Path, string State);

/// <summary>
/// Describes one exact local, baseline, and remote comparison path with the whole value of each side.
/// </summary>
public sealed record ArchidektDifference(
    string Path,
    string State,
    string? BaselineValue,
    string? LocalValue,
    string? RemoteValue)
    : ArchidektDifferenceRow(Path, State);

/// <summary>
/// Shows one differing field of a row: its value on each side. A side that does not hold the row has no value, and a
/// side that holds the row with an empty field has a JSON null.
/// </summary>
public sealed record ArchidektDifferenceField(
    string Name,
    JsonElement? Baseline,
    JsonElement? Local,
    JsonElement? Remote);

/// <summary>
/// Describes one comparison path by the fields whose values differ, instead of the whole value of each side. The whole
/// values on a full row are the JSON of one entry or category (or one text value), so the fields are read back from
/// that JSON and nothing is recomputed from the decks.
/// </summary>
public sealed record ArchidektConciseDifference(
    string Path,
    string State,
    IReadOnlyList<ArchidektDifferenceField> Fields)
    : ArchidektDifferenceRow(Path, State)
{
    /// <summary>
    /// Gets the differing fields in the order the entry or category lists them.
    /// </summary>
    public IReadOnlyList<ArchidektDifferenceField> Fields { get; init; } = Array.AsReadOnly(Fields.ToArray());

    /// <summary>
    /// Lists the fields of a row whose values are not the same on the baseline, local, and remote side. A side that
    /// lacks the row counts as a different value, so an added or removed row lists the fields of the sides that hold
    /// it. A row for one text value, such as the deck name, lists that one value under its last path segment. A
    /// remote change whose local side was not compared (a stale zone move) takes the baseline row as the local row, so
    /// it lists only the fields Archidekt changed and does not look like a local removal.
    /// </summary>
    public static ArchidektConciseDifference From(ArchidektDifference difference)
    {
        ArgumentNullException.ThrowIfNull(difference);
        bool holdsObject = difference.Path.StartsWith("/entries/", StringComparison.Ordinal) ||
            difference.Path.StartsWith("/categories/", StringComparison.Ordinal);
        return new ArchidektConciseDifference(
            difference.Path,
            difference.State,
            holdsObject ? ObjectFields(difference) : [TextField(difference)]);
    }
    /// <summary>
    /// Builds the one field of a row that holds a single text value.
    /// </summary>
    private static ArchidektDifferenceField TextField(ArchidektDifference difference)
    {
        return new ArchidektDifferenceField(
            difference.Path[(difference.Path.LastIndexOf('/') + 1)..],
            Text(difference.BaselineValue),
            Text(difference.LocalValue),
            Text(difference.RemoteValue));
    }

    /// <summary>
    /// Wraps a text value as a JSON string, or returns null when the side has no value.
    /// </summary>
    private static JsonElement? Text(string? value)
    {
        return value is null ? null : JsonSerializer.SerializeToElement(value);
    }

    /// <summary>
    /// Lists the fields of an entry or category row that differ between its three sides.
    /// </summary>
    private static List<ArchidektDifferenceField> ObjectFields(ArchidektDifference difference)
    {
        Dictionary<string, JsonElement>? baseline = Properties(difference.BaselineValue);
        bool localNotCompared = difference.State == ArchidektDifferenceStates.RemoteChanged &&
            difference.LocalValue is null;
        Dictionary<string, JsonElement>?[] sides =
        [
            baseline,
            localNotCompared ? baseline : Properties(difference.LocalValue),
            Properties(difference.RemoteValue),
        ];
        List<string> names = [];
        foreach (Dictionary<string, JsonElement>? side in sides)
        {
            if (side is null)
            {
                continue;
            }

            foreach (string name in side.Keys)
            {
                if (!names.Contains(name, StringComparer.Ordinal))
                {
                    names.Add(name);
                }
            }
        }

        List<ArchidektDifferenceField> fields = [];
        foreach (string name in names)
        {
            JsonElement? baselineValue = ValueOf(sides[0], name);
            JsonElement? localValue = ValueOf(sides[1], name);
            JsonElement? remoteValue = ValueOf(sides[2], name);
            if (!SameValue(baselineValue, localValue) || !SameValue(localValue, remoteValue))
            {
                fields.Add(new ArchidektDifferenceField(name, baselineValue, localValue, remoteValue));
            }
        }

        return fields;
    }

    /// <summary>
    /// Reads the properties of the JSON object a row side holds, or returns null when the side lacks the row.
    /// </summary>
    private static Dictionary<string, JsonElement>? Properties(string? json)
    {
        if (json is null)
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(json);
        Dictionary<string, JsonElement> properties = new(StringComparer.Ordinal);
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            properties[property.Name] = property.Value.Clone();
        }

        return properties;
    }

    /// <summary>
    /// Gets one property of a row side, or null when the side lacks the row or the property.
    /// </summary>
    private static JsonElement? ValueOf(Dictionary<string, JsonElement>? side, string name)
    {
        return side is not null && side.TryGetValue(name, out JsonElement value) ? value : null;
    }

    /// <summary>
    /// Tells whether two sides hold the same JSON value, or both hold none.
    /// </summary>
    private static bool SameValue(JsonElement? left, JsonElement? right)
    {
        return left is null || right is null
            ? left is null && right is null
            : string.Equals(left.Value.GetRawText(), right.Value.GetRawText(), StringComparison.Ordinal);
    }
}

/// <summary>
/// Carries the three-way state required for an explicit synchronization decision.
/// </summary>
public sealed record ArchidektSyncDiff(
    Guid LocalDeckId,
    long Revision,
    string RemoteDeckId,
    string RemoteFingerprint,
    string? BaselineRemoteFingerprint,
    bool HasConflicts,
    IReadOnlyList<ArchidektDifference> Differences)
{
    /// <summary>
    /// Gets immutable path-addressed differences.
    /// </summary>
    public IReadOnlyList<ArchidektDifference> Differences { get; init; } =
        Array.AsReadOnly(Differences.ToArray());
}

/// <summary>
/// Carries immutable guards and exact operations for a later synchronization apply.
/// </summary>
public sealed record ArchidektSyncPreview(
    string Direction,
    Guid? LocalDeckId,
    long? Revision,
    string RemoteDeckId,
    string RemoteFingerprint,
    string ContentFingerprint,
    string PreviewFingerprint,
    bool HasConflicts,
    IReadOnlyList<ArchidektDifference> Differences,
    IReadOnlyList<ArchidektRemoteOperation> Operations,
    int PredictedProviderRequests)
{
    /// <summary>
    /// Gets the request limit: the most requests to Archidekt one tool call may send, which
    /// <see cref="PredictedProviderRequests"/> counts against; null when the preview makes no such prediction.
    /// </summary>
    public int? ProviderRequestLimit { get; init; }

    /// <summary>
    /// Gets the options the apply tool must repeat from this preview, such as <c>discardLocalChanges</c>; null when it
    /// needs none.
    /// </summary>
    public string? ApplyOptionsNotice { get; init; }

    /// <summary>
    /// Gets the reason the apply tool would refuse this preview, such as a card with no Archidekt printing or a change
    /// on the other side; null when it would not.
    /// </summary>
    public string? ApplyBlockedNotice { get; init; }

    /// <summary>
    /// Gets immutable path differences.
    /// </summary>
    public IReadOnlyList<ArchidektDifference> Differences { get; init; } =
        Array.AsReadOnly(Differences.ToArray());

    /// <summary>
    /// Gets immutable provider operations in execution order.
    /// </summary>
    public IReadOnlyList<ArchidektRemoteOperation> Operations { get; init; } =
        Array.AsReadOnly(Operations.ToArray());
}

/// <summary>
/// Carries a push plan whose new cards have Archidekt printings, the operations as shown to the caller, and the
/// printings chosen so a later apply does not search again.
/// </summary>
public sealed record ArchidektPushPlan(
    RemoteDeckSnapshot Target,
    ArchidektRemotePlan Plan,
    IReadOnlyList<ArchidektRemoteOperation> Operations,
    int PredictedProviderRequests,
    int ProviderRequestLimit,
    ArchidektPushBlock? Block,
    ArchidektPrintingChoices Choices);

/// <summary>
/// Says why a push cannot be sent, with its reason code: <c>push-printing-unresolved</c> when a card has no Archidekt
/// printing, <c>request-limit-exceeded</c> when the lookups or the push would pass the request limit.
/// </summary>
public sealed record ArchidektPushBlock(string Notice, string ReasonCode)
{
    /// <summary>
    /// Gets true when the push is blocked by the request limit rather than by an argument.
    /// </summary>
    public bool IsRequestLimit => ReasonCode == ArchidektProviderException.RequestLimitReasonCode;
}

/// <summary>
/// Describes one previewed primitive provider operation without exposing transport payloads.
/// </summary>
public sealed record ArchidektRemoteOperation(
    int Sequence,
    string Kind,
    string Subject,
    string Summary);

/// <summary>
/// Reports the final status of one attempted provider operation.
/// </summary>
public sealed record ArchidektOperationStatus(
    int Sequence,
    string Kind,
    string Subject,
    string Status,
    string Message);

/// <summary>
/// Reports a guarded apply, including partial or unknown remote state.
/// </summary>
public sealed record ArchidektApplyResult(
    string Outcome,
    Guid? LocalDeckId,
    long? Revision,
    string RemoteDeckId,
    string? FinalRemoteFingerprint,
    IReadOnlyList<ArchidektOperationStatus> Operations)
{
    /// <summary>
    /// Tells what to do when the writes were sent but the deck could not be read afterwards.
    /// </summary>
    public const string UnverifiedNotice =
        "The operations were sent, but Archidekt could not be read afterwards, so the result is not verified and " +
        "the local baseline was not updated. Run archidekt_pull_preview to see the Archidekt deck before pushing " +
        "again.";

    /// <summary>
    /// Gets immutable operation statuses in attempted order.
    /// </summary>
    public IReadOnlyList<ArchidektOperationStatus> Operations { get; init; } =
        Array.AsReadOnly(Operations.ToArray());

    /// <summary>
    /// Gets local entry IDs a verified push could not link to provider rows; omitted when all linked.
    /// The next push removes and recreates these rows instead of duplicating them.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<Guid>? UnlinkedLocalIds { get; init; }

    /// <summary>
    /// Gets a sentence about the outcome that the operation statuses do not say, such as why the final read failed;
    /// omitted when there is nothing to add.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Notice { get; init; }

    /// <summary>
    /// Gets the rows that still differ from the pushed deck when the check after the writes found a difference;
    /// omitted otherwise. Only the first rows are listed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ArchidektVerificationDifference>? Differences { get; init; }
}

/// <summary>
/// Names one row of the Archidekt deck that still differed from the pushed deck after the writes.
/// </summary>
public sealed record ArchidektVerificationDifference(string Kind, string Subject, string Summary);

/// <summary>
/// Reports one typed folder move result after verification.
/// </summary>
public sealed record ArchidektFolderMoveStatus(
    string Kind,
    string Id,
    string? PreviousParentFolderId,
    string? FinalParentFolderId,
    string Status);

/// <summary>
/// Carries verified folder move outcomes and the resulting tree fingerprint.
/// </summary>
public sealed record ArchidektFolderMoveResult(
    IReadOnlyList<ArchidektFolderMoveStatus> Items,
    string FinalTreeFingerprint)
{
    /// <summary>
    /// Gets immutable per-item verification states.
    /// </summary>
    public IReadOnlyList<ArchidektFolderMoveStatus> Items { get; init; } =
        Array.AsReadOnly(Items.ToArray());
}

/// <summary>
/// Carries a guarded snapshot restore preview.
/// </summary>
public sealed record ArchidektSnapshotRestorePreview(
    string RemoteDeckId,
    string SnapshotId,
    string SnapshotChecksum,
    string SnapshotContentFingerprint,
    string RemoteFingerprint,
    string PreviewFingerprint,
    IReadOnlyList<ArchidektDifference> Differences,
    IReadOnlyList<ArchidektRemoteOperation> Operations,
    int PredictedProviderRequests)
{
    /// <summary>
    /// Gets immutable snapshot-to-current differences.
    /// </summary>
    public IReadOnlyList<ArchidektDifference> Differences { get; init; } =
        Array.AsReadOnly(Differences.ToArray());

    /// <summary>
    /// Gets immutable restore operations.
    /// </summary>
    public IReadOnlyList<ArchidektRemoteOperation> Operations { get; init; } =
        Array.AsReadOnly(Operations.ToArray());
}
