using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using ModelContextProtocol.Server;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Results;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Scryfall;

/// <summary>
/// Exposes the Scryfall tools that return one chosen kind of detail: card details, reference data, and saved
/// request snapshots. Each tool has one required field that picks the job, and the result names the job it did.
/// </summary>
internal sealed class ScryfallReferenceTools
{
    /// <summary>
    /// Names the printings section of the card details tool.
    /// </summary>
    internal const string IncludePrints = "prints";

    /// <summary>
    /// Names the rulings section of the card details tool.
    /// </summary>
    internal const string IncludeRulings = "rulings";

    /// <summary>
    /// Names the reference kind that reads sets.
    /// </summary>
    private const string KindSets = "sets";

    /// <summary>
    /// Names the reference kind that reads one catalog.
    /// </summary>
    private const string KindCatalog = "catalog";

    /// <summary>
    /// Names the reference kind that reads bulk data metadata.
    /// </summary>
    private const string KindBulk = "bulk";

    /// <summary>
    /// Names the snapshot kind that lists snapshot metadata.
    /// </summary>
    private const string KindList = "list";

    /// <summary>
    /// Names the snapshot kind that replays one snapshot.
    /// </summary>
    private const string KindGet = "get";

    /// <summary>
    /// Stores the unified provider and local evidence boundary.
    /// </summary>
    private readonly ScryfallService service;

    /// <summary>
    /// Creates the detail tools around one Scryfall service.
    /// </summary>
    internal ScryfallReferenceTools(ScryfallService service)
    {
        this.service = service;
    }

    /// <summary>
    /// Gets the allowed item values of the list parameters, published in each tool schema.
    /// </summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> ItemChoices { get; } =
        new Dictionary<string, IReadOnlyList<string>> { ["include"] = [IncludePrints, IncludeRulings] };

