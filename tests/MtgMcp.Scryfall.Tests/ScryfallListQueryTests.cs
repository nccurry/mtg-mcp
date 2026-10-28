using System.Text.Json;
using MtgMcp.Core.Results;

namespace MtgMcp.Scryfall.Tests;

/// <summary>
/// Verifies the pure list query function: filters, groups, metrics, rows, and stable output (QUERY-001 to QUERY-007,
/// cases Q01 to Q08 and Q11 to Q14).
/// </summary>
public sealed class ScryfallListQueryTests
{
    /// <summary>
    /// Verifies a deck grouped by mana value gives one group per value and totals equal the deck size (case Q01).
    /// </summary>
    [Fact]
    public void Run_GroupByManaValue_GivesOneGroupPerValueWithTheDeckTotal()
    {
        List<ScryfallListEntry> deck =
        [
            ListTestCards.Entry(ListTestCards.Make("One", manaValue: 1m), quantity: 3),
            ListTestCards.Entry(ListTestCards.Make("Two", manaValue: 2m), quantity: 2),
            ListTestCards.Entry(ListTestCards.Make("Two b", manaValue: 2m)),
            ListTestCards.Entry(ListTestCards.Make("Land", typeLine: "Land", manaCost: null, manaValue: 0m), 4),
        ];

        ScryfallListQueryResult result = Success(
            deck,
            new ScryfallListQueryRequest([], ["manaValue"], ["quantity", "cards"], null, HasDeckFields: true));

        Assert.Equal(10, result.Matched.Quantity);
        Assert.Equal(
            [(0L, 4L, 1), (1L, 3L, 1), (2L, 3L, 2)],
            result.Groups.Select(group => (
                (long)KeyOf(group, "manaValue")!,
                (long)group.Get("quantity")!,
                (int)group.Get("cards")!)));
        Assert.False(result.MultiValued);
    }

    /// <summary>
    /// Verifies a two-color card counts in both color groups and the result says so (case Q02).
    /// </summary>
    [Fact]
    public void Run_GroupByColors_CountsAMulticolorCardInEveryColorAndFlagsIt()
    {
        List<ScryfallListEntry> deck =
        [
            ListTestCards.Entry(ListTestCards.Make("Gold", colors: ["B", "G"])),
            ListTestCards.Entry(ListTestCards.Make("Green", colors: ["G"])),
            ListTestCards.Entry(ListTestCards.Make("Gray", colors: [])),
        ];

        ScryfallListQueryResult result = Success(deck, Request(groupBy: ["colors"]));

        Assert.True(result.MultiValued);
        Assert.Equal(
            [("B", 1L), ("G", 2L), (null, 1L)],
            result.Groups.Select(group => ((string?)KeyOf(group, "colors"), (long)group.Get("quantity")!)));
    }

    /// <summary>
    /// Verifies a card with no tags falls in the none group, and the none group comes last (case Q03).
    /// </summary>
    [Fact]
    public void Run_GroupByTags_PutsAnUntaggedCardInTheNoneGroup()
    {
        List<ScryfallListEntry> deck =
        [
            ListTestCards.Entry(ListTestCards.Make("Ramp"), tags: ["ramp"]),
            ListTestCards.Entry(ListTestCards.Make("Plain")),
        ];

        ScryfallListQueryResult result = Success(deck, Request(groupBy: ["tags"]));

        Assert.Equal(["ramp", null], result.Groups.Select(group => (string?)KeyOf(group, "tags")));
    }

