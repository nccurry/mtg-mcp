using MtgMcp.App.Hosting;
using MtgMcp.App.Scryfall;
using MtgMcp.Core.Results;
using MtgMcp.Scryfall;

namespace MtgMcp.App.CardLists;

/// <summary>
/// Runs <c>card_lists_compare</c> and <c>card_list_query</c>: reads the sources, applies the pure set or query
/// function, and writes a page that stays under the result size limit. Nothing is stored between pages.
/// </summary>
internal sealed class CardListCoordinator
{
    /// <summary>
    /// Gives the number of items in a page when the caller names none.
    /// </summary>
    internal const int DefaultPageSize = 25;

    /// <summary>
    /// Gives the most items a caller can ask for in one page.
    /// </summary>
    internal const int MaximumPageSize = 100;

    /// <summary>
    /// Gives the most unmatched entries one result lists.
    /// </summary>
    internal const int MaximumUnmatchedListed = 50;

    /// <summary>
    /// Gives the bytes the groups of a query may take when it also returns rows.
    /// </summary>
    private const int GroupBudgetWithRows = 15_000;

    /// <summary>
    /// Says how to get a smaller result when groups were cut.
    /// </summary>
    private const string GroupNarrowHint =
        "Some groups were left out to stay under the result size limit. Group by a field with fewer values, add a " +
        "where filter, or ask for fewer metrics.";

    /// <summary>
    /// Reads list sources.
    /// </summary>
    private readonly CardListResolver resolver;

    /// <summary>
    /// Writes cards in a response format.
    /// </summary>
    private readonly ScryfallService scryfall;

