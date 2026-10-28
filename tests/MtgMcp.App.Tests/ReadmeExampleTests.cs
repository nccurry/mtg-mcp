using System.Text.Json;
using System.Text.RegularExpressions;
using MtgMcp.App.Hosting;

namespace MtgMcp.App.Tests;

/// <summary>
/// Binds every request example in the README to the real tool schemas, so the worked examples cannot drift from
/// the tools (DOC-001). An example is a <c>json</c> block that follows a <c>&lt;!-- example: tool_name --&gt;</c>
/// comment.
/// </summary>
public sealed partial class ReadmeExampleTests : IClassFixture<ToolCatalogFixture>
{
    /// <summary>
    /// Holds the argument catalog of one full registration.
    /// </summary>
    private readonly ToolArgumentCatalog catalog;

    /// <summary>
    /// Takes the catalog the fixture built, so the examples bind through the registered tools.
    /// </summary>
    public ReadmeExampleTests(ToolCatalogFixture fixture)
    {
        catalog = fixture.Catalog;
    }

    /// <summary>
    /// Matches an example marker and the JSON block after it.
    /// </summary>
    [GeneratedRegex(
        @"<!-- example: (?<tool>[a-z_]+) -->\r?\n```json\r?\n(?<json>.*?)\r?\n```",
        RegexOptions.Singleline)]
    private static partial Regex ExampleBlock();

    /// <summary>
    /// Verifies the README holds the worked examples the plan requires and that each one binds to its tool.
    /// </summary>
    [Fact]
    public void ReadmeExamples_BindToTheRegisteredToolSchemas()
    {
        (string Tool, string Json)[] examples = ReadExamples();

        Assert.Equal(
            [
                "card_lists_compare",
                "card_list_query",
                "local_deck_validate",
                "local_deck_category_rules_preview",
                "stats_simulate",
            ],
            examples.Select(example => example.Tool));
        foreach ((string tool, string json) in examples)
        {
            Dictionary<string, JsonElement> arguments = Parse(json);

            Assert.Null(ToolCallPolicy.DiagnoseArguments(catalog, tool, arguments));
            Assert.Null(ToolCallPolicy.FindUnknownArgument(catalog, tool, arguments));
        }
    }

    /// <summary>
    /// Verifies the worked examples ask for what the README says they do.
    /// </summary>
    [Fact]
    public void ReadmeExamples_AskForTheDocumentedJobs()
    {
        Dictionary<string, JsonElement> byTool = ReadExamples().ToDictionary(
            example => example.Tool,
            example => JsonElement.Parse(example.Json),
            StringComparer.Ordinal);

        Assert.Equal("difference", byTool["card_lists_compare"].GetProperty("operation").GetString());
        Assert.Equal(
            "scryfall-snapshot",
            byTool["card_lists_compare"].GetProperty("left").GetProperty("kind").GetString());
        Assert.Equal("deck", byTool["card_lists_compare"].GetProperty("right").GetProperty("kind").GetString());
        Assert.Equal(
            ["quantity", "pips", "sources"],
            byTool["card_list_query"].GetProperty("metrics").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal("commander", byTool["local_deck_validate"].GetProperty("rules").GetString());
        Assert.Equal(
            4,
            byTool["local_deck_category_rules_preview"].GetProperty("source").GetProperty("ruleSet")
                .GetProperty("rules").GetArrayLength());
        Assert.Equal(
            ["four-mana-turn-4"],
            byTool["stats_simulate"].GetProperty("conditions").EnumerateArray()
                .Select(item => item.GetProperty("id").GetString()));
        Assert.Equal(10_000, byTool["stats_simulate"].GetProperty("samples").GetInt32());
    }

    /// <summary>
    /// Verifies the checks the examples go through would reject a misspelled top-level field and a misspelled nested
    /// field, so a passing example proves something.
    /// </summary>
    [Fact]
    public void ReadmeExampleChecks_RejectMisspelledFields()
    {
        string topLevel = "{\"deckId\":\"0193e97a-ef95-7700-8000-000000000001\",\"rulez\":\"commander\"}";
        string nested =
            "{\"left\":{\"kind\":\"deck\",\"deckIdd\":\"0193e97a-ef95-7700-8000-000000000001\"}," +
            "\"right\":{\"kind\":\"deck\",\"deckId\":\"0193e97a-ef95-7700-8000-000000000001\"}," +
            "\"operation\":\"difference\"}";

        Assert.NotNull(ToolCallPolicy.FindUnknownArgument(catalog, "local_deck_validate", Parse(topLevel)));
        Assert.NotNull(ToolCallPolicy.DiagnoseArguments(catalog, "card_lists_compare", Parse(nested)));
    }

    /// <summary>
    /// Parses a JSON object of tool arguments.
    /// </summary>
    private static Dictionary<string, JsonElement> Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
    }

    /// <summary>
    /// Reads every marked example from the README at the repository root.
    /// </summary>
    private static (string Tool, string Json)[] ReadExamples()
    {
        string readme = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "README.md"));
        return
        [
            .. ExampleBlock().Matches(readme)
                .Select(match => (match.Groups["tool"].Value, match.Groups["json"].Value)),
        ];
    }

    /// <summary>
    /// Walks up from the test folder to the folder that holds the solution file.
    /// </summary>
    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "mtg-mcp.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