    /// <summary>
    /// Verifies pips count colors, hybrid, and Phyrexian symbols as the design states, ignore generic and X, and
    /// multiply by copies (case Q04).
    /// </summary>
    [Fact]
    public void Run_Pips_FollowTheHybridAndPhyrexianRules()
    {
        List<ScryfallListEntry> deck =
        [
            ListTestCards.Entry(ListTestCards.Make("A", manaCost: "{1}{G}{G}")),
            ListTestCards.Entry(ListTestCards.Make("B", manaCost: "{W/U}")),
            ListTestCards.Entry(ListTestCards.Make("C", manaCost: "{G/P}")),
            ListTestCards.Entry(ListTestCards.Make("D", manaCost: "{X}{R}")),
        ];

        ScryfallListQueryResult result = Success(deck, Request(metrics: ["pips"]));

        ScryfallOrderedObject pips = (ScryfallOrderedObject)Assert.Single(result.Groups).Get("pips")!;
        Assert.Equal(["W", "U", "R", "G", "hybrid", "phyrexian"], pips.Fields.Select(field => field.Key));
        Assert.Equal([1L, 1L, 1L, 3L, 1L, 1L], pips.Fields.Select(field => (long)field.Value!));
        Assert.Equal(1L, pips.Get("hybrid"));
        Assert.Equal(1L, pips.Get("phyrexian"));
        Assert.Equal(3L, pips.Get("G"));
        Assert.Contains(result.Limits, limit => limit.Contains("Pips", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies copies multiply pip counts and a hybrid with a generic part counts its color once.
    /// </summary>
    [Fact]
    public void Run_Pips_MultiplyByCopiesAndCountGenericHybridColorOnce()
    {
        List<ScryfallListEntry> deck =
        [
            ListTestCards.Entry(ListTestCards.Make("A", manaCost: "{2/W}{W/B/P}"), quantity: 2),
        ];

        ScryfallListQueryResult result = Success(deck, Request(metrics: ["pips"]));

        ScryfallOrderedObject pips = (ScryfallOrderedObject)Assert.Single(result.Groups).Get("pips")!;
        Assert.Equal(4L, pips.Get("W"));
        Assert.Equal(2L, pips.Get("B"));
        Assert.Equal(4L, pips.Get("hybrid"));
        Assert.Equal(2L, pips.Get("phyrexian"));
    }

    /// <summary>
    /// Verifies sources count colors from produced mana, any-color lands, and lands with no produced mana (case Q05).
    /// </summary>
    [Fact]
    public void Run_Sources_CountColorsAnyColorAndNoProducedMana()
    {
        ScryfallCard forest = ListTestCards.Make("Forest", "Basic Land — Forest", null, 0m, producedMana: ["G"]);
        ScryfallCard dual = ListTestCards.Make("Dual", "Land", null, 0m, producedMana: ["G", "U"]);
        ScryfallCard five = ListTestCards.Make("Five", "Land", null, 0m, producedMana: ["W", "U", "B", "R", "G"]);
        ScryfallCard fetch = ListTestCards.Make("Fetch", "Land", null, 0m);
        ScryfallCard rock = ListTestCards.Make("Rock", "Artifact", producedMana: ["G"]);
        ScryfallCard spell = ListTestCards.Make("Plain spell");
        List<ScryfallListEntry> deck = [.. new[] { forest, dual, five, fetch, rock, spell }.Select(Wrap)];

        ScryfallListQueryResult result = Success(deck, Request(metrics: ["sources"]));

        ScryfallOrderedObject sources = (ScryfallOrderedObject)Assert.Single(result.Groups).Get("sources")!;
        Assert.Equal(4L, sources.Get("G"));
        Assert.Equal(2L, sources.Get("U"));
        Assert.Equal(1L, sources.Get("anyColor"));
        Assert.Equal(1L, sources.Get("noProducedMana"));
        Assert.Contains(result.Limits, limit => limit.Contains("tapped", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies a card with no price is left out of a sum and counted in missing values (case Q06), and sums are
    /// exact decimal strings multiplied by copies.
    /// </summary>
    [Fact]
    public void Run_SumPrice_LeavesOutCardsWithoutAPriceAndReportsThem()
    {
        List<ScryfallListEntry> deck =
        [
            ListTestCards.Entry(ListTestCards.Make("Cheap", usd: "0.10"), quantity: 3),
            ListTestCards.Entry(ListTestCards.Make("Dear", usd: "14.20")),
            ListTestCards.Entry(ListTestCards.Make("Unpriced")),
        ];

        ScryfallListQueryResult result = Success(
            deck,
            Request(metrics: ["sum:priceUsd", "avg:priceUsd", "min:priceUsd", "max:priceUsd"]));

        ScryfallOrderedObject group = Assert.Single(result.Groups);
        Assert.Equal("14.50", group.Get("sum:priceUsd"));
        Assert.Equal("3.625", group.Get("avg:priceUsd"));
        Assert.Equal("0.10", group.Get("min:priceUsd"));
        Assert.Equal("14.20", group.Get("max:priceUsd"));
        ScryfallMissingValues missing = Assert.Single(result.MissingValues);
        Assert.Equal(("priceUsd", 1), (missing.Field, missing.Cards));
        Assert.Contains(result.Limits, limit => limit.Contains("rounded", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies an average, minimum, and maximum over cards that all lack a price are null.
    /// </summary>
    [Fact]
    public void Run_AverageOverCardsThatAllLackAPrice_IsNull()
    {
        ScryfallListQueryResult result = Success(
            [ListTestCards.Entry(ListTestCards.Make("Unpriced"))],
            Request(metrics: ["avg:priceUsd", "min:priceUsd", "max:priceUsd"]));

        ScryfallOrderedObject group = Assert.Single(result.Groups);
        Assert.Null(group.Get("avg:priceUsd"));
        Assert.Null(group.Get("min:priceUsd"));
        Assert.Null(group.Get("max:priceUsd"));
    }

    /// <summary>
    /// Verifies every where operator keeps only the matching cards (case Q07).
    /// </summary>
    [Theory]
    [InlineData("manaValue", "eq", "2", "Two")]
    [InlineData("manaValue", "ne", "2", "One,Three")]
    [InlineData("manaValue", "lt", "2", "One")]
    [InlineData("manaValue", "lte", "2", "One,Two")]
    [InlineData("manaValue", "gt", "2", "Three")]
    [InlineData("manaValue", "gte", "2", "Three,Two")]
    [InlineData("manaValue", "in", "[1,3]", "One,Three")]
    [InlineData("name", "eq", "\"two\"", "Two")]
    [InlineData("name", "in", "[\"ONE\",\"three\"]", "One,Three")]
    [InlineData("typeLine", "contains", "\"instant\"", "Two")]
    [InlineData("colors", "contains", "\"g\"", "One")]
    [InlineData("colors", "in", "[\"R\",\"G\"]", "One,Three")]
    [InlineData("gameChanger", "eq", "true", "Three")]
    public void Run_Where_EachOperatorKeepsOnlyMatchingCards(string field, string op, string valueJson, string expected)
    {
        ScryfallCard one = ListTestCards.Make("One", manaValue: 1m, colors: ["G"]);
        ScryfallCard two = ListTestCards.Make("Two", typeLine: "Instant", manaValue: 2m, colors: ["U"]);
        ScryfallCard three = ListTestCards.Make("Three", manaValue: 3m, colors: ["R"]) with { GameChanger = true };
        ScryfallListQueryRequest request = Request(
            where: [new ScryfallListCondition(field, op, Json(valueJson))],
            rows: Rows(["name"]));

        ScryfallListQueryResult result = Success([Wrap(one), Wrap(two), Wrap(three)], request);

        Assert.Equal(
            expected.Split(',').Order(StringComparer.Ordinal),
            result.Rows!.Items.Select(row => (string)row.Get("name")!).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Verifies sorted rows are stable across pages, ordered by the key, with ties broken by name and missing values
    /// last (case Q08).
    /// </summary>
    [Fact]
    public void Run_SortedRows_AreStableAcrossPagesAndPutMissingValuesLast()
    {
        List<ScryfallListEntry> deck =
        [
            ListTestCards.Entry(ListTestCards.Make("B", usd: "5.00")),
            ListTestCards.Entry(ListTestCards.Make("A", usd: "5.00")),
            ListTestCards.Entry(ListTestCards.Make("C", usd: "9.00")),
            ListTestCards.Entry(ListTestCards.Make("D")),
            ListTestCards.Entry(ListTestCards.Make("E", usd: "1.00")),
        ];
        ScryfallListSort[] sort = [new ScryfallListSort("priceUsd", "desc")];

        ScryfallListRows first = Success(deck, Request(rows: new ScryfallListRowOptions(["name", "priceUsd"], sort,
            0, 2))).Rows!;
        ScryfallListRows second = Success(deck, Request(rows: new ScryfallListRowOptions(["name"], sort, 2, 2))).Rows!;
        ScryfallListRows third = Success(deck, Request(rows: new ScryfallListRowOptions(["name"], sort, 4, 2))).Rows!;

        Assert.Equal(["C", "A"], first.Items.Select(row => (string)row.Get("name")!));
        Assert.Equal(["B", "E"], second.Items.Select(row => (string)row.Get("name")!));
        Assert.Equal(["D"], third.Items.Select(row => (string)row.Get("name")!));
        Assert.Equal(5, first.TotalRows);
        Assert.Equal(9L, first.Items[0].Get("priceUsd"));
        Assert.NotNull(first.Items[0].Get("oracleId"));
    }

    /// <summary>
    /// Verifies the same request over the same list gives the same JSON twice, whatever the entry order
    /// (QUERY-006).
    /// </summary>
    [Fact]
    public void Run_SameRequestTwice_GivesIdenticalJson()
    {
        List<ScryfallListEntry> deck =
        [
            ListTestCards.Entry(ListTestCards.Make("B", colors: ["G", "U"], usd: "1.50"), 2),
            ListTestCards.Entry(ListTestCards.Make("A", colors: ["G"], usd: "0.50")),
        ];
        ScryfallListQueryRequest request = Request(
            groupBy: ["colors", "manaValue"],
            metrics: ["quantity", "cards", "sum:priceUsd", "pips", "sources"],
            rows: Rows(["name", "colors", "priceUsd"]));

        string once = JsonSerializer.Serialize(Success(deck, request));
        string twice = JsonSerializer.Serialize(Success([.. deck.AsEnumerable().Reverse()], request));

        Assert.Equal(once, twice);
    }

    /// <summary>
    /// Verifies oracle text matches case-insensitively across every face and cannot be grouped (case Q11).
    /// </summary>
    [Fact]
    public void Run_OracleTextContains_MatchesAnyFaceIgnoringCaseAndCannotBeGrouped()
    {
        ScryfallCard tapped = ListTestCards.Make("Tapped Dual", oracleText: "This land enters tapped.");
        ScryfallCard other = ListTestCards.Make("Other", oracleText: "Draw a card.");
        ScryfallCard mdfc = TwoFacedCard();
        ScryfallListQueryRequest request = Request(
            where: [new ScryfallListCondition("oracleText", "contains", Json("\"ENTERS TAPPED\""))],
            rows: Rows(["name"]));

        ScryfallListQueryResult result = Success([Wrap(tapped), Wrap(other), Wrap(mdfc)], request);

        Assert.Equal(
            ["Spell // Land", "Tapped Dual"],
            result.Rows!.Items.Select(row => (string)row.Get("name")!));
        OperationInvalidInput grouped = Failure([Wrap(tapped)], Request(groupBy: ["oracleText"]));
        Assert.Equal("groupBy[0]", grouped.Path);
        Assert.Equal("invalid-field", grouped.ReasonCode);
        Assert.DoesNotContain("oracleText", grouped.Message.Split("Allowed fields:")[1], StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies legality filters read Scryfall status values (case Q12).
    /// </summary>
    [Fact]
    public void Run_LegalInAndBannedIn_UseScryfallLegalityStatuses()
    {
        ScryfallCard legal = ListTestCards.Make(
            "Legal",
            legalities: new() { ["commander"] = "legal", ["oathbreaker"] = "legal" });
        ScryfallCard banned = ListTestCards.Make(
            "Banned",
            legalities: new() { ["commander"] = "banned", ["oathbreaker"] = "legal" });
        ScryfallCard notLegal = ListTestCards.Make("Not legal", legalities: new() { ["commander"] = "not_legal" });
        List<ScryfallListEntry> deck = [.. new[] { legal, banned, notLegal }.Select(Wrap)];

        ScryfallListQueryResult oathbreaker = Success(
            deck,
            Request(where: [new ScryfallListCondition("legalIn", "contains", Json("\"oathbreaker\""))],
                rows: Rows(["name"])));
        ScryfallListQueryResult bannedInCommander = Success(
            deck,
            Request(where: [new ScryfallListCondition("bannedIn", "contains", Json("\"commander\""))],
                rows: Rows(["name"])));

        Assert.Equal(["Banned", "Legal"], oathbreaker.Rows!.Items.Select(row => (string)row.Get("name")!));
        Assert.Equal(["Banned"], bannedInCommander.Rows!.Items.Select(row => (string)row.Get("name")!));
    }

    /// <summary>
    /// Verifies the games filter, the reserved group, and that cards without games data are counted as missing
    /// (case Q13).
    /// </summary>
    [Fact]
    public void Run_GamesAndReserved_FilterGroupAndCountMissingGamesData()
    {
        ScryfallCard arena = ListTestCards.Make("Arena", games: ["paper", "arena"], reserved: true);
        ScryfallCard paper = ListTestCards.Make("Paper", games: ["paper"]);
        ScryfallCard noData = ListTestCards.Make("No data", games: []);
        List<ScryfallListEntry> deck = [.. new[] { arena, paper, noData }.Select(Wrap)];

        ScryfallListQueryResult result = Success(
            deck,
            Request(
                where: [new ScryfallListCondition("games", "contains", Json("\"arena\""))],
                groupBy: ["reserved"]));

        ScryfallOrderedObject group = Assert.Single(result.Groups);
        Assert.Equal(true, KeyOf(group, "reserved"));
        ScryfallMissingValues missing = Assert.Single(result.MissingValues);
        Assert.Equal(("games", 1), (missing.Field, missing.Cards));
    }

    /// <summary>
    /// Verifies the other price columns give exact decimal sums and report missing values per field (case Q14).
    /// </summary>
    [Fact]
    public void Run_EuroAndTixSums_AreExactAndReportMissingValuesPerField()
    {
        ScryfallCard first = ListTestCards.Make("First", prices: new() { ["eur"] = "1.10", ["tix"] = "0.02" });
        ScryfallCard second = ListTestCards.Make("Second", prices: new() { ["eur"] = "2.25", ["tix"] = null });
        ScryfallCard third = ListTestCards.Make("Third", prices: new() { ["eur"] = null, ["tix"] = "0.01" });
        List<ScryfallListEntry> deck = [.. new[] { first, second, third }.Select(card => ListTestCards.Entry(card, 2))];

        ScryfallListQueryResult result = Success(deck, Request(metrics: ["sum:priceEur", "sum:priceTix"]));

        ScryfallOrderedObject group = Assert.Single(result.Groups);
        Assert.Equal("6.70", group.Get("sum:priceEur"));
        Assert.Equal("0.06", group.Get("sum:priceTix"));
        Assert.Equal(
            [("priceEur", 1), ("priceTix", 1)],
            result.MissingValues.Select(value => (value.Field, value.Cards)));
    }

    /// <summary>
    /// Verifies the list-specific fields: types, subtypes, supertypes, layout, power, and deck fields.
    /// </summary>
    [Fact]
    public void Run_TypeFieldsAndDeckFields_ReadTheCardAndTheEntry()
    {
        ScryfallCard card = ListTestCards.Make(
            "Legend",
            typeLine: "Legendary Artifact Creature — Robot Wizard",
            power: "*",
            layout: "transform");
        List<ScryfallListEntry> deck = [ListTestCards.Entry(card, 2, zone: "commander", categories: ["Ramp", "Draw"])];

        ScryfallListQueryResult result = Success(
            deck,
            Request(rows: Rows(
                ["cardTypes", "subtypes", "supertypes", "layout", "power", "zone", "category", "quantity",
                    "edhrecRank"])));

        ScryfallOrderedObject row = Assert.Single(result.Rows!.Items);
        Assert.Equal(["Artifact", "Creature"], (IReadOnlyList<string>)row.Get("cardTypes")!);
        Assert.Equal(["Robot", "Wizard"], (IReadOnlyList<string>)row.Get("subtypes")!);
        Assert.Equal(["Legendary"], (IReadOnlyList<string>)row.Get("supertypes")!);
        Assert.Equal("transform", row.Get("layout"));
        Assert.Null(row.Get("power"));
        Assert.Equal("commander", row.Get("zone"));
        Assert.Equal(2L, row.Get("quantity"));
        Assert.Null(row.Get("edhrecRank"));
    }

    /// <summary>
    /// Verifies bad requests fail with a reason, a path, and the allowed values (case Q10 and the other checks).
    /// </summary>
    [Theory]
    [InlineData("unknown-group", "invalid-field", "groupBy[0]")]
    [InlineData("unknown-where", "invalid-field", "where[0].field")]
    [InlineData("operator", "invalid-operator", "where[0].op")]
    [InlineData("value-type", "invalid-value", "where[0].value")]
    [InlineData("in-not-array", "invalid-value", "where[0].value")]
    [InlineData("in-empty", "invalid-value", "where[0].value")]
    [InlineData("three-groups", "too-many-group-fields", "groupBy")]
    [InlineData("metric-name", "invalid-metric", "metrics[0]")]
    [InlineData("metric-text-field", "invalid-metric", "metrics[0]")]
    [InlineData("deck-field-on-search", "invalid-field", "groupBy[0]")]
    [InlineData("row-field", "invalid-field", "rows.fields[0]")]
    [InlineData("sort-field", "invalid-field", "rows.sortBy[0].field")]
    [InlineData("sort-list", "invalid-sort", "rows.sortBy[0]")]
    [InlineData("sort-direction", "invalid-sort", "rows.sortBy[0]")]
    public void Run_InvalidRequest_FailsWithReasonPathAndAllowedValues(string kind, string reason, string path)
    {
        ScryfallListQueryRequest request = kind switch
        {
            "unknown-group" => Request(groupBy: ["colour"]),
            "unknown-where" => Request(where: [new ScryfallListCondition("colour", "eq", Json("\"R\""))]),
            "operator" => Request(where: [new ScryfallListCondition("manaValue", "contains", Json("1"))]),
            "value-type" => Request(where: [new ScryfallListCondition("manaValue", "eq", Json("\"1\""))]),
            "in-not-array" => Request(where: [new ScryfallListCondition("manaValue", "in", Json("1"))]),
            "in-empty" => Request(where: [new ScryfallListCondition("manaValue", "in", Json("[]"))]),
            "three-groups" => Request(groupBy: ["colors", "tags", "manaValue"]),
            "metric-name" => Request(metrics: ["median:priceUsd"]),
            "metric-text-field" => Request(metrics: ["sum:name"]),
            "deck-field-on-search" => Request(groupBy: ["zone"]),
            "row-field" => Request(rows: Rows(["nope"])),
            "sort-field" => Request(rows: Sorted("oracleText", "asc")),
            "sort-list" => Request(rows: Sorted("colors", "asc")),
            _ => Request(rows: Sorted("name", "up")),
        };

        OperationInvalidInput failure = Failure([Wrap(ListTestCards.Make("A"))], request);

        Assert.Equal(reason, failure.ReasonCode);
        Assert.Equal(path, failure.Path);
        if (reason == "invalid-field")
        {
            Assert.Contains("manaValue", failure.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Verifies a list over the work limit fails and says the limit.
    /// </summary>
    [Fact]
    public void Run_ListOverTheWorkLimit_FailsAndStatesTheLimit()
    {
        ScryfallListEntry entry = Wrap(ListTestCards.Make("A"));
        List<ScryfallListEntry> big = [.. Enumerable.Repeat(entry, ScryfallListQuery.MaximumEntries + 1)];

        OperationInvalidInput failure = Failure(big, Request());

        Assert.Equal("list-too-large", failure.ReasonCode);
        Assert.Contains("20000", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies an empty match still returns one empty group when nothing is grouped, and no groups when grouped.
    /// </summary>
    [Fact]
    public void Run_NoMatches_GivesZeroTotals()
    {
        ScryfallListEntry entry = Wrap(ListTestCards.Make("A", manaValue: 1m));
        ScryfallListCondition none = new("manaValue", "gt", Json("9"));

        ScryfallListQueryResult ungrouped = Success([entry], Request(where: [none]));
        ScryfallListQueryResult grouped = Success([entry], Request(where: [none], groupBy: ["colors"]));

        Assert.Equal(0L, Assert.Single(ungrouped.Groups).Get("quantity"));
        Assert.Empty(grouped.Groups);
        Assert.Equal(0, ungrouped.Matched.Cards);
    }

    /// <summary>
    /// Verifies the field and operator lists the schema and errors publish.
    /// </summary>
    [Fact]
    public void FieldNamesAndOperators_ListEveryPublishedValue()
    {
        Assert.Contains("oracleText", ScryfallListQuery.FieldNames);
        Assert.Contains("priceTix", ScryfallListQuery.FieldNames);
        Assert.Equal(8, ScryfallListQuery.Operators.Count);
        Assert.Equal(ScryfallListQuery.FieldNames.Count, ScryfallListQuery.FieldNames.Distinct().Count());
    }

    /// <summary>
    /// Verifies a field named twice in groupBy, a metric named twice, or a row field named twice is refused with the
    /// path of the second one, so no result carries a repeated JSON key.
    /// </summary>
    [Fact]
    public void Run_RepeatedGroupFieldMetricOrRowField_IsInvalid()
    {
        List<ScryfallListEntry> deck = [ListTestCards.Entry(ListTestCards.Make("A"))];

        OperationInvalidInput group = Failure(deck, Request(groupBy: ["colors", "colors"]));
        OperationInvalidInput metric = Failure(deck, Request(metrics: ["quantity", "quantity"]));
        OperationInvalidInput sum = Failure(deck, Request(metrics: ["sum:manaValue", "sum:manaValue"]));
        OperationInvalidInput row = Failure(deck, Request(rows: Rows(["name", "name"])));

        Assert.Equal(("invalid-field", "groupBy[1]"), (group.ReasonCode, group.Path));
        Assert.Equal(("invalid-metric", "metrics[1]"), (metric.ReasonCode, metric.Path));
        Assert.Equal(("invalid-metric", "metrics[1]"), (sum.ReasonCode, sum.Path));
        Assert.Equal(("invalid-field", "rows.fields[1]"), (row.ReasonCode, row.Path));
    }

    /// <summary>
    /// Verifies a card whose real value is the text none stays apart from the group of cards with no value.
    /// </summary>
    [Fact]
    public void Run_GroupByTags_KeepsARealNoneTagApartFromMissingValues()
    {
        List<ScryfallListEntry> deck =
        [
            ListTestCards.Entry(ListTestCards.Make("Tagged none"), tags: ["none"]),
            ListTestCards.Entry(ListTestCards.Make("Untagged")),
        ];

        ScryfallListQueryResult result = Success(deck, Request(groupBy: ["tags"]));

        Assert.Equal(2, result.Groups.Count);
        // The real tag is the text none; the missing-value group has a JSON null key, and comes last.
        Assert.Equal("none", (string)KeyOf(result.Groups[0], "tags")!);
        Assert.Null(KeyOf(result.Groups[1], "tags"));
        Assert.Equal([1L, 1L], result.Groups.Select(group => (long)group.Get("quantity")!));
    }

    /// <summary>
    /// Verifies an in filter with a value that is not an array is refused for its shape before its type.
    /// </summary>
    [Fact]
    public void Run_InFilterWithoutAnArray_IsInvalidForItsShape()
    {
        List<ScryfallListEntry> deck = [ListTestCards.Entry(ListTestCards.Make("A"))];

        OperationInvalidInput invalid = Failure(
            deck,
            Request(where: [new ScryfallListCondition("manaValue", "in", Json("\"three\""))]));

        Assert.Equal(("invalid-value", "where[0].value"), (invalid.ReasonCode, invalid.Path));
        Assert.Contains("array", invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds a request with the given parts, for a list that has no deck fields.
    /// </summary>
    private static ScryfallListQueryRequest Request(
        IReadOnlyList<ScryfallListCondition>? where = null,
        IReadOnlyList<string>? groupBy = null,
        IReadOnlyList<string>? metrics = null,
        ScryfallListRowOptions? rows = null)
    {
        return new ScryfallListQueryRequest(where ?? [], groupBy ?? [], metrics ?? [], rows, HasDeckFields: false);
    }

    /// <summary>
    /// Builds row options that read the first 50 rows with the given fields.
    /// </summary>
    private static ScryfallListRowOptions Rows(IReadOnlyList<string> fields)
    {
        return new ScryfallListRowOptions(fields, [], 0, 50);
    }

    /// <summary>
    /// Builds row options with one sort key.
    /// </summary>
    private static ScryfallListRowOptions Sorted(string field, string direction)
    {
        return new ScryfallListRowOptions(["name"], [new ScryfallListSort(field, direction)], 0, 5);
    }

    /// <summary>
    /// Wraps a card in a one-copy entry.
    /// </summary>
    private static ScryfallListEntry Wrap(ScryfallCard card)
    {
        return ListTestCards.Entry(card);
    }

    /// <summary>
    /// Reads one part of a group key.
    /// </summary>
    private static object? KeyOf(ScryfallOrderedObject group, string field)
    {
        return ((ScryfallOrderedObject)group.Get("key")!).Get(field);
    }

    /// <summary>
    /// Parses a JSON value for a filter.
    /// </summary>
    private static JsonElement Json(string text)
    {
        return JsonElement.Parse(text);
    }

    /// <summary>
    /// Builds a modal card whose back face is a land that enters tapped.
    /// </summary>
    private static ScryfallCard TwoFacedCard()
    {
        const string text = """
            {"id":"aaaaaaaa-0000-4000-8000-000000000001","oracle_id":"bbbbbbbb-0000-4000-8000-000000000001",
             "name":"Spell // Land","set":"tst","collector_number":"9","lang":"en","layout":"modal_dfc",
             "card_faces":[{"name":"Spell","type_line":"Sorcery","oracle_text":"Draw a card."},
                           {"name":"Land","type_line":"Land","oracle_text":"This land Enters Tapped."}]}
            """;
        using JsonDocument document = JsonDocument.Parse(text);
        return ScryfallMapper.Card(document.RootElement, DateTimeOffset.UnixEpoch, "test", includeRaw: false);
    }

    /// <summary>
    /// Runs a query that must succeed. Entries that carry a zone make the deck fields available.
    /// </summary>
    private static ScryfallListQueryResult Success(
        IReadOnlyList<ScryfallListEntry> entries,
        ScryfallListQueryRequest request)
    {
        ScryfallListQueryRequest adjusted = request with
        {
            HasDeckFields = request.HasDeckFields || entries.Any(entry => entry.Zone is not null),
        };
        return Assert.IsType<OperationSuccess<ScryfallListQueryResult>>(ScryfallListQuery.Run(entries,
            adjusted).Value).Data;
    }

    /// <summary>
    /// Runs a query that must fail as invalid input.
    /// </summary>
    private static OperationInvalidInput Failure(
        IReadOnlyList<ScryfallListEntry> entries,
        ScryfallListQueryRequest request)
    {
        return Assert.IsType<OperationInvalidInput>(ScryfallListQuery.Run(entries, request).Value);
    }
}
