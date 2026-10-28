using System.Text;
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace MtgMcp.E2E.Tests;

/// <summary>
/// Measures the size of default results that can grow large, using real Scryfall data.
/// </summary>
public sealed class ResultSizeLiveTests
{
    /// <summary>
    /// Records the UTF-8 size of a 100-entry local deck read and a default 25-row search page.
    /// </summary>
    [Fact]
    [Trait("Category", "Live")]
    public async Task DefaultResults_ReportUtf8Sizes()
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            "local",
            "decks,scryfall",
            TestContext.Current.CancellationToken).ConfigureAwait(false);

        object[] entries = Enumerable.Range(0, 100)
            .Select(index => (object)new { quantity = 1, cardName = $"Fixture Card {index:D3}", zone = "main" })
            .ToArray();
        CallToolResult created = await session.Client.CallToolAsync(
            "deck_create",
            new Dictionary<string, object?> { ["request"] = new { name = "Size Fixture", format = "commander", entries } },
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
        Guid deckId = Assert.IsType<JsonElement>(created.StructuredContent).GetProperty("data").GetProperty("deckId").GetGuid();
        int deckSize = await MeasureAsync(
            session,
            "deck_get",
            new Dictionary<string, object?> { ["deckId"] = deckId }).ConfigureAwait(false);
        TestContext.Current.TestOutputHelper?.WriteLine($"deck_get 100 entries: {deckSize}");

        int searchSize = await MeasureAsync(
            session,
            "scryfall_search",
            new Dictionary<string, object?> { ["query"] = "set:dmu t:creature" }).ConfigureAwait(false);
        TestContext.Current.TestOutputHelper?.WriteLine($"scryfall_search 25 rows: {searchSize}");
        Assert.True(searchSize > 0 && deckSize > 0);
    }

    /// <summary>
    /// Calls one tool and returns the UTF-8 length of its structured result.
    /// </summary>
    private static async Task<int> MeasureAsync(
        McpProcessSession session,
        string tool,
        IReadOnlyDictionary<string, object?> arguments)
    {
        CallToolResult result = await session.Client.CallToolAsync(
            tool,
            arguments,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
        JsonElement structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("success", structured.GetProperty("kind").GetString());
        return Encoding.UTF8.GetByteCount(structured.GetRawText());
    }
}
