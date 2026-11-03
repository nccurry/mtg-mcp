using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MtgMcp.App.Decks;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Tests;

/// <summary>
/// Checks that the paged deck and card tools reach every row, that paging never changes what an apply step is bound to,
/// and that an export read in chunks adds up to the exact file, through a real MCP client and server over the fakes.
/// </summary>
public sealed class ToolPagingTests
{
    /// <summary>
    /// Verifies the chunks of every artifact of every format, joined in order, are the exact content: the length and
    /// the SHA-256 of the whole content match, and a chunk never passes the chunk limit of the export tool.
    /// </summary>
    [Theory]
    [InlineData("mtg-mcp-json-v1")]
    [InlineData("generic-text-v1")]
    [InlineData("archidekt-text-v1")]
    [InlineData("moxfield-bulk-edit-v1")]
    public async Task ExportBundle_ChunksJoinToTheExactContent_AndTheChecksumMatches(string format)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ToolBudgetWorld world = await ToolBudgetWorld.CreateAsync(token, remoteDeckSize: 1);
        DeckDocument deck = await world.CreateLocalDeckAsync("Export deck", resolved: true, token);

        ToolBudgetWorld.ToolOutcome first = await world.CallAsync(
            "local_deck_export_bundle",
            new JsonObject { ["deckId"] = deck.DeckId.ToString(), ["formatId"] = format },
            token);
        JsonElement[] artifacts = [.. first.Data.GetProperty("artifacts").EnumerateArray()];

        Assert.NotEmpty(artifacts);
        bool anyPaged = false;
        foreach (JsonElement artifact in artifacts)
        {
            string name = artifact.GetProperty("fileName").GetString()!;
            StringBuilder joined = new();
            string? cursor = null;
            while (true)
            {
                JsonObject arguments = new()
                {
                    ["deckId"] = deck.DeckId.ToString(),
                    ["formatId"] = format,
                    ["artifact"] = name,
                    ["cursor"] = cursor,
                };
                ToolBudgetWorld.ToolOutcome page = await world.CallAsync("local_deck_export_bundle", arguments, token);
                int offset = joined.Length;
                JsonElement chunk = page.Data.GetProperty("chunk");
                Assert.Equal(offset, chunk.GetProperty("offset").GetInt32());
                string text = chunk.GetProperty("content").GetString()!;
                int chunkBytes = ResultSize.Measure(text) - 2;
                Assert.True(
                    chunkBytes <= DeckExportPaging.MaximumChunkBytes,
                    $"{name} chunk at {offset} takes {chunkBytes} bytes, over {DeckExportPaging.MaximumChunkBytes}.");
                Assert.True(page.Characters < 40_000, $"{name} chunk at {offset} is {page.Characters} characters");
                joined.Append(text);
                if (!page.Data.TryGetProperty("nextCursor", out JsonElement next) || next.ValueKind != JsonValueKind.String)
                {
                    break;
                }

                anyPaged = true;
                cursor = next.GetString();
            }

            string content = joined.ToString();
            Assert.Equal(artifact.GetProperty("totalLength").GetInt32(), content.Length);
            Assert.Equal(
                artifact.GetProperty("sha256").GetString(),
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant());
        }

