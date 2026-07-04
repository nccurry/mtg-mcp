using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace MtgMcp.E2E.Tests;

/// <summary>
/// Exercises a disposable Red/White Weenies workflow against the official Scryfall API.
/// </summary>
public sealed class RedWhiteWeeniesLiveMcpTests
{
    /// <summary>
    /// Defines the only color-identity symbols allowed by the test deck.
    /// </summary>
    private static readonly string[] BorosColors = ["W", "R"];

    /// <summary>
    /// Creates a sixty-card local deck and resolves every unique card through one bounded provider collection.
    /// </summary>
    [Fact]
    [Trait("Category", "Live")]
    public async Task LocalDeck_ResolvesCompleteRedWhiteWeeniesEvidence()
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            "local",
            "decks,scryfall",
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        (string Name, int Quantity)[] list =
        [
            ("Venerable Knight", 4),
            ("Recruitment Officer", 4),
            ("Hopeful Initiate", 4),
            ("Monastery Swiftspear", 4),
            ("Thalia, Guardian of Thraben", 4),
            ("Resolute Reinforcements", 4),
            ("Lightning Strike", 4),
            ("Play with Fire", 4),
            ("Wedding Announcement", 4),
            ("Plains", 8),
            ("Mountain", 8),
            ("Battlefield Forge", 8),
        ];
        object[] entries = list
            .Select((value, index) => (object)new
            {
                quantity = value.Quantity,
                cardName = value.Name,
                zone = "main",
                sortOrder = index,
            })
            .ToArray();
        JsonElement deck = await CallSuccessAsync(
            session,
            "deck_create",
            new Dictionary<string, object?>
            {
                ["request"] = new
                {
                    name = "Red White Weenies",
                    description = "Disposable live evidence workflow",
                    format = "modern",
                    entries,
                },
            }).ConfigureAwait(false);
        Guid deckId = deck.GetProperty("deckId").GetGuid();
        Assert.Equal(60, deck.GetProperty("entries").EnumerateArray().Sum(
            value => value.GetProperty("quantity").GetInt32()));

        JsonElement validation = await CallSuccessAsync(
            session,
            "deck_validate",
            new Dictionary<string, object?> { ["deckId"] = deckId }).ConfigureAwait(false);
        Assert.True(validation.GetProperty("isStructurallyValid").GetBoolean());
        Assert.Empty(validation.GetProperty("issues").EnumerateArray());

        object[] lookups = list
            .Select(value => (object)new { kind = "exact-name", value = value.Name })
            .ToArray();
        JsonElement collection = await CallSuccessAsync(
            session,
            "scryfall_card_collection",
            new Dictionary<string, object?>
            {
                ["lookups"] = lookups,
                ["freshnessPolicy"] = "refresh",
            }).ConfigureAwait(false);
        JsonElement.ArrayEnumerator rows = collection.GetProperty("page").GetProperty("items").EnumerateArray();
        JsonElement[] resolved = rows.ToArray();
        Assert.Equal(list.Length, resolved.Length);
        Assert.All(resolved, row => Assert.Equal("found", row.GetProperty("status").GetString()));
        Assert.All(
            resolved.Select(row => row.GetProperty("card")),
            card => Assert.All(
                card.GetProperty("colorIdentity").EnumerateArray(),
                color => Assert.Contains(color.GetString(), BorosColors)));
        Assert.All(
            resolved.SelectMany(row => row.GetProperty("card").GetProperty("priceEvidence").EnumerateArray()),
            evidence => Assert.Equal("scryfall-market-price", evidence.GetProperty("context").GetString()));
        Assert.NotEqual(Guid.Empty, collection.GetProperty("snapshot").GetProperty("snapshotId").GetGuid());

        JsonElement snapshots = await CallSuccessAsync(
            session,
            "scryfall_snapshot_list",
            new Dictionary<string, object?> { ["operation"] = "card-collection" }).ConfigureAwait(false);
        Assert.Single(snapshots.GetProperty("items").EnumerateArray());
        Assert.True(File.Exists(Path.Combine(session.DataRoot, "decks.db")));
        Assert.True(File.Exists(Path.Combine(session.DataRoot, "scryfall.db")));
    }

    /// <summary>
    /// Calls one MCP tool and extracts its successful structured payload.
    /// </summary>
    private static async Task<JsonElement> CallSuccessAsync(
        McpProcessSession session,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments)
    {
        CallToolResult result = await session.Client.CallToolAsync(
            toolName,
            arguments,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
        Assert.NotEqual(true, result.IsError);
        JsonElement structured = Assert.IsType<JsonElement>(result.StructuredContent);
        JsonElement operation = structured.GetProperty("result");
        Assert.Equal("success", operation.GetProperty("kind").GetString());
        return operation.GetProperty("data");
    }
}