    /// <summary>
    /// Creates the coordinator around the shared resolver and the Scryfall service.
    /// </summary>
    internal CardListCoordinator(CardListResolver resolver, ScryfallService scryfall)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(scryfall);
        this.resolver = resolver;
        this.scryfall = scryfall;
    }

    /// <summary>
    /// Applies one set operation to two card lists and returns one page of the result.
    /// </summary>
    internal async Task<OperationResult<CardListsCompareResult>> CompareAsync(
        CardListSourceInput left,
        CardListSourceInput right,
        string operation,
        string matchBy,
        string responseFormat,
        IReadOnlyList<string>? fields,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (!ScryfallSetNames.TryParseOperation(operation, out ScryfallSetOperation setOperation))
        {
            return new OperationInvalidInput(
                "invalid-operation",
                $"operation must be one of: {string.Join(", ", ScryfallSetNames.Operations)}.",
                "operation");
        }

        if (!ScryfallSetNames.TryParseMatchBy(matchBy, out ScryfallMatchBy match))
        {
            return new OperationInvalidInput(
                "invalid-match-by",
                $"matchBy must be one of: {string.Join(", ", ScryfallSetNames.MatchModes)}.",
                "matchBy");
        }

        OperationResult<ScryfallCardFormat> format = ScryfallCardFormat.Parse(responseFormat, fields);
        if (format is not OperationSuccess<ScryfallCardFormat> parsed)
        {
            return OperationFailure.Forward<ScryfallCardFormat, CardListsCompareResult>(format);
        }

        int maximum = parsed.Data.IncludeRaw ? 25 : MaximumPageSize;
        if (pageSize < 1 || pageSize > maximum)
        {
            return new OperationInvalidInput(
                "invalid-page-size",
                $"pageSize must be from 1 through {maximum}.",
                "pageSize");
        }

        int idCount = 0;
        OperationInvalidInput? invalid = CardListResolver.Validate(left, "left", ref idCount) ??
            CardListResolver.Validate(right, "right", ref idCount);
        if (invalid is not null)
        {
            return invalid;
        }

        string fingerprint = CardListCursors.Fingerprint(
            (left, typeof(CardListSourceInput)),
            (right, typeof(CardListSourceInput)),
            (setOperation.ToString(), typeof(string)),
            (match.ToString(), typeof(string)),
            (parsed.Data.Name, typeof(string)),
            (fields, typeof(IReadOnlyList<string>)));
        CardListReadOptions options = new(
            parsed.Data.IncludeRaw,
            IncludeFacts: false,
            new CardListBudget(ScryfallListQuery.MaximumEntries));
        OperationResult<ResolvedCardList> leftList = await resolver.ResolveAsync(left, "left", options,
            cancellationToken)
            .ConfigureAwait(false);
        if (leftList is not OperationSuccess<ResolvedCardList> leftResolved)
        {
            return OperationFailure.Forward<ResolvedCardList, CardListsCompareResult>(leftList);
        }

        OperationResult<ResolvedCardList> rightList = await resolver.ResolveAsync(right, "right", options,
            cancellationToken)
            .ConfigureAwait(false);
        if (rightList is not OperationSuccess<ResolvedCardList> rightResolved)
        {
            return OperationFailure.Forward<ResolvedCardList, CardListsCompareResult>(rightList);
        }

        ResolvedCardList combined = CardListResolver.Combine(
            leftResolved.Data,
            rightResolved.Data,
            setOperation,
            match);
        OperationResult<int> read = CardListCursors.Read(cursor, fingerprint, combined.Sources);
        if (read is not OperationSuccess<int> position)
        {
            return OperationFailure.Forward<int, CardListsCompareResult>(read);
        }

        if (position.Data > combined.Entries.Count)
        {
            return PageCursor.Invalid();
        }

        List<ScryfallCard> window = [.. combined.Entries
            .Skip(position.Data)
            .Take(pageSize)
            .Select(entry => entry.Card.Card)];
        OperationResult<ScryfallFormattedList> formatted = await ScryfallToolExecution.RunAsync(() =>
            scryfall.FormatCardsAsync(window, responseFormat, fields, cancellationToken)).ConfigureAwait(false);
        if (formatted is not OperationSuccess<ScryfallFormattedList> shaped)
        {
            return OperationFailure.Forward<ScryfallFormattedList, CardListsCompareResult>(formatted);
        }

        CardListCounts counts = new(
            DistinctCards(leftResolved.Data, match),
            DistinctCards(rightResolved.Data, match),
            combined.Entries.Count,
            combined.Unmatched.Count);
        List<CardListUnmatched> unmatched = [.. combined.Unmatched.Take(MaximumUnmatchedListed)];
        CardListsCompareResult header = new(
            counts,
            new ScryfallPage<ScryfallCardView>([], combined.Entries.Count, null),
            unmatched,
            combined.Sources,
            fingerprint)
        {
            ResponseFormat = shaped.Data.ResponseFormat,
            Limits = shaped.Data.Limits,
            UnmatchedOmitted = combined.Unmatched.Count > unmatched.Count
                ? combined.Unmatched.Count - unmatched.Count
                : null,
        };
        (int keep, int averageBytes) = CountFitting([.. shaped.Data.Views.Select(view => (object)view)], header);
        int next = position.Data + keep;
        string? nextCursor = next < combined.Entries.Count
            ? CardListCursors.Encode(fingerprint, next, combined.Sources)
            : null;
        bool truncated = keep < shaped.Data.Views.Count;
        return new OperationSuccess<CardListsCompareResult>(header with
        {
            Page = new ScryfallPage<ScryfallCardView>([.. shaped.Data.Views.Take(keep)], combined.Entries.Count,
                nextCursor),
            Truncated = truncated ? true : null,
            Narrow = truncated
                ? ScryfallCutPageHint.Build(
                    keep,
                    shaped.Data.Views.Count,
                    position.Data,
                    combined.Entries.Count,
                    averageBytes)
                : null,
            ReturnedCount = keep,
            RequestedCount = shaped.Data.Views.Count,
            CutReason = truncated ? ScryfallCutPageHint.ResultSizeLimit : null,
        });
    }

    /// <summary>
    /// Filters, groups, and measures one card list, and returns one page of its rows when rows were asked for.
    /// </summary>
    internal async Task<OperationResult<CardListQueryResult>> QueryAsync(
        CardListSourceInput source,
        IReadOnlyList<CardListFilterInput>? where,
        IReadOnlyList<string>? groupBy,
        IReadOnlyList<string>? metrics,
        CardListRowsInput? rows,
        string? cursor,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        int rowSize = pageSize ?? DefaultPageSize;
        if (rowSize < 1 || rowSize > MaximumPageSize)
        {
            return new OperationInvalidInput(
                "invalid-page-size",
                $"pageSize must be from 1 through {MaximumPageSize}.",
                "pageSize");
        }

        if (rows is null && (cursor is not null || pageSize is not null))
        {
            string name = cursor is not null ? "cursor" : "pageSize";
            return new OperationInvalidInput(
                "cursor-requires-rows",
                $"{name} pages the rows, so it needs the rows object of the first request.",
                name);
        }

        int idCount = 0;
        OperationInvalidInput? invalid = CardListResolver.Validate(source, "source", ref idCount);
        if (invalid is not null)
        {
            return invalid;
        }

        string fingerprint = CardListCursors.Fingerprint(
            (source, typeof(CardListSourceInput)),
            (where, typeof(IReadOnlyList<CardListFilterInput>)),
            (groupBy, typeof(IReadOnlyList<string>)),
            (metrics, typeof(IReadOnlyList<string>)),
            (rows?.Fields, typeof(IReadOnlyList<string>)),
            (rows?.SortBy, typeof(IReadOnlyList<CardListSortInput>)));
        OperationResult<ResolvedCardList> resolved = await resolver.ResolveAsync(
            source,
            "source",
            new CardListReadOptions(
                IncludeRaw: false,
                NeedsFacts(where, groupBy, metrics, rows),
                new CardListBudget(ScryfallListQuery.MaximumEntries)),
            cancellationToken).ConfigureAwait(false);
        if (resolved is not OperationSuccess<ResolvedCardList> list)
        {
            return OperationFailure.Forward<ResolvedCardList, CardListQueryResult>(resolved);
        }

        OperationResult<int> read = CardListCursors.Read(cursor, fingerprint, list.Data.Sources);
        if (read is not OperationSuccess<int> position)
        {
            return OperationFailure.Forward<int, CardListQueryResult>(read);
        }

        if (list.Data.HasDeckFields && !IsLocalDeck(list.Data.Sources) &&
            FindEntryIdPath(where, groupBy, rows) is { } entryIdPath)
        {
            return new OperationInvalidInput(
                "invalid-field",
                "Unknown or unavailable field 'entryId'. Archidekt sources have no local entry IDs; " +
                "use a local deck source.",
                entryIdPath);
        }

        ScryfallListQueryRequest request = new(
            [.. (where ?? []).Select(filter => new ScryfallListCondition(filter.Field, filter.Op, filter.Value))],
            groupBy ?? [],
            metrics ?? [],
            rows is null
                ? null
                : new ScryfallListRowOptions(
                    rows.Fields ?? [],
                    [.. (rows.SortBy ?? []).Select(sort => new ScryfallListSort(sort.Field, sort.Direction))],
                    position.Data,
                    rowSize),
            list.Data.HasDeckFields);
        List<ScryfallListEntry> entries =
        [
            .. list.Data.Entries.Select(entry => new ScryfallListEntry(
                entry.Card.Card,
                entry.Card.Tags,
                entry.Card.CheapestPriceUsd,
                entry.Quantity,
                entry.Zone,
                entry.Categories,
                entry.EntryId)),
        ];
        OperationResult<ScryfallListQueryResult> run = ScryfallListQuery.Run(entries, request);
        if (run is OperationInvalidInput { Path: null } unplaced)
        {
            return new OperationInvalidInput(unplaced.ReasonCode, unplaced.Message, "source");
        }

        if (run is not OperationSuccess<ScryfallListQueryResult> answer)
        {
            return OperationFailure.Forward<ScryfallListQueryResult, CardListQueryResult>(run);
        }

        // A cursor past the end of the rows is refused here exactly as card_lists_compare refuses it.
        if (answer.Data.Rows is { } rowsRead && position.Data > rowsRead.TotalRows)
        {
            return PageCursor.Invalid();
        }

        return new OperationSuccess<CardListQueryResult>(Build(list.Data, answer.Data, fingerprint, position.Data));
    }

    /// <summary>
    /// Builds the query result and cuts groups and rows to the size limit.
    /// </summary>
    private static CardListQueryResult Build(
        ResolvedCardList list,
        ScryfallListQueryResult answer,
        string fingerprint,
        int position)
    {
        List<CardListUnmatched> unmatched = [.. list.Unmatched.Take(MaximumUnmatchedListed)];
        List<string> limits = [.. answer.Limits];
        CardListQueryResult header = new(
            list.Sources,
            answer.Matched,
            [],
            answer.MultiValued,
            answer.MissingValues,
            unmatched,
            null,
            limits,
            fingerprint)
        {
            UnmatchedOmitted = list.Unmatched.Count > unmatched.Count ? list.Unmatched.Count - unmatched.Count : null,
        };
        int headerBytes = ResultSize.Measure(header);
        // Without rows the groups may use everything the header leaves; with rows they get a fixed share, so the rows
        // keep room too. CountFitting takes the bytes that are not for the groups.
        int bytesNotForGroups = answer.Rows is null
            ? headerBytes
            : ResultSizeLimit.MaximumBytes - ResultSizeLimit.EnvelopeReserveBytes - GroupBudgetWithRows;
        List<int> groupSizes = [.. answer.Groups.Select(group => ResultSize.Measure(group) + 1)];
        int keptGroups = ResultSizeLimit.CountFitting(groupSizes, bytesNotForGroups);
        List<ScryfallOrderedObject> groups = [.. answer.Groups.Take(keptGroups)];
        bool truncated = keptGroups < answer.Groups.Count;
        CardListRowsPage? rows = null;
        string? nextCursor = null;
        if (answer.Rows is { } read)
        {
            int fixedBytes = headerBytes + groupSizes.Take(keptGroups).Sum();
            List<int> rowSizes = [.. read.Items.Select(item => ResultSize.Measure(item) + 1)];
            int keptRows = ResultSizeLimit.CountFitting(rowSizes, fixedBytes);
            truncated |= keptRows < read.Items.Count;
            int next = position + keptRows;
            nextCursor = next < read.TotalRows ? CardListCursors.Encode(fingerprint, next, list.Sources) : null;
            rows = new CardListRowsPage([.. read.Items.Take(keptRows)], read.TotalRows);
        }

        return header with
        {
            Groups = groups,
            Rows = rows,
            NextCursor = nextCursor,
            Truncated = truncated ? true : null,
            Narrow = truncated
                ? nextCursor is not null && keptGroups == answer.Groups.Count
                    ? ScryfallFormattedResponse.NarrowHint
                    : GroupNarrowHint
                : null,
        };
    }

    /// <summary>
    /// Finds the path of the first filter, group, row field, or sort key that names entryId, or null when none does.
    /// </summary>
    private static string? FindEntryIdPath(
        IReadOnlyList<CardListFilterInput>? where,
        IReadOnlyList<string>? groupBy,
        CardListRowsInput? rows)
    {
        for (int index = 0; index < (where?.Count ?? 0); index++)
        {
            if (where![index].Field == "entryId")
            {
                return $"where[{index}].field";
            }
        }

        for (int index = 0; index < (groupBy?.Count ?? 0); index++)
        {
            if (groupBy![index] == "entryId")
            {
                return $"groupBy[{index}]";
            }
        }

        for (int index = 0; index < (rows?.Fields?.Count ?? 0); index++)
        {
            if (rows!.Fields![index] == "entryId")
            {
                return $"rows.fields[{index}]";
            }
        }

        for (int index = 0; index < (rows?.SortBy?.Count ?? 0); index++)
        {
            if (rows!.SortBy![index].Field == "entryId")
            {
                return $"rows.sortBy[{index}].field";
            }
        }

        return null;
    }

    /// <summary>
    /// Reports whether the sources are local decks or deck backups, the only lists whose entry IDs are local entry IDs.
    /// </summary>
    private static bool IsLocalDeck(IReadOnlyList<CardListSourceInfo> sources)
    {
        return sources.All(source => source.Kind is "deck" or "deck-backup");
    }

    /// <summary>
    /// Reports whether a query uses a field that comes from the installed tag and price tables, so the tag and price
    /// lookups run only when the answer needs them.
    /// </summary>
    internal static bool NeedsFacts(
        IReadOnlyList<CardListFilterInput>? where,
        IReadOnlyList<string>? groupBy,
        IReadOnlyList<string>? metrics,
        CardListRowsInput? rows)
    {
        List<string?> names =
        [
            .. (where ?? []).Select(filter => filter.Field),
            .. groupBy ?? [],
            .. (metrics ?? []).Select(metric => metric[(metric.IndexOf(':', StringComparison.Ordinal) + 1)..]),
            .. rows?.Fields ?? [],
            .. (rows?.SortBy ?? []).Select(sort => sort.Field),
        ];
        return names.Any(name => name is "tags" or "cheapestPriceUsd");
    }

    /// <summary>
    /// Counts how many leading cards of a page fit under the size limit next to the rest of the result, and the
    /// average size in bytes of the cards that fit.
    /// </summary>
    private static (int Keep, int AverageBytes) CountFitting(IReadOnlyList<object> views, CardListsCompareResult header)
    {
        List<int> sizes = [.. views.Select(view => ResultSize.Measure(view) + 1)];
        int keep = ResultSizeLimit.CountFitting(sizes, ResultSize.Measure(header));
        int average = keep == 0 ? 0 : (int)Math.Ceiling(sizes.Take(keep).Sum() / (double)keep);
        return (keep, average);
    }

    /// <summary>
    /// Counts the distinct cards of a resolved list under a match mode.
    /// </summary>
    private static int DistinctCards(ResolvedCardList list, ScryfallMatchBy matchBy)
    {
        return list.Entries
            .Select(entry => ScryfallCardSets.KeyOf(entry.Card.Card, matchBy))
            .Distinct(StringComparer.Ordinal)
            .Count();
    }
}