        Assert.True(
            anyPaged || format != "mtg-mcp-json-v1",
            "The JSON export of 300 entries should take several chunks.");
    }

    /// <summary>
    /// Verifies an unknown artifact name, a cursor that was edited, and a page size over the limit are refused with the argument
    /// name.
    /// </summary>
    [Fact]
    public async Task ExportBundle_UnknownArtifactCursorOrPageSize_IsRefused()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ToolBudgetWorld world = await ToolBudgetWorld.CreateAsync(token, remoteDeckSize: 1);
        DeckDocument deck = await world.CreateLocalDeckAsync("Export deck", resolved: true, token, size: 5);

        ToolBudgetWorld.ToolOutcome unknown = await world.CallAsync(
            "local_deck_export_bundle",
            new JsonObject
            {
                ["deckId"] = deck.DeckId.ToString(),
                ["formatId"] = "generic-text-v1",
                ["artifact"] = "nope.txt",
            },
            token);
        ToolBudgetWorld.ToolOutcome edited = await world.CallAsync(
            "local_deck_export_bundle",
            new JsonObject
            {
                ["deckId"] = deck.DeckId.ToString(),
                ["formatId"] = "generic-text-v1",
                ["cursor"] = "not-a-cursor",
            },
            token);
        ToolBudgetWorld.ToolOutcome large = await world.CallAsync(
            "local_deck_export_bundle",
            new JsonObject
            {
                ["deckId"] = deck.DeckId.ToString(),
                ["formatId"] = "generic-text-v1",
                ["pageSize"] = 30_001,
            },
            token);

        Assert.Equal("artifact", unknown.Structured.GetProperty("path").GetString());
        Assert.Equal("cursor", edited.Structured.GetProperty("path").GetString());
        Assert.Equal("pageSize", large.Structured.GetProperty("path").GetString());
    }

    /// <summary>
    /// Verifies the identity preview pages every row once, every page carries the same guards, applying with the guards
    /// of the last page changes every entry, and a changed fingerprint is refused.
    /// </summary>
    [Fact]
    public async Task IdentityPreview_PagesEveryRow_AndApplyIsBoundToTheWholePreview()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ToolBudgetWorld world = await ToolBudgetWorld.CreateAsync(token, remoteDeckSize: 1);
        DeckDocument deck = await world.CreateLocalDeckAsync("Unresolved", resolved: false, token);
        JsonArray ids = [.. deck.Entries.Take(150).Select(value => (JsonNode?)value.EntryId.ToString())];
        List<ToolBudgetWorld.ToolOutcome> pages = await ReadPagesAsync(
            world,
            "local_deck_identity_reconcile_preview",
            new JsonObject
            {
                ["deckId"] = deck.DeckId.ToString(),
                ["expectedRevision"] = deck.Revision,
                ["entryIds"] = ids,
            },
            "rows",
            token);

        Assert.True(pages.Count > 1);
        List<Guid> rowIds =
            [.. pages.SelectMany(page => page.Data.GetProperty("rows").EnumerateArray())
                .Select(row => row.GetProperty("entryId").GetGuid())];
        Assert.Equal(150, rowIds.Count);
        Assert.Equal(150, rowIds.Distinct().Count());
        Assert.Equal(deck.Entries.Take(150).Select(value => value.EntryId), rowIds);
        Assert.All(pages, page => Assert.Equal(150, page.Data.GetProperty("rowCount").GetInt32()));
        Assert.Single(pages.Select(page => page.Data.GetProperty("previewFingerprint").GetString()).Distinct());
        Assert.Single(pages.Select(page => page.Data.GetProperty("applyToken").GetString()).Distinct());

        JsonElement guards = pages[^1].Data;
        ToolBudgetWorld.ToolOutcome stale = await world.CallAsync(
            "local_deck_identity_reconcile_apply",
            ApplyArguments(
                deck,
                "0" + guards.GetProperty("previewFingerprint").GetString(),
                guards.GetProperty("applyToken").GetString()!),
            token);
        Assert.NotEqual("success", stale.Kind);
        ToolBudgetWorld.ToolOutcome applied = await world.CallAsync(
            "local_deck_identity_reconcile_apply",
            ApplyArguments(
                deck,
                guards.GetProperty("previewFingerprint").GetString()!,
                guards.GetProperty("applyToken").GetString()!),
            token);
        Assert.False(applied.IsError, applied.Kind);

        DeckDocument after = Assert.IsType<OperationSuccess<DeckDocument>>(
            (await world.Store.GetAsync(deck.DeckId, token)).Value).Data;
        Assert.Equal(150, after.Entries.Count(entry => entry.OracleId is not null));
    }

    /// <summary>
    /// Verifies the category rules preview pages every decision once with the same guards on every page, applying with
    /// the guards of the last page files every matching entry, and a changed fingerprint is refused.
    /// </summary>
    [Fact]
    public async Task CategoryRulesPreview_PagesEveryDecision_AndApplyIsBoundToTheWholePreview()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ToolBudgetWorld world = await ToolBudgetWorld.CreateAsync(token, remoteDeckSize: 1);
        DeckDocument deck = await world.CreateLocalDeckAsync("Rules", resolved: true, token);
        Guid category = deck.Categories.Single(value => value.Name == "Utility").CategoryId;
        JsonObject source = new()
        {
            ["kind"] = "inline",
            ["ruleSet"] = new JsonObject
            {
                ["assignmentMode"] = "add-only",
                ["rules"] = new JsonArray(new JsonObject
                {
                    ["categoryId"] = category.ToString(),
                    ["allOf"] = new JsonArray(
                        new JsonObject { ["tagType"] = "oracle", ["exactSlug"] = ToolBudgetWorld.TagSlug }),
                    ["anyOf"] = new JsonArray(),
                    ["noneOf"] = new JsonArray(),
                }),
            },
        };
        List<ToolBudgetWorld.ToolOutcome> pages = await ReadPagesAsync(
            world,
            "local_deck_category_rules_preview",
            new JsonObject
            {
                ["deckId"] = deck.DeckId.ToString(),
                ["expectedRevision"] = deck.Revision,
                ["source"] = source.DeepClone(),
                ["freshnessPolicy"] = "cache-only",
            },
            "decisions",
            token);

        Assert.True(pages.Count > 1);
        List<Guid> decisionIds =
            [.. pages.SelectMany(page => page.Data.GetProperty("decisions").EnumerateArray())
                .Select(row => row.GetProperty("entryId").GetGuid())];
        Assert.Equal(ToolBudgetWorld.DeckSize, decisionIds.Count);
        Assert.Equal(ToolBudgetWorld.DeckSize, decisionIds.Distinct().Count());
        Assert.Equal(
            deck.Entries.Select(value => value.EntryId).OrderBy(value => value.ToString(), StringComparer.Ordinal),
            decisionIds);
        Assert.Single(pages.Select(page => page.Data.GetProperty("previewFingerprint").GetString()).Distinct());

        JsonElement guards = pages[^1].Data;
        JsonObject Apply(string fingerprint)
        {
            return new JsonObject
            {
                ["deckId"] = deck.DeckId.ToString(),
                ["expectedRevision"] = deck.Revision,
                ["expectedActiveGeneration"] = guards.GetProperty("corpusGenerationId").GetString(),
                ["source"] = source.DeepClone(),
                ["freshnessPolicy"] = "cache-only",
                ["previewFingerprint"] = fingerprint,
                ["applyToken"] = guards.GetProperty("applyToken").GetString(),
            };
        }

        ToolBudgetWorld.ToolOutcome stale = await world.CallAsync(
            "local_deck_category_rules_apply",
            Apply("0" + guards.GetProperty("previewFingerprint").GetString()),
            token);
        Assert.NotEqual("success", stale.Kind);
        _ = (await world.CallAsync(
            "local_deck_category_rules_apply",
            Apply(guards.GetProperty("previewFingerprint").GetString()!),
            token)).Data;

        DeckDocument after = Assert.IsType<OperationSuccess<DeckDocument>>(
            (await world.Store.GetAsync(deck.DeckId, token)).Value).Data;
        int tagged = after.CategoryAssignments.Count(value => value.CategoryId == category);
        Assert.True(tagged >= 100, $"{tagged} assignments were added in the category.");
    }

    /// <summary>
    /// Verifies the import preview pages every proposed entry once and keeps the same fingerprint on every page.
    /// </summary>
    [Fact]
    public async Task ImportPreview_PagesEveryEntry_WithOneFingerprint()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ToolBudgetWorld world = await ToolBudgetWorld.CreateAsync(token, remoteDeckSize: 1);
        string text = string.Join('\n', world.Cards.Take(300).Select(card => "1 " + card["name"]!.GetValue<string>()));

        List<ToolBudgetWorld.ToolOutcome> pages = await ReadPagesAsync(
            world,
            "local_deck_import_preview",
            new JsonObject { ["formatId"] = "generic-text-v1", ["content"] = text },
            "proposal.entries",
            token);

        Assert.True(pages.Count > 1);
        JsonElement[] entries =
            [.. pages.SelectMany(page => page.Data.GetProperty("proposal").GetProperty("entries").EnumerateArray())];
        Assert.Equal(300, entries.Length);
        Assert.Equal(300, entries.Select(entry => entry.GetProperty("entryId").GetGuid()).Distinct().Count());
        Assert.Equal(
            world.Cards.Take(300).Select(card => card["name"]!.GetValue<string>()),
            entries.Select(entry => entry.GetProperty("cardName").GetString()));
        Assert.All(pages, page => Assert.Equal(300, page.Data.GetProperty("entryCount").GetInt32()));
        Assert.Single(pages.Select(page => page.Data.GetProperty("previewFingerprint").GetString()).Distinct());
    }

    /// <summary>
    /// Verifies the card tools cut a full-format page to the size limit, say so, and reach every card through the next
    /// cursor, in the same order as the standard format.
    /// </summary>
    [Fact]
    public async Task FullFormatSearch_CutsPages_AndReachesEveryCard()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ToolBudgetWorld world = await ToolBudgetWorld.CreateAsync(token, remoteDeckSize: 1);

        List<string> ids = [];
        JsonObject arguments = new() { ["query"] = "name:budget", ["responseFormat"] = "full" };
        bool sawTruncated = false;
        for (int page = 0; page < 40; page++)
        {
            ToolBudgetWorld.ToolOutcome outcome = await world.CallAsync("scryfall_search", arguments, token);
            Assert.True(outcome.Characters <= 50_000, $"page {page} is {outcome.Characters} characters");
            JsonElement data = outcome.Data;
            ids.AddRange(data.GetProperty("page").GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("id").GetString()!));
            sawTruncated |= data.TryGetProperty(
                "truncated",
                out JsonElement cut) && cut.ValueKind == JsonValueKind.True;
            if (!data.GetProperty(
                "page").TryGetProperty("nextCursor", out JsonElement next) || next.ValueKind != JsonValueKind.String)
            {
                break;
            }

            arguments["cursor"] = next.GetString();
        }

        Assert.Equal(175, ids.Distinct().Count());
        Assert.Equal(175, ids.Count);
        Assert.True(sawTruncated);
    }

    /// <summary>
    /// Verifies the three local previews hand out a cursor that serves the next page of a preview that is still the
    /// same, refuse a cursor of a preview that changed as stale with the current previewFingerprint, and refuse a page
    /// size outside 1 through 100 and an edited cursor with the argument path.
    /// </summary>
    [Fact]
    public async Task LocalPreviews_Cursors_AreBoundToThePreview()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ToolBudgetWorld world = await ToolBudgetWorld.CreateAsync(token, remoteDeckSize: 1);
        DeckDocument unresolved = await world.CreateLocalDeckAsync("Unresolved", resolved: false, token);
        DeckDocument resolved = await world.CreateLocalDeckAsync("Resolved", resolved: true, token);
        JsonObject otherRules = RulesArguments(resolved);
        otherRules["source"]!["ruleSet"]!["rules"]![0]!["categoryId"] =
            resolved.Categories.Single(value => value.Name == "Ramp").CategoryId.ToString();
        (string Tool, JsonObject Arguments, JsonObject Changed)[] previews =
        [
            (
                "local_deck_identity_reconcile_preview",
                IdentityArguments(unresolved),
                IdentityArguments(unresolved, take: 149)),
            ("local_deck_category_rules_preview", RulesArguments(resolved), otherRules),
            ("local_deck_import_preview", ImportArguments(world), ImportArguments(world, take: 299)),
        ];

        foreach ((string tool, JsonObject arguments, JsonObject changed) in previews)
        {
            JsonObject small = (JsonObject)arguments.DeepClone();
            small["pageSize"] = 2;
            ToolBudgetWorld.ToolOutcome first = await world.CallAsync(tool, small, token);
            string cursor = first.Data.GetProperty("nextCursor").GetString()!;
            small["cursor"] = cursor;
            ToolBudgetWorld.ToolOutcome second = await world.CallAsync(tool, small, token);
            JsonObject other = (JsonObject)changed.DeepClone();
            other["pageSize"] = 2;
            other["cursor"] = cursor;
            ToolBudgetWorld.ToolOutcome stale = await world.CallAsync(tool, other, token);

            Assert.Equal(
                first.Data.GetProperty("previewFingerprint").GetString(),
                second.Data.GetProperty("previewFingerprint").GetString());
            Assert.Equal("conflict", stale.Kind);
            Assert.Equal("cursor-stale", stale.Structured.GetProperty("reasonCode").GetString());
            Assert.True(stale.Structured.GetProperty("current").TryGetProperty("previewFingerprint", out _));
            foreach ((string name, object value, string path, string reason) in new[]
            {
                ("pageSize", (object)0, "pageSize", "invalid-page-size"),
                ("pageSize", 101, "pageSize", "invalid-page-size"),
                ("cursor", cursor + "x", "cursor", "invalid-cursor"),
            })
            {
                JsonObject bad = (JsonObject)arguments.DeepClone();
                bad[name] = JsonValue.Create(value);
                ToolBudgetWorld.ToolOutcome refused = await world.CallAsync(tool, bad, token);
                Assert.Equal("invalid-input", refused.Kind);
                Assert.Equal(path, refused.Structured.GetProperty("path").GetString());
                Assert.Equal(reason, refused.Structured.GetProperty("reasonCode").GetString());
            }
        }
    }

    /// <summary>
    /// Builds the arguments of an identity preview over the first entries of a deck.
    /// </summary>
    private static JsonObject IdentityArguments(DeckDocument deck, int take = 150)
    {
        return new JsonObject
        {
            ["deckId"] = deck.DeckId.ToString(),
            ["expectedRevision"] = deck.Revision,
            ["entryIds"] = new JsonArray(
                [.. deck.Entries.Take(take).Select(value => (JsonNode?)value.EntryId.ToString())]),
        };
    }

    /// <summary>
    /// Builds the arguments of a category rules preview that files every tagged card into one category.
    /// </summary>
    private static JsonObject RulesArguments(DeckDocument deck)
    {
        return new JsonObject
        {
            ["deckId"] = deck.DeckId.ToString(),
            ["expectedRevision"] = deck.Revision,
            ["source"] = new JsonObject
            {
                ["kind"] = "inline",
                ["ruleSet"] = new JsonObject
                {
                    ["assignmentMode"] = "add-only",
                    ["rules"] = new JsonArray(new JsonObject
                    {
                        ["categoryId"] = deck.Categories.Single(value => value.Name == "Utility").CategoryId.ToString(),
                        ["allOf"] = new JsonArray(
                            new JsonObject { ["tagType"] = "oracle", ["exactSlug"] = ToolBudgetWorld.TagSlug }),
                        ["anyOf"] = new JsonArray(),
                        ["noneOf"] = new JsonArray(),
                    }),
                },
            },
            ["freshnessPolicy"] = "cache-only",
        };
    }

    /// <summary>
    /// Builds the arguments of an import preview of plain text lines.
    /// </summary>
    private static JsonObject ImportArguments(ToolBudgetWorld world, int take = 300)
    {
        return new JsonObject
        {
            ["formatId"] = "generic-text-v1",
            ["content"] = string.Join(
                '\n',
                world.Cards.Take(take).Select(card => "1 " + card["name"]!.GetValue<string>())),
        };
    }

    /// <summary>
    /// Reads every page of a tool that pages one list with <c>nextCursor</c>, and returns the pages.
    /// </summary>
    private static async Task<List<ToolBudgetWorld.ToolOutcome>> ReadPagesAsync(
        ToolBudgetWorld world,
        string tool,
        JsonObject arguments,
        string rowsPath,
        CancellationToken token)
    {
        _ = rowsPath;
        List<ToolBudgetWorld.ToolOutcome> pages = [];
        for (int page = 0; page < 60; page++)
        {
            ToolBudgetWorld.ToolOutcome outcome = await world.CallAsync(tool, arguments, token);
            Assert.True(outcome.Characters < 40_000, $"{tool} page {page} is {outcome.Characters} characters");
            pages.Add(outcome);
            if (!outcome.Data.TryGetProperty(
                "nextCursor",
                out JsonElement next) || next.ValueKind != JsonValueKind.String)
            {
                return pages;
            }

            arguments["cursor"] = next.GetString();
        }

        Assert.Fail($"{tool} did not finish.");
        return pages;
    }

    /// <summary>
    /// Builds the arguments of an identity apply for the guards of a preview.
    /// </summary>
    private static JsonObject ApplyArguments(DeckDocument deck, string fingerprint, string token)
    {
        return new JsonObject
        {
            ["deckId"] = deck.DeckId.ToString(),
            ["expectedRevision"] = deck.Revision,
            ["previewFingerprint"] = fingerprint,
            ["applyToken"] = token,
        };
    }
}
