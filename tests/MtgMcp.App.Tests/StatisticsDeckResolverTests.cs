using MtgMcp.App.Statistics;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;
using MtgMcp.Statistics;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies exact local deck selectors, revision guards, quantity expansion, and format neutrality.
/// </summary>
public sealed class StatisticsDeckResolverTests
{
    /// <summary>
    /// Verifies entry, zone, and category selector unions produce canonical disjoint group buckets.
    /// </summary>
    [Fact]
    public async Task ResolvePopulation_SelectorUnionExpandsQuantitiesAndDisclosesPartition()
    {
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        (DeckDocument deck, Guid firstId, Guid secondId, _, Guid categoryId) =
            await CreateDeckAsync(store, "custom-format");
        StatisticsDeckResolver resolver = new(store);
        DeckStatisticsPopulationInput input = new(
            deck.DeckId,
            deck.Revision,
            [new DeckZoneNamesSelectorInput(["main"]), new DeckEntryIdsSelectorInput([secondId])],
            [
                new DeckStatisticsGroupInput(
                    "category",
                    [new DeckCategoryIdsSelectorInput([categoryId])]),
                new DeckStatisticsGroupInput(
                    "side",
                    [new DeckZoneNamesSelectorInput(["sideboard"])]),
            ]);

        StatisticsPopulation population = RequireSuccess(await resolver.ResolvePopulationAsync(
            input,
            TestContext.Current.CancellationToken));
        DeckDocument canonicalDeck = RequireSuccess(await store.GetAsync(
            deck.DeckId,
            TestContext.Current.CancellationToken));

        Assert.Equal(["category", "side"], population.DeclaredGroups);
        Assert.Equal(3, population.Buckets.Count);
        Assert.Equal(6, population.Buckets.Sum(value => value.Count));
        Assert.Equal(
            canonicalDeck.Entries.Select(value => value.EntryId),
            population.DeckEvidence!.SelectedEntries.Select(value => value.EntryId));
        Assert.Empty(population.DeckEvidence.ExcludedEntries);
        StatisticsPopulationBucket side = Assert.Single(
            population.Buckets,
            value => value.Groups.Contains("side", StringComparer.Ordinal));
        Assert.Equal(1, side.Count);
        Assert.Equal(["category", "side"], side.Groups);
    }

    /// <summary>
    /// Verifies raw populations pass through without local storage access.
    /// </summary>
    [Fact]
    public async Task ResolvePopulation_RawInputDoesNotCreateStorage()
    {
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        StatisticsDeckResolver resolver = new(store);
        RawStatisticsPopulationInput input = new(
            [new StatisticsPopulationBucket(10, ["success"])],
            ["success"]);

        StatisticsPopulation population = RequireSuccess(await resolver.ResolvePopulationAsync(
            input,
            TestContext.Current.CancellationToken));

        Assert.Equal(10, Assert.Single(population.Buckets).Count);
        Assert.Null(population.DeckEvidence);
        Assert.False(File.Exists(Path.Combine(temporary.Path, "decks.db")));
    }

    /// <summary>
    /// Verifies summary resolution freezes stored fields, categories, selected entries, and caller values.
    /// </summary>
    [Fact]
    public async Task ResolveSummary_UsesStoredFieldsAndCallerValuesOnly()
    {
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        (DeckDocument deck, Guid firstId, _, Guid thirdId, Guid categoryId) =
            await CreateDeckAsync(store, "commander");
        StatisticsDeckResolver resolver = new(store);
        DeckSummarySourceInput input = new(
            deck.DeckId,
            deck.Revision,
            [new DeckZoneNamesSelectorInput(["main"])],
            [new DeckNumericSeriesInput(
                "caller-value",
                [new DeckNumericValueInput(firstId, "2")])],
            [50],
            new DeckZonePartitionInput(["main"], []));

        DeckSummaryRequest summary = RequireSuccess(await resolver.ResolveSummaryAsync(
            input,
            TestContext.Current.CancellationToken));

        Assert.Equal("commander", deck.Format);
        Assert.Equal([firstId, thirdId], summary.SelectedEntries.Select(value => value.EntryId));
        Assert.Single(summary.ExcludedEntries);
        Assert.Contains(
            summary.SelectedEntries[0].Categories,
            value => value.CategoryId == categoryId);
        Assert.Equal("caller-value", Assert.Single(summary.NumericSeries).Name);
        Assert.Equal(50, Assert.Single(summary.Percentiles));
    }