    /// <summary>
    /// Returns printings, rulings, or both for one card.
    /// </summary>
    [McpServerTool(
        Name = "scryfall_card_details",
        Title = "Get Scryfall Card Details",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description(
        "Scryfall: gets the printings, the rulings, or both for one card, found by one lookup of any kind " +
        "(Scryfall ID, Oracle ID, exact name, fuzzy name, or set and collector number), from installed card data " +
        "or an official request snapshot. Set include to [\"prints\"], [\"rulings\"], or both; the result has a " +
        "prints section and a rulings section, each present only when asked for. Example: " +
        "{\"lookup\":{\"kind\":\"exact-name\",\"value\":\"Sol Ring\"},\"include\":[\"rulings\"]}.")]
    internal Task<OperationResult<ScryfallCardDetailsResult>> GetCardDetailsAsync(
        [Description(
            "One card lookup: {kind, value} for scryfall-id, oracle-id, exact-name, or fuzzy-name, or " +
            "{kind: printing, setCode, collectorNumber}.")]
        ScryfallCardLookup lookup,
        [Description(
            "Sections to return: prints (printings), rulings, or both. At least one is required; " +
            "each may be listed once.")]
        IReadOnlyList<string> include,
        [Description("Cache policy: default, cache-only, or refresh."), FreshnessPolicyValues]
        string freshnessPolicy = "default",
        [Description("Include each lossless raw provider object in the requested sections.")]
        bool includeRaw = false,
        [Description("Only with prints: opaque cursor from a previous prints section.")]
        string? printsCursor = null,
        [Description("Only with rulings: opaque cursor from a previous rulings section.")]
        string? rulingsCursor = null,
        [Description("Items to return in each requested section, from 1 through 100, or through 25 with includeRaw.")]
        int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        return ScryfallToolExecution.RunAsync(() => ReadDetailsAsync(
            lookup,
            include,
            freshnessPolicy,
            includeRaw,
            printsCursor,
            rulingsCursor,
            pageSize,
            cancellationToken));
    }

    /// <summary>
    /// Returns official sets, one named catalog, or the bulk data metadata.
    /// </summary>
    [McpServerTool(
        Name = "scryfall_reference_get",
        Title = "Get Scryfall Reference Data",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description(
        "Scryfall: gets official reference data. kind sets lists all sets or one set (codeOrId); kind catalog " +
        "returns one named catalog (catalogName is required, for example card-names or creature-types); kind bulk " +
        "returns official metadata for the bulk datasets without downloading them. The result has the same kind " +
        "and holds the matching data. Example: {\"kind\":\"catalog\",\"catalogName\":\"creature-types\"}.")]
    internal Task<OperationResult<ScryfallReferenceResult>> GetReferenceAsync(
        [Description("What to read: sets, catalog, or bulk."), AllowedValues(KindSets, KindCatalog, KindBulk)]
        string kind,
        [Description("Only with kind sets: optional exact set code or set UUID; omit to list all sets.")]
        string? codeOrId = null,
        [Description("Only with kind catalog, and then required: exact official Scryfall catalog name.")]
        string? catalogName = null,
        [Description("Cache policy: default, cache-only, or refresh."), FreshnessPolicyValues]
        string freshnessPolicy = "default",
        [Description("Only with kind sets: include each lossless raw set object.")] bool includeRaw = false,
        [Description("Only with kind sets or catalog: opaque cursor for this immutable result.")]
        string? cursor = null,
        [Description("Only with kind sets or catalog: items to return, from 1 through 100; the default is 25.")]
        int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        return ScryfallToolExecution.RunAsync(() => ReadReferenceAsync(
            kind,
            codeOrId,
            catalogName,
            freshnessPolicy,
            includeRaw,
            cursor,
            pageSize,
            cancellationToken));
    }

    /// <summary>
    /// Lists saved request snapshots or replays one by ID.
    /// </summary>
    [McpServerTool(
        Name = "scryfall_request_snapshots_get",
        Title = "Get Saved Scryfall Requests",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Saved Scryfall requests: reads the local snapshots saved for every kind of Scryfall request (search, " +
        "card lookups, sets, catalogs, and more), with no network access. kind list pages snapshot metadata, with " +
        "optional operation (the request type) and time filters; kind get replays the ordered members of one " +
        "snapshot (snapshotId is required). The result has the same kind. Example: " +
        "{\"kind\":\"list\",\"operation\":\"search\",\"pageSize\":10}.")]
    internal Task<OperationResult<ScryfallSnapshotsResult>> GetRequestSnapshotsAsync(
        [Description("What to read: list (snapshot metadata) or get (one snapshot's members)."),
         AllowedValues(KindList, KindGet)]
        string kind,
        [Description("Only with kind get, and then required: immutable snapshot UUID from a Scryfall result.")]
        Guid? snapshotId = null,
        [Description("Only with kind list: optional exact snapshot operation filter."),
         AllowedValues(
             "search", "autocomplete", "card-get", "card-collection", "card-prints", "card-rulings",
             "sets", "set-get", "catalog", "bulk-metadata")]
        string? operation = null,
        [Description("Only with kind list: optional inclusive UTC lower bound for retrieval time.")]
        DateTimeOffset? retrievedAfterUtc = null,
        [Description("Only with kind list: optional exclusive UTC upper bound for retrieval time.")]
        DateTimeOffset? retrievedBeforeUtc = null,
        [Description("Only with kind get: include each exact raw snapshot member; limits pages to 25 when true.")]
        bool includeRaw = false,
        [Description("Opaque cursor from a previous result of the same kind.")] string? cursor = null,
        [Description("Items to return, from 1 through 100, or through 25 when includeRaw; the default is 25.")]
        int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        return ScryfallToolExecution.RunAsync(() => ReadSnapshotsAsync(
            kind,
            snapshotId,
            operation,
            retrievedAfterUtc,
            retrievedBeforeUtc,
            includeRaw,
            cursor,
            pageSize,
            cancellationToken));
    }

    /// <summary>
    /// Resolves the card, then reads the requested sections and returns the first failure when any step fails.
    /// </summary>
    private async Task<OperationResult<ScryfallCardDetailsResult>> ReadDetailsAsync(
        ScryfallCardLookup lookup,
        IReadOnlyList<string>? include,
        string freshnessPolicy,
        bool includeRaw,
        string? printsCursor,
        string? rulingsCursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        OperationInvalidInput? invalid = ValidateDetails(include, includeRaw, printsCursor, rulingsCursor, pageSize);
        if (invalid is not null)
        {
            return invalid;
        }

        IReadOnlyList<string> sections = include!;
        OperationResult<ScryfallCardIdentity> identityResult = await service.ResolveCardIdentityAsync(
            lookup, freshnessPolicy, cancellationToken).ConfigureAwait(false);
        if (identityResult is not OperationSuccess<ScryfallCardIdentity> identity)
        {
            return OperationFailure.Forward<ScryfallCardIdentity, ScryfallCardDetailsResult>(identityResult);
        }

        ScryfallPrintsResult? prints = null;
        if (sections.Contains(IncludePrints))
        {
            OperationResult<ScryfallPrintsResult> printsResult = await service.GetPrintsAsync(
                identity.Data.OracleId, freshnessPolicy, printsCursor, pageSize, includeRaw, cancellationToken)
                .ConfigureAwait(false);
            if (printsResult is not OperationSuccess<ScryfallPrintsResult> printsSuccess)
            {
                return OperationFailure.Forward<ScryfallPrintsResult, ScryfallCardDetailsResult>(printsResult);
            }

            prints = printsSuccess.Data;
        }

        ScryfallRulingsResult? rulings = null;
        if (sections.Contains(IncludeRulings))
        {
            OperationResult<ScryfallRulingsResult> rulingsResult = await service.GetRulingsAsync(
                identity.Data.OracleId,
                identity.Data.ScryfallId,
                freshnessPolicy,
                rulingsCursor,
                pageSize,
                includeRaw,
                cancellationToken).ConfigureAwait(false);
            if (rulingsResult is not OperationSuccess<ScryfallRulingsResult> rulingsSuccess)
            {
                return OperationFailure.Forward<ScryfallRulingsResult, ScryfallCardDetailsResult>(rulingsResult);
            }

            rulings = rulingsSuccess.Data;
        }

        return new OperationSuccess<ScryfallCardDetailsResult>(
            new ScryfallCardDetailsResult(identity.Data.OracleId, identity.Data.ScryfallId, prints, rulings));
    }

    /// <summary>
    /// Checks the include list, the page size and raw-output rule, and the cursors that apply to only one section, so a
    /// bad request fails before the card is resolved.
    /// </summary>
    private static OperationInvalidInput? ValidateDetails(
        IReadOnlyList<string>? include,
        bool includeRaw,
        string? printsCursor,
        string? rulingsCursor,
        int pageSize)
    {
        OperationInvalidInput? pageFailure = ScryfallCardEvidenceOperations.ValidatePageSize(pageSize, includeRaw);
        if (pageFailure is not null)
        {
            return pageFailure;
        }

        if (include is null || include.Count == 0)
        {
            return new OperationInvalidInput(
                "invalid-include",
                "include must list at least one of prints or rulings.",
                "include");
        }

        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (string section in include)
        {
            if (section is not (IncludePrints or IncludeRulings) || !seen.Add(section))
            {
                return new OperationInvalidInput(
                    "invalid-include",
                    "include may list each of prints and rulings once.",
                    "include");
            }
        }

        if (!seen.Contains(IncludeRulings) && rulingsCursor is not null)
        {
            return new OperationInvalidInput(
                KindFields.FieldNotAllowed,
                "rulingsCursor is allowed only when include has rulings.",
                "rulingsCursor");
        }

        return !seen.Contains(IncludePrints) && printsCursor is not null
            ? new OperationInvalidInput(
                KindFields.FieldNotAllowed,
                "printsCursor is allowed only when include has prints.",
                "printsCursor")
            : null;
    }

    /// <summary>
    /// Runs the one reference read that matches the kind.
    /// </summary>
    private async Task<OperationResult<ScryfallReferenceResult>> ReadReferenceAsync(
        string kind,
        string? codeOrId,
        string? catalogName,
        string freshnessPolicy,
        bool includeRaw,
        string? cursor,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        OperationInvalidInput? invalid = KindFields.CheckKind(kind, KindSets, KindCatalog, KindBulk) ??
            kind switch
            {
                KindSets => KindFields.RejectGiven(kind, ("catalogName", catalogName is not null)),
                KindCatalog => KindFields.RequireText(kind, "catalogName", catalogName) ??
                    KindFields.RejectGiven(kind, ("codeOrId", codeOrId is not null), ("includeRaw", includeRaw)),
                _ => KindFields.RejectGiven(
                    kind,
                    ("codeOrId", codeOrId is not null),
                    ("catalogName", catalogName is not null),
                    ("includeRaw", includeRaw),
                    ("cursor", cursor is not null),
                    ("pageSize", pageSize is not null)),
            };
        if (invalid is not null)
        {
            return invalid;
        }

        int size = pageSize ?? 25;
        switch (kind)
        {
            case KindSets:
                OperationResult<ScryfallSetsResult> sets = await service.GetSetsAsync(
                    codeOrId, freshnessPolicy, cursor, size, includeRaw, cancellationToken).ConfigureAwait(false);
                return sets is OperationSuccess<ScryfallSetsResult> setsSuccess
                    ? new OperationSuccess<ScryfallReferenceResult>(new ScryfallSetsReference(setsSuccess.Data))
                    : OperationFailure.Forward<ScryfallSetsResult, ScryfallReferenceResult>(sets);
            case KindCatalog:
                OperationResult<ScryfallCatalogResult> catalog = await service.GetCatalogAsync(
                    catalogName!, freshnessPolicy, cursor, size, cancellationToken).ConfigureAwait(false);
                return catalog is OperationSuccess<ScryfallCatalogResult> catalogSuccess
                    ? new OperationSuccess<ScryfallReferenceResult>(new ScryfallCatalogReference(catalogSuccess.Data))
                    : OperationFailure.Forward<ScryfallCatalogResult, ScryfallReferenceResult>(catalog);
            case KindBulk:
                OperationResult<ScryfallBulkMetadataResult> bulk = await service.GetBulkMetadataAsync(
                    freshnessPolicy, cancellationToken).ConfigureAwait(false);
                return bulk is OperationSuccess<ScryfallBulkMetadataResult> bulkSuccess
                    ? new OperationSuccess<ScryfallReferenceResult>(new ScryfallBulkReference(bulkSuccess.Data))
                    : OperationFailure.Forward<ScryfallBulkMetadataResult, ScryfallReferenceResult>(bulk);
            default:
                throw new UnreachableException("The kind was checked before this switch.");
        }
    }

    /// <summary>
    /// Runs the one snapshot read that matches the kind.
    /// </summary>
    private async Task<OperationResult<ScryfallSnapshotsResult>> ReadSnapshotsAsync(
        string kind,
        Guid? snapshotId,
        string? operation,
        DateTimeOffset? retrievedAfterUtc,
        DateTimeOffset? retrievedBeforeUtc,
        bool includeRaw,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        OperationInvalidInput? invalid = KindFields.CheckKind(kind, KindList, KindGet);
        if (invalid is null && kind == KindList)
        {
            invalid = KindFields.RejectGiven(kind, ("snapshotId", snapshotId is not null), ("includeRaw", includeRaw));
        }
        else
        {
            invalid ??= KindFields.Require(kind, "snapshotId", snapshotId is not null)
                ?? KindFields.RejectGiven(
                    kind,
                    ("operation", operation is not null),
                    ("retrievedAfterUtc", retrievedAfterUtc is not null),
                    ("retrievedBeforeUtc", retrievedBeforeUtc is not null));
        }

        if (invalid is not null)
        {
            return invalid;
        }

        if (kind == KindList)
        {
            OperationResult<ScryfallPage<ScryfallSnapshotSummary>> list = await service.ListSnapshotsAsync(
                operation, retrievedAfterUtc, retrievedBeforeUtc, cursor, pageSize, cancellationToken)
                .ConfigureAwait(false);
            return list is OperationSuccess<ScryfallPage<ScryfallSnapshotSummary>> listSuccess
                ? new OperationSuccess<ScryfallSnapshotsResult>(new ScryfallSnapshotListReference(listSuccess.Data))
                : OperationFailure.Forward<ScryfallPage<ScryfallSnapshotSummary>, ScryfallSnapshotsResult>(list);
        }

        OperationResult<ScryfallSnapshotPage> get = await service.GetSnapshotAsync(
            snapshotId!.Value, cursor, pageSize, includeRaw, cancellationToken).ConfigureAwait(false);
        return get is OperationSuccess<ScryfallSnapshotPage> getSuccess
            ? new OperationSuccess<ScryfallSnapshotsResult>(new ScryfallSnapshotGetReference(getSuccess.Data))
            : OperationFailure.Forward<ScryfallSnapshotPage, ScryfallSnapshotsResult>(get);
    }
}
