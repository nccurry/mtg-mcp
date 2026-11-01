using ModelContextProtocol.Server;
using MtgMcp.App.Configuration;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies the published tool descriptions state the facts an agent needs to pick the cheapest route: which tool
/// edits many entries, what an add needs, what an apply verifies, and which tools answer deck-building questions.
/// </summary>
public sealed class ToolFactsDescriptionTests
{
    /// <summary>
    /// Verifies the apply-changes description and the single-entry change fields point to patch-entries.
    /// </summary>
    [Fact]
    public void ApplyChanges_PointsSingleEntryEditsToPatchEntries()
    {
        McpServerTool tool = FindTool("local_deck_apply_changes");
        string schema = tool.ProtocolTool.InputSchema.GetRawText();

        Assert.Contains("prefer it to update-entry, assign-category, and unassign-category", tool.ProtocolTool.Description!, StringComparison.Ordinal);
        Assert.True(Occurrences(schema, "use patch-entries instead") >= 2, "assign and unassign point to patch-entries");
        Assert.Contains("use patch-entries instead", schema, StringComparison.Ordinal);
        Assert.Contains("add-entry needs only a card name", tool.ProtocolTool.Description!, StringComparison.Ordinal);
        Assert.Contains("first-listed", tool.ProtocolTool.Description!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies local_deck_get names each of its filters.
    /// </summary>
    [Fact]
    public void LocalDeckGet_NamesItsFilters()
    {
        string description = FindTool("local_deck_get").ProtocolTool.Description!;

        Assert.All(["cardNames", "zones", "categoryNames", "responseFormat"], name =>
            Assert.Contains(name, description, StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies the push apply says it reads Archidekt back, so a caller does not check again.
    /// </summary>
    [Fact]
    public void ArchidektPushApply_SaysItVerifiesEachCard()
    {
        string description = FindTool("archidekt_push_apply").ProtocolTool.Description!;

        Assert.Contains("reads Archidekt back and verifies each card", description, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the card-list query names the subtype and tag groupings and the exact mana tools name the questions
    /// they answer.
    /// </summary>
    [Fact]
    public void AnalysisTools_NameTheQuestionsTheyAnswer()
    {
        Assert.Contains("subtypes (Aura, Equipment) or tags", FindTool("card_list_query").ProtocolTool.Description!, StringComparison.Ordinal);
        foreach (string name in new[] { "stats_castability", "stats_mana_availability", "stats_simulate" })
        {
            Assert.Contains("land-count and on-curve", FindTool(name).ProtocolTool.Description!, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Verifies the category-rules apply tells the caller to repeat the preview's options.
    /// </summary>
    [Fact]
    public void CategoryRulesApply_SaysToRepeatSourceAndFreshnessPolicy()
    {
        McpServerTool tool = FindTool("local_deck_category_rules_apply");

        Assert.Contains("Repeat the source and freshnessPolicy of the preview", tool.ProtocolTool.Description!, StringComparison.Ordinal);
        Assert.Contains("Scryfall evidence policy: default, cache-only, or refresh.", tool.ProtocolTool.InputSchema.GetRawText(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the page-size descriptions state the default.
    /// </summary>
    [Theory]
    [InlineData("local_deck_list", "from 1 through 100; default 50.")]
    [InlineData("archidekt_deck_list", "from 1 through 100; default 50.")]
    [InlineData("scryfall_autocomplete", "from 1 through 100; default 25.")]
    [InlineData("scryfall_tags_search", "from 1 through 100; default 25.")]
    [InlineData("scryfall_card_details", "or through 25 with includeRaw; default 25.")]
    public void ListTools_StateTheirDefaultPageSize(string name, string expected)
    {
        string schema = FindTool(name).ProtocolTool.InputSchema.GetRawText();

        Assert.Contains(expected, schema, StringComparison.Ordinal);
    }

    /// <summary>
    /// Finds one registered tool in the full remote surface.
    /// </summary>
    private static McpServerTool FindTool(string name)
    {
        using RegisteredToolSet registered = new(OperationMode.Remote);
        return Assert.Single(registered.Tools, value => value.ProtocolTool.Name == name);
    }

    /// <summary>
    /// Counts the non-overlapping occurrences of a phrase.
    /// </summary>
    private static int Occurrences(string text, string phrase)
    {
        int count = 0;
        int index = text.IndexOf(phrase, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(phrase, index + phrase.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