    /// <summary>
    /// Verifies format labels and commander-zone presence never affect identical selector results.
    /// </summary>
    [Fact]
    public async Task ResolvePopulation_CustomAndCommanderLabelsUseIdenticalSelectionMath()
    {
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        (DeckDocument custom, _, _, _, _) = await CreateDeckAsync(store, "future-format");
        (DeckDocument commander, _, _, _, _) = await CreateDeckAsync(store, "commander");
        StatisticsDeckResolver resolver = new(store);

        StatisticsPopulation customPopulation = RequireSuccess(await resolver.ResolvePopulationAsync(
            new DeckStatisticsPopulationInput(
                custom.DeckId,
                custom.Revision,
                [new DeckZoneNamesSelectorInput(["main"])],
                []),
            TestContext.Current.CancellationToken));
        StatisticsPopulation commanderPopulation = RequireSuccess(await resolver.ResolvePopulationAsync(
            new DeckStatisticsPopulationInput(
                commander.DeckId,
                commander.Revision,
                [new DeckZoneNamesSelectorInput(["main"])],
                []),
            TestContext.Current.CancellationToken));

        Assert.Equal(
            customPopulation.Buckets.Select(value => value.Count),
            commanderPopulation.Buckets.Select(value => value.Count));
        DeckValidationReport validation = RequireSuccess(await store.ValidateAsync(
            commander.DeckId,
            TestContext.Current.CancellationToken));
        Assert.True(validation.IsStructurallyValid);
    }

    /// <summary>
    /// Verifies stale revisions, missing decks, invalid selectors, and out-of-population groups remain explicit.
    /// </summary>
    [Fact]
    public async Task ResolvePopulation_InvalidDeckEvidenceFailsClosed()
    {
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        (DeckDocument deck, Guid firstId, Guid secondId, _, Guid categoryId) =
            await CreateDeckAsync(store, "custom");
        StatisticsDeckResolver resolver = new(store);

        Assert.IsType<OperationConflict>((await resolver.ResolvePopulationAsync(
            new DeckStatisticsPopulationInput(
                deck.DeckId,
                deck.Revision + 1,
                [new DeckEntryIdsSelectorInput([firstId])],
                []),
            TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationNotFound>((await resolver.ResolvePopulationAsync(
            new DeckStatisticsPopulationInput(
                Guid.CreateVersion7(),
                1,
                [new DeckEntryIdsSelectorInput([firstId])],
                []),
            TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationInvalidInput>((await resolver.ResolvePopulationAsync(
            new DeckStatisticsPopulationInput(
                deck.DeckId,
                deck.Revision,
                [new DeckEntryIdsSelectorInput([Guid.CreateVersion7()])],
                []),
            TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationInvalidInput>((await resolver.ResolvePopulationAsync(
            new DeckStatisticsPopulationInput(
                deck.DeckId,
                deck.Revision,
                [new DeckEntryIdsSelectorInput([firstId])],
                [new DeckStatisticsGroupInput(
                    "outside",
                    [new DeckEntryIdsSelectorInput([secondId])])]),
            TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationInvalidInput>((await resolver.ResolvePopulationAsync(
            new DeckStatisticsPopulationInput(
                deck.DeckId,
                deck.Revision,
                [new DeckCategoryIdsSelectorInput([categoryId, categoryId])],
                []),
            TestContext.Current.CancellationToken)).Value);
    }

    /// <summary>
    /// Verifies null roots and canceled local reads fail before returning selected evidence.
    /// </summary>
    [Fact]
    public async Task ResolvePopulation_NullAndCancellationStopClosed()
    {
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        StatisticsDeckResolver resolver = new(store);
        (DeckDocument deck, _, _, _, _) = await CreateDeckAsync(store, "custom");

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await resolver.ResolvePopulationAsync(
                null!,
                TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await resolver.ResolveSummaryAsync(
                null!,
                TestContext.Current.CancellationToken));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await resolver.ResolvePopulationAsync(
                new DeckStatisticsPopulationInput(
                    deck.DeckId,
                    deck.Revision,
                    [new DeckZoneNamesSelectorInput(["main"])],
                    []),
                cancellation.Token));
    }

    /// <summary>
    /// Creates one three-entry format-neutral local deck with overlapping categories.
    /// </summary>
    private static async Task<(DeckDocument Deck, Guid FirstId, Guid SecondId, Guid ThirdId, Guid CategoryId)>
        CreateDeckAsync(SqliteDeckStore store, string format)
    {
        Guid firstId = Guid.CreateVersion7();
        Guid secondId = Guid.CreateVersion7();
        Guid thirdId = Guid.CreateVersion7();
        Guid categoryId = Guid.CreateVersion7();
        DeckDocument deck = RequireSuccess(await store.CreateAsync(
            new DeckCreateRequest(
                $"Statistics {format}",
                Format: format,
                Entries:
                [
                    new DeckEntryDraft(2, "First", Zone: "main", EntryId: firstId),
                    new DeckEntryDraft(1, "Second", Zone: "sideboard", EntryId: secondId),
                    new DeckEntryDraft(3, "Third", Zone: "main", EntryId: thirdId),
                ],
                Categories: [new DeckCategoryDraft("Selected", CategoryId: categoryId)],
                CategoryAssignments:
                [
                    new DeckCategoryAssignment(firstId, categoryId, true),
                    new DeckCategoryAssignment(secondId, categoryId, false),
                ]),
            TestContext.Current.CancellationToken));
        return (deck, firstId, secondId, thirdId, categoryId);
    }

    /// <summary>
    /// Extracts one successful operation result.
    /// </summary>
    private static T RequireSuccess<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }
}
