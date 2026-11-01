using System.Text.Json;
using MtgMcp.App.Configuration;
using MtgMcp.App.Decks;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Tests;

/// <summary>
/// Runs the example requests published in the descriptions of the batch edit and category rule preview tools, so
/// the examples cannot drift from the tools (SCHEMA-006).
/// </summary>
public sealed class DeckToolExampleTests : IClassFixture<ToolCatalogFixture>
{
    /// <summary>
    /// Holds the argument catalog of one full registration.
    /// </summary>
    private readonly ToolArgumentCatalog catalog;

    /// <summary>
    /// Takes the catalog the fixture built, so the examples bind through the registered tools.
    /// </summary>
    public DeckToolExampleTests(ToolCatalogFixture fixture)
    {
        catalog = fixture.Catalog;
    }

    /// <summary>
    /// Verifies the batch edit description holds a minimal and a full example, and that every example binds to the
    /// tool's parameters and applies to a deck.
    /// </summary>
    [Fact]
    public async Task ApplyChangesExamples_BindAndApply()
    {
        string description = DescriptionOf("local_deck_apply_changes");
        (string Name, string Json)[] examples = ReadExamples(description);

        Assert.Equal(["minimal", "categorize", "full"], examples.Select(example => example.Name));
        foreach ((string name, string json) in examples)
        {
            using TemporaryDirectory temporary = new();
            using SqliteDeckStore store = new(temporary.Path, "test");
            DeckDocument deck = await CreateDeckAsync(store);
            string request = Fill(json, deck);

            Dictionary<string, JsonElement> arguments = Arguments(request);
            Assert.Null(ToolCallPolicy.DiagnoseArguments(catalog, "local_deck_apply_changes", arguments));
            using UninstalledCardData cardData = new();
            DeckBatchWriteTools tools = new(store, OperationMode.Local, cardData.Resolver);
            IReadOnlyList<DeckChangeInput> changes = arguments["changes"]
                .Deserialize<IReadOnlyList<DeckChangeInput>>(ToolCallPolicy.SerializerOptions)!;
            OperationResult<DeckWriteResult> result = await tools.ApplyChangesAsync(
                arguments["deckId"].GetGuid(),
                arguments["expectedRevision"].GetInt64(),
                changes,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(
                result.Value is OperationSuccess<DeckWriteResult>,
                $"{name}: {JsonSerializer.Serialize(result.Value)}");
        }
    }

    /// <summary>
    /// Verifies the batch edit full example makes every change it describes.
    /// </summary>
    [Fact]
    public async Task ApplyChangesFullExample_RenamesAddsCategorizesAndRemoves()
    {
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "test");
        using UninstalledCardData cardData = new();
        DeckDocument deck = await CreateDeckAsync(store);
        Dictionary<string, JsonElement> arguments = Arguments(Fill(DeckToolExamples.ApplyChangesFull, deck));
        IReadOnlyList<DeckChangeInput> changes = arguments["changes"]
            .Deserialize<IReadOnlyList<DeckChangeInput>>(ToolCallPolicy.SerializerOptions)!;

        OperationResult<DeckWriteResult> result = await new DeckBatchWriteTools(
            store,
            OperationMode.Local,
            cardData.Resolver)
            .ApplyChangesAsync(deck.DeckId, deck.Revision, changes, true, cancellationToken: TestContext.Current.CancellationToken);

        DeckDocument updated = Assert.IsType<OperationSuccess<DeckWriteResult>>(result.Value).Data.Deck!;
        Assert.Equal("Golgari Ramp", updated.Name);
        Assert.Equal(["Rhystic Study"], updated.Entries.Select(entry => entry.CardName));
        Assert.Equal(
            ["Draw"],
            updated.Categories.Where(category => category.Name == "Draw").Select(category => category.Name));
        Assert.Single(updated.CategoryAssignments, assignment => assignment.IsPrimary);
    }

    /// <summary>
    /// Verifies the category rule preview description holds a minimal and a full example, and that each binds and
    /// previews against installed card data.
    /// </summary>
    [Fact]
    public async Task RulesPreviewExamples_BindAndPreview()
    {
        string description = DescriptionOf("local_deck_category_rules_preview");
        (string Name, string Json)[] examples = ReadExamples(description);

        Assert.Equal(["minimal", "full"], examples.Select(example => example.Name));
        foreach ((string name, string json) in examples)
        {
            using TemporaryDirectory temporary = new();
            DeckCategorizationSourceFixture.DeckCategorizationSourceHandler handler =
                DeckCategorizationSourceFixture.CreateHandler();
            using ScryfallService scryfall = DeckCategorizationSourceFixture.CreateService(temporary.Path, handler);
            Assert.IsType<OperationSuccess<ScryfallCorpusSyncResult>>((await scryfall.SyncCorpusAsync(
                "refresh",
                cancellationToken: TestContext.Current.CancellationToken)).Value);
            using SqliteDeckStore store = new(temporary.Path, "test");
            DeckDocument deck = await CreateDeckAsync(store, DeckCategorizationSourceFixture.OracleId);
            Dictionary<string, JsonElement> arguments = Arguments(Fill(json, deck));

            Assert.Null(ToolCallPolicy.DiagnoseArguments(catalog, "local_deck_category_rules_preview", arguments));
            DeckCategorizationReadTools tools = new(new DeckCategorizationCoordinator(store, scryfall));
            OperationResult<DeckCategoryRulesPreviewView> result = await tools.PreviewAsync(
                arguments["deckId"].GetGuid(),
                arguments["source"].Deserialize<CategoryRuleSource>(ToolCallPolicy.SerializerOptions)!,
                arguments.TryGetValue("expectedRevision", out JsonElement revision) ? revision.GetInt64() : null,
                arguments.TryGetValue("freshnessPolicy", out JsonElement freshness)
                    ? freshness.GetString()!
                    : "default",
                cancellationToken: TestContext.Current.CancellationToken);

            DeckCategoryRulesPreviewView preview = Assert.IsType<OperationSuccess<DeckCategoryRulesPreviewView>>(
                result.Value).Data;
            Assert.Equal(name == "full" ? "matched" : "unmatched", Assert.Single(preview.Decisions).Status);
            Assert.True(preview.IsComplete, name);
        }
    }

