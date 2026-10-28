using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Results;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Scryfall;

/// <summary>
/// Exposes the Scryfall tools that return one chosen kind of detail: card details, reference data, and saved
/// search snapshots. Each tool has one required field that picks the job, and the result names the job it did.
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
    /// Returns printings, rulings, or both for one Oracle identity.
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
        "Scryfall: gets the printings, the rulings, or both for one card by Oracle ID, from installed card data " +
        "or an official request snapshot. Set include to [\"prints\"], [\"rulings\"], or both; the result has " +
        "a prints section and a rulings section, each present only when asked for. Example: " +
        "{\"oracleId\":\"00000000-0000-0000-0000-000000000000\",\"include\":[\"rulings\"]}.")]
    internal Task<OperationResult<ScryfallCardDetailsResult>> GetCardDetailsAsync(
        [Description("Exact Oracle UUID of the card.")] Guid oracleId,
        [Description(
            "Sections to return: prints (printings), rulings, or both. At least one is required; " +
            "each may be listed once.")]
        IReadOnlyList<string> include,
        [Description("Only with rulings: optional exact Scryfall printing UUID used by the provider rulings route.")]
        Guid? scryfallCardId = null,
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
            oracleId,
            include,
            scryfallCardId,
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
        [Description("What to read: sets, catalog, or bulk."), AllowedValues("sets", "catalog", "bulk")]
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
    /// Lists saved search snapshots or replays one by ID.
    /// </summary>
    [McpServerTool(
        Name = "scryfall_search_snapshots_get",
        Title = "Get Scryfall Search Snapshots",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Scryfall: reads saved exact-request snapshots without network access. kind list pages snapshot " +
        "metadata, with optional operation and time filters; kind get replays the ordered members of one " +
        "snapshot (snapshotId is required). The result has the same kind. Example: " +
        "{\"kind\":\"list\",\"operation\":\"search\",\"pageSize\":10}.")]
    internal Task<OperationResult<ScryfallSnapshotsResult>> GetSearchSnapshotsAsync(
        [Description("What to read: list (snapshot metadata) or get (one snapshot's members)."),
         AllowedValues("list", "get")]
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
    /// Reads the requested sections and returns the first failure when any section fails.
    /// </summary>
    private async Task<OperationResult<ScryfallCardDetailsResult>> ReadDetailsAsync(
        Guid oracleId,
        IReadOnlyList<string>? include,
        Guid? scryfallCardId,
        string freshnessPolicy,
        bool includeRaw,
        string? printsCursor,
        string? rulingsCursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        OperationInvalidInput? invalid = ValidateDetails(include, scryfallCardId, printsCursor, rulingsCursor);
        if (invalid is not null)
        {
            return invalid;
        }

        IReadOnlyList<string> sections = include!;
        ScryfallPrintsResult? prints = null;
        if (sections.Contains(IncludePrints))
        {
            OperationResult<ScryfallPrintsResult> printsResult = await service.GetPrintsAsync(
                oracleId, freshnessPolicy, printsCursor, pageSize, includeRaw, cancellationToken)
                .ConfigureAwait(false);
            if (printsResult is not OperationSuccess<ScryfallPrintsResult> printsSuccess)
            {
                return OperationResultForwarding.ForwardFailure<ScryfallPrintsResult, ScryfallCardDetailsResult>(
                    printsResult);
            }

            prints = printsSuccess.Data;
        }

        ScryfallRulingsResult? rulings = null;
        if (sections.Contains(IncludeRulings))
        {
            OperationResult<ScryfallRulingsResult> rulingsResult = await service.GetRulingsAsync(
                oracleId, scryfallCardId, freshnessPolicy, rulingsCursor, pageSize, includeRaw, cancellationToken)
                .ConfigureAwait(false);
            if (rulingsResult is not OperationSuccess<ScryfallRulingsResult> rulingsSuccess)
            {
                return OperationResultForwarding.ForwardFailure<ScryfallRulingsResult, ScryfallCardDetailsResult>(
                    rulingsResult);
            }

            rulings = rulingsSuccess.Data;
        }

        return new OperationSuccess<ScryfallCardDetailsResult>(
            new ScryfallCardDetailsResult(oracleId, prints, rulings));
    }

    /// <summary>
    /// Checks the include list and the fields that apply to only one section.
    /// </summary>
    private static OperationInvalidInput? ValidateDetails(
        IReadOnlyList<string>? include,
        Guid? scryfallCardId,
        string? printsCursor,
        string? rulingsCursor)
    {
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

        if (!seen.Contains(IncludeRulings) && (scryfallCardId is not null || rulingsCursor is not null))
        {
            return new OperationInvalidInput(
                "field-not-allowed",
                "scryfallCardId and rulingsCursor are allowed only when include has rulings.",
                scryfallCardId is not null ? "scryfallCardId" : "rulingsCursor");
        }

        return !seen.Contains(IncludePrints) && printsCursor is not null
            ? new OperationInvalidInput(
                "field-not-allowed",
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
        OperationInvalidInput? invalid = ValidateReference(kind, codeOrId, catalogName, includeRaw, cursor, pageSize);
        if (invalid is not null)
        {
            return invalid;
        }

        int size = pageSize ?? 25;
        switch (kind)
        {
            case "sets":
                OperationResult<ScryfallSetsResult> sets = await service.GetSetsAsync(
                    codeOrId, freshnessPolicy, cursor, size, includeRaw, cancellationToken).ConfigureAwait(false);
                return sets is OperationSuccess<ScryfallSetsResult> setsSuccess
                    ? new OperationSuccess<ScryfallReferenceResult>(new ScryfallSetsReference(setsSuccess.Data))
                    : OperationResultForwarding.ForwardFailure<ScryfallSetsResult, ScryfallReferenceResult>(sets);
            case "catalog":
                OperationResult<ScryfallCatalogResult> catalog = await service.GetCatalogAsync(
                    catalogName!, freshnessPolicy, cursor, size, cancellationToken).ConfigureAwait(false);
                return catalog is OperationSuccess<ScryfallCatalogResult> catalogSuccess
                    ? new OperationSuccess<ScryfallReferenceResult>(new ScryfallCatalogReference(catalogSuccess.Data))
                    : OperationResultForwarding.ForwardFailure<ScryfallCatalogResult, ScryfallReferenceResult>(
                        catalog);
            default:
                OperationResult<ScryfallBulkMetadataResult> bulk = await service.GetBulkMetadataAsync(
                    freshnessPolicy, cancellationToken).ConfigureAwait(false);
                return bulk is OperationSuccess<ScryfallBulkMetadataResult> bulkSuccess
                    ? new OperationSuccess<ScryfallReferenceResult>(new ScryfallBulkReference(bulkSuccess.Data))
                    : OperationResultForwarding.ForwardFailure<ScryfallBulkMetadataResult, ScryfallReferenceResult>(
                        bulk);
        }
    }

    /// <summary>
    /// Checks that the fields given belong to the chosen kind.
    /// </summary>
    private static OperationInvalidInput? ValidateReference(
        string kind,
        string? codeOrId,
        string? catalogName,
        bool includeRaw,
        string? cursor,
        int? pageSize)
    {
        if (kind is not ("sets" or "catalog" or "bulk"))
        {
            return new OperationInvalidInput("invalid-kind", "kind must be sets, catalog, or bulk.", "kind");
        }

        if (kind == "catalog" && string.IsNullOrWhiteSpace(catalogName))
        {
            return new OperationInvalidInput(
                "missing-field", "catalogName is required when kind is catalog.", "catalogName");
        }

        string? misplaced = kind switch
        {
            "sets" => catalogName is not null ? "catalogName" : null,
            "catalog" => codeOrId is not null ? "codeOrId" : includeRaw ? "includeRaw" : null,
            _ => FindFirstGiven(
                ("codeOrId", codeOrId is not null),
                ("catalogName", catalogName is not null),
                ("includeRaw", includeRaw),
                ("cursor", cursor is not null),
                ("pageSize", pageSize is not null)),
        };
        return misplaced is null
            ? null
            : new OperationInvalidInput(
                "field-not-allowed",
                $"{misplaced} is not allowed when kind is {kind}.",
                misplaced);
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
        OperationInvalidInput? invalid = ValidateSnapshots(
            kind, snapshotId, operation, retrievedAfterUtc, retrievedBeforeUtc, includeRaw);
        if (invalid is not null)
        {
            return invalid;
        }

        if (kind == "list")
        {
            OperationResult<ScryfallPage<ScryfallSnapshotSummary>> list = await service.ListSnapshotsAsync(
                operation, retrievedAfterUtc, retrievedBeforeUtc, cursor, pageSize, cancellationToken)
                .ConfigureAwait(false);
            return list is OperationSuccess<ScryfallPage<ScryfallSnapshotSummary>> listSuccess
                ? new OperationSuccess<ScryfallSnapshotsResult>(new ScryfallSnapshotListReference(listSuccess.Data))
                : OperationResultForwarding.ForwardFailure<
                    ScryfallPage<ScryfallSnapshotSummary>, ScryfallSnapshotsResult>(list);
        }

        OperationResult<ScryfallSnapshotPage> get = await service.GetSnapshotAsync(
            snapshotId!.Value, cursor, pageSize, includeRaw, cancellationToken).ConfigureAwait(false);
        return get is OperationSuccess<ScryfallSnapshotPage> getSuccess
            ? new OperationSuccess<ScryfallSnapshotsResult>(new ScryfallSnapshotGetReference(getSuccess.Data))
            : OperationResultForwarding.ForwardFailure<ScryfallSnapshotPage, ScryfallSnapshotsResult>(get);
    }

    /// <summary>
    /// Checks that the fields given belong to the chosen kind.
    /// </summary>
    private static OperationInvalidInput? ValidateSnapshots(
        string kind,
        Guid? snapshotId,
        string? operation,
        DateTimeOffset? retrievedAfterUtc,
        DateTimeOffset? retrievedBeforeUtc,
        bool includeRaw)
    {
        if (kind is not ("list" or "get"))
        {
            return new OperationInvalidInput("invalid-kind", "kind must be list or get.", "kind");
        }

        if (kind == "get" && snapshotId is null)
        {
            return new OperationInvalidInput(
                "missing-field", "snapshotId is required when kind is get.", "snapshotId");
        }

        string? misplaced = kind == "list"
            ? FindFirstGiven(("snapshotId", snapshotId is not null), ("includeRaw", includeRaw))
            : FindFirstGiven(
                ("operation", operation is not null),
                ("retrievedAfterUtc", retrievedAfterUtc is not null),
                ("retrievedBeforeUtc", retrievedBeforeUtc is not null));
        return misplaced is null
            ? null
            : new OperationInvalidInput(
                "field-not-allowed",
                $"{misplaced} is not allowed when kind is {kind}.",
                misplaced);
    }

    /// <summary>
    /// Returns the name of the first field that was given, or null when none was.
    /// </summary>
    private static string? FindFirstGiven(params (string Name, bool Given)[] fields)
    {
        foreach ((string name, bool given) in fields)
        {
            if (given)
            {
                return name;
            }
        }

        return null;
    }
}