    /// <summary>
    /// Verifies a preview with a stale revision returns a conflict, and one without a revision does not.
    /// </summary>
    [Fact]
    public async Task RulesPreview_RevisionIsOptional_AndStaleRevisionConflicts()
    {
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "test");
        using ScryfallService scryfall = new(
            temporary.Path,
            allowLocalWrites: false,
            "test",
            ToolCallPolicy.SerializerOptions);
        DeckDocument deck = await CreateDeckAsync(store);
        CategoryRuleSource source = new InlineCategoryRuleSource(new CategoryRuleSet(
            "add-only",
            [new CategoryRule(Guid.Parse(DeckToolExamples.PlaceholderCategoryId), [], [], [])]));
        DeckCategorizationReadTools tools = new(new DeckCategorizationCoordinator(store, scryfall));

        OperationResult<DeckCategoryRulesPreviewView> withoutRevision = await tools.PreviewAsync(
            deck.DeckId,
            source,
            cancellationToken: TestContext.Current.CancellationToken);
        OperationResult<DeckCategoryRulesPreviewView> stale = await tools.PreviewAsync(
            deck.DeckId,
            source,
            deck.Revision + 5,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<OperationSuccess<DeckCategoryRulesPreviewView>>(withoutRevision.Value);
        Assert.Equal(deck.Revision, Assert.IsType<OperationConflict>(stale.Value).Current!.Revision);
    }

    /// <summary>
    /// Verifies the rule resolver refuses a missing rule source.
    /// </summary>
    [Fact]
    public async Task RulesPreview_NullSource_ReturnsInvalidInput()
    {
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "test");
        using ScryfallService scryfall = new(
            temporary.Path,
            allowLocalWrites: false,
            "test",
            ToolCallPolicy.SerializerOptions);
        DeckDocument deck = await CreateDeckAsync(store);
        DeckCategoryRuleResolver resolver = new(scryfall);

        OperationResult<ResolvedDeckCategoryRules> result = await resolver.ResolveAsync(
            null,
            deck,
            "default",
            TestContext.Current.CancellationToken);

        Assert.Equal("invalid-category-rule-source", Assert.IsType<OperationInvalidInput>(result.Value).ReasonCode);
    }

    /// <summary>
    /// Creates the deck the examples refer to: one entry and one category with the placeholder IDs.
    /// </summary>
    private static async Task<DeckDocument> CreateDeckAsync(SqliteDeckStore store, Guid? oracleId = null)
    {
        DeckCreateRequest request = new(
            "Example Deck",
            Format: "commander",
            DeckId: Guid.Parse(DeckToolExamples.PlaceholderDeckId),
            Entries:
            [
                new DeckEntryDraft(
                    1,
                    "Fixture Ramp Card",
                    OracleId: oracleId,
                    EntryId: Guid.Parse(DeckToolExamples.PlaceholderEntryId)),
            ],
            Categories:
            [
                new DeckCategoryDraft("Ramp", CategoryId: Guid.Parse(DeckToolExamples.PlaceholderCategoryId)),
            ]);
        return Assert.IsType<OperationSuccess<DeckDocument>>(
            (await store.CreateAsync(request, TestContext.Current.CancellationToken)).Value).Data;
    }

    /// <summary>
    /// Puts the deck's real revision in place of the placeholder revision in an example.
    /// </summary>
    private static string Fill(string example, DeckDocument deck)
    {
        return example.Replace(
            $"\"expectedRevision\":{DeckToolExamples.PlaceholderRevision}",
            $"\"expectedRevision\":{deck.Revision}",
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads the published description of one tool.
    /// </summary>
    private static string DescriptionOf(string toolName)
    {
        using RegisteredToolSet registered = new(OperationMode.Remote);
        return registered.Tools.Single(tool => tool.ProtocolTool.Name == toolName).ProtocolTool.Description!;
    }

    /// <summary>
    /// Reads each <c>Example (name): {json}</c> from a description, in order.
    /// </summary>
    private static (string Name, string Json)[] ReadExamples(string description)
    {
        List<(string Name, string Json)> examples = [];
        int index = description.IndexOf(DeckToolExamples.ExamplePrefix, StringComparison.Ordinal);
        while (index >= 0)
        {
            int nameStart = index + DeckToolExamples.ExamplePrefix.Length;
            int nameEnd = description.IndexOf("): ", nameStart, StringComparison.Ordinal);
            int jsonStart = nameEnd + 3;
            int depth = 0;
            int end = jsonStart;
            do
            {
                depth += description[end] == '{' ? 1 : description[end] == '}' ? -1 : 0;
                end++;
            }
            while (depth > 0);

            examples.Add((description[nameStart..nameEnd], description[jsonStart..end]));
            index = description.IndexOf(DeckToolExamples.ExamplePrefix, end, StringComparison.Ordinal);
        }

        return [.. examples];
    }

    /// <summary>
    /// Parses a JSON object of tool arguments.
    /// </summary>
    private static Dictionary<string, JsonElement> Arguments(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
    }
}
