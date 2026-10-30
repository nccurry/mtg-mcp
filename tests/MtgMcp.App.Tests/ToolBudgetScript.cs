using System.Text.Json;
using System.Text.Json.Nodes;
using MtgMcp.App.Statistics;
using MtgMcp.Core.Decks;

namespace MtgMcp.App.Tests;

/// <summary>
/// Holds the ordered tool calls of the result size test and the names of the tools they cover. Reads come before the
/// writes that change what they read, and the writes that destroy data come last.
/// </summary>
internal sealed class ToolBudgetScript(ToolBudgetWorld world, CancellationToken token)
{
    /// <summary>
    /// Names the most pages the script reads from any paged tool, so a paging loop that never ends fails the test.
    /// </summary>
    private const int MaximumPages = 40;

    /// <summary>
    /// Names the number of characters a page aims to stay under, below the 50,000 at which Claude Code saves a result
    /// to a file.
    /// </summary>
    private const int PageTarget = 40_000;

    /// <summary>
    /// Lists every tool the script calls.
    /// </summary>
    internal static IReadOnlyList<string> CoveredTools { get; } =
    [
        "local_deck_list", "local_deck_get", "local_deck_validate", "local_deck_interchange_formats",
        "local_deck_export_bundle", "local_deck_import_preview", "local_deck_import_create",
        "local_deck_identity_reconcile_preview", "local_deck_identity_reconcile_apply",
        "local_deck_duplicates_preview", "local_deck_category_rules_preview", "local_deck_category_rules_apply",
        "local_deck_create", "local_deck_apply_changes", "local_deck_delete", "local_deck_backup_create",
        "local_deck_backup_list", "local_deck_backup_restore", "local_deck_backup_delete",
        "scryfall_search", "scryfall_cards_get", "scryfall_autocomplete", "scryfall_card_data_status",
        "scryfall_tags_search", "scryfall_installed_cards_by_tag", "scryfall_card_details", "scryfall_reference_get",
        "scryfall_request_snapshots_get", "scryfall_request_snapshot_delete", "scryfall_card_data_sync",
        "scryfall_card_data_rollback", "scryfall_card_data_delete", "card_list_query", "card_lists_compare",
        "stats_hypergeometric", "stats_multivariate", "stats_turn_table", "stats_mana_availability",
        "stats_package_assembly", "stats_mulligan", "stats_minimum_count", "stats_local_deck_summary",
        "stats_castability", "stats_simulate",
        "archidekt_auth_status", "archidekt_deck_list", "archidekt_deck_get", "archidekt_sync_diff",
        "archidekt_pull_preview", "archidekt_pull_apply", "archidekt_push_preview", "archidekt_push_apply",
        "archidekt_snapshot_restore_preview", "archidekt_folders_get", "archidekt_snapshots_get",
    ];

    /// <summary>
    /// Holds every outcome in call order.
    /// </summary>
    private readonly List<ToolBudgetWorld.ToolOutcome> outcomes = [];

    /// <summary>
    /// Runs the script and returns every outcome.
    /// </summary>
    internal async Task<List<ToolBudgetWorld.ToolOutcome>> RunAsync()
    {
        DeckDocument main = await world.CreateLocalDeckAsync("Budget deck", resolved: true, token);
        DeckDocument unresolved = await world.CreateLocalDeckAsync("Unresolved deck", resolved: false, token);
        DeckDocument small = await world.CreateLocalDeckAsync("Small deck", resolved: true, token, size: 99);
        ToolBudgetWorld.ToolOutcome firstBackup = await CallAsync("local_deck_backup_create", []);

        await ReadLocalDeckAsync(main);
        await ReadScryfallAsync(main);
        await ReadStatisticsAsync(main, small);
        await ReadAndWriteArchidektAsync();
        await WriteLocalDecksAsync(main, unresolved);
        await WriteScryfallAsync();
        await RestoreBackupAsync(firstBackup);
        return outcomes;
    }

    /// <summary>
    /// Calls one tool, records the outcome, and returns it.
    /// </summary>
    private async Task<ToolBudgetWorld.ToolOutcome> CallAsync(string tool, JsonObject arguments)
    {
        ToolBudgetWorld.ToolOutcome outcome = await world.CallAsync(tool, arguments, token);
        outcomes.Add(outcome);
        return outcome;
    }

    /// <summary>
    /// Calls a paged tool until its data holds no <paramref name="nextName"/>, passing the value back as
    /// <paramref name="passName"/>, and returns the last page. With <paramref name="pageCeiling"/>, every page must
    /// stay under that many characters.
    /// </summary>
    private async Task<ToolBudgetWorld.ToolOutcome> PageAsync(
        string tool,
        JsonObject arguments,
        string nextName,
        string passName,
        int pageCeiling = int.MaxValue)
    {
        for (int page = 0; page < MaximumPages; page++)
        {
            ToolBudgetWorld.ToolOutcome outcome = await CallAsync(tool, arguments);
            Assert.True(
                outcome.Characters < pageCeiling,
                $"{tool} page {page} is {outcome.Characters} characters, over {pageCeiling}.");
            JsonElement node = outcome.Data;
            foreach (string part in nextName.Split('.'))
            {
                if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(part, out node))
                {
                    return outcome;
                }
            }

            if (node.ValueKind == JsonValueKind.Null)
            {
                return outcome;
            }

            arguments[passName] = JsonNode.Parse(node.GetRawText());
        }

        Assert.Fail($"{tool} did not finish after {MaximumPages} pages.");
        return null!;
    }

    /// <summary>
    /// Reads every chunk of every artifact of one export.
    /// </summary>
    private async Task ExportAllAsync(Guid deckId, string format)
    {
        ToolBudgetWorld.ToolOutcome first = await CallAsync(
            "local_deck_export_bundle",
            new JsonObject { ["deckId"] = deckId.ToString(), ["formatId"] = format });
        foreach (JsonElement info in first.Data.GetProperty("artifacts").EnumerateArray().Skip(1))
        {
            await PageAsync(
                "local_deck_export_bundle",
                new JsonObject
                {
                    ["deckId"] = deckId.ToString(),
                    ["formatId"] = format,
                    ["artifact"] = info.GetProperty("fileName").GetString(),
                },
                "chunk.nextOffset",
                "offset");
        }

        JsonElement chunk = first.Data.GetProperty("chunk");
        if (chunk.TryGetProperty("nextOffset", out JsonElement next) && next.ValueKind == JsonValueKind.Number)
        {
            await PageAsync(
                "local_deck_export_bundle",
                new JsonObject { ["deckId"] = deckId.ToString(), ["formatId"] = format, ["offset"] = next.GetInt32() },
                "chunk.nextOffset",
                "offset");
        }
    }

    /// <summary>
    /// Calls a tool that pages two lists, operations and differences, until neither has a next page, and returns the
    /// last page.
    /// </summary>
    private async Task<ToolBudgetWorld.ToolOutcome> PageTwoListsAsync(string tool, JsonObject arguments)
    {
        for (int page = 0; page < MaximumPages; page++)
        {
            ToolBudgetWorld.ToolOutcome outcome = await CallAsync(tool, arguments);
            JsonElement data = outcome.Data;
            bool moreOperations = TryReadOffset(data, "nextOperationOffset", out int operations);
            bool moreDifferences = TryReadOffset(data, "nextDifferenceOffset", out int differences);
            if (!moreOperations && !moreDifferences)
            {
                return outcome;
            }

            // A page that is not the last carries both next offsets; a missing one is a defect, not a cue to guess.
            Assert.True(
                moreOperations && moreDifferences,
                $"{tool} left out a next offset on a page that is not the last.");
            arguments["operationOffset"] = operations;
            arguments["differenceOffset"] = differences;
        }

        Assert.Fail($"{tool} did not finish after {MaximumPages} pages.");
        return null!;
    }

    /// <summary>
    /// Reads a whole-number property that is present and not null.
    /// </summary>
    private static bool TryReadOffset(JsonElement data, string name, out int value)
    {
        value = 0;
        return data.TryGetProperty(name, out JsonElement element) &&
            element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt32(out value);
    }

    /// <summary>
    /// Reads the main deck every way: every include, every page, validation, export, duplicates, and rule previews.
    /// </summary>
    private async Task ReadLocalDeckAsync(DeckDocument main)
    {
        _ = await CallAsync("local_deck_list", []);
        foreach (string[] include in new[] { Array.Empty<string>(), ["cards"], ["tags"], ["cards", "tags"] })
        {
            JsonObject arguments = new() { ["deckId"] = main.DeckId.ToString() };
            if (include.Length > 0)
            {
                arguments["include"] = Strings(include);
            }

            await PageAsync("local_deck_get", arguments, "nextCursor", "cursor");
        }

        _ = await CallAsync(
            "local_deck_validate",
            new JsonObject { ["deckId"] = main.DeckId.ToString(), ["rules"] = "commander" });
        _ = await CallAsync("local_deck_interchange_formats", []);
        string[] formats = ["mtg-mcp-json-v1", "generic-text-v1", "archidekt-text-v1", "moxfield-bulk-edit-v1"];
        foreach (string format in formats)
        {
            await ExportAllAsync(main.DeckId, format);
        }

        _ = await CallAsync(
            "local_deck_duplicates_preview",
            new JsonObject
            {
                ["deckId"] = main.DeckId.ToString(),
                ["exempt"] = "none",
                ["keepPriority"] = new JsonArray(
                    new JsonObject { ["zone"] = "main" },
                    new JsonObject { ["category"] = "Ramp" }),
            });
    }

    /// <summary>
    /// Reads the card tools over the installed fixture cards.
    /// </summary>
    private async Task ReadScryfallAsync(DeckDocument main)
    {
        foreach (string format in new[] { "concise", "standard", "full" })
        {
            await PageAsync(
                "scryfall_search",
                new JsonObject { ["query"] = "name:budget", ["responseFormat"] = format },
                "nextCursor",
                "cursor");
        }

        JsonArray lookups = [];
        foreach (JsonObject card in world.Cards.Take(100))
        {
            lookups.Add(new JsonObject { ["kind"] = "exact-name", ["value"] = card["name"]!.GetValue<string>() });
        }

        await PageAsync(
            "scryfall_cards_get",
            new JsonObject
            {
                ["lookups"] = lookups,
                ["freshnessPolicy"] = "cache-only",
                ["responseFormat"] = "full",
                ["pageSize"] = 25,
            },
            "nextCursor",
            "cursor");
        _ = await CallAsync("scryfall_autocomplete", new JsonObject { ["query"] = "Budget" });
        _ = await CallAsync("scryfall_card_data_status", []);
        _ = await CallAsync("scryfall_tags_search", new JsonObject { ["query"] = "budget" });
        await PageAsync(
            "scryfall_installed_cards_by_tag",
            new JsonObject
            {
                ["tagIdentity"] = ToolBudgetWorld.TagSlug,
                ["tagType"] = "oracle",
                ["responseFormat"] = "full",
            },
            "nextCursor",
            "cursor");
        _ = await CallAsync(
            "scryfall_card_details",
            new JsonObject
            {
                ["lookup"] = new JsonObject { ["kind"] = "exact-name", ["value"] = "Budget Card 001" },
                ["include"] = Strings(["prints", "rulings"]),
            });
        _ = await CallAsync("scryfall_reference_get", new JsonObject { ["kind"] = "sets" });
        _ = await CallAsync(
            "scryfall_reference_get",
            new JsonObject { ["kind"] = "catalog", ["catalogName"] = "card-names" });
        _ = await CallAsync("scryfall_reference_get", new JsonObject { ["kind"] = "bulk" });
        _ = await CallAsync("scryfall_request_snapshots_get", new JsonObject { ["kind"] = "list" });
        JsonObject deck = new() { ["kind"] = "deck", ["deckId"] = main.DeckId.ToString() };
        await PageAsync(
            "card_list_query",
            new JsonObject
            {
                ["source"] = deck.DeepClone(),
                ["rows"] = new JsonObject
                {
                    ["fields"] = Strings(["name", "manaValue", "typeLine", "oracleText", "tags", "zone", "category"]),
                },
            },
            "nextCursor",
            "cursor");
        _ = await CallAsync(
            "card_list_query",
            new JsonObject
            {
                ["source"] = deck.DeepClone(),
                ["groupBy"] = Strings(["manaValue"]),
                ["metrics"] = Strings(["quantity"]),
            });
        await PageAsync(
            "card_lists_compare",
            new JsonObject
            {
                ["left"] = deck.DeepClone(),
                ["right"] = new JsonObject
                {
                    ["kind"] = "cards",
                    ["ids"] = Strings(
                        [.. world.Cards.Skip(150).Take(100).Select(card => card["id"]!.GetValue<string>())]),
                },
                ["operation"] = "union",
                ["responseFormat"] = "full",
            },
            "nextCursor",
            "cursor");
    }

    /// <summary>
    /// Calls the statistics tools: the raw examples, a deck summary, castability, and a simulation.
    /// </summary>
    private async Task ReadStatisticsAsync(DeckDocument main, DeckDocument small)
    {
        (string Tool, string Json)[] examples =
        [
            ("stats_hypergeometric", StatisticsToolExamples.Hypergeometric),
            ("stats_multivariate", StatisticsToolExamples.Multivariate),
            ("stats_turn_table", StatisticsToolExamples.TurnTable),
            ("stats_mana_availability", StatisticsToolExamples.ManaAvailability),
            ("stats_package_assembly", StatisticsToolExamples.PackageAssembly),
            ("stats_mulligan", StatisticsToolExamples.Mulligan),
            ("stats_minimum_count", StatisticsToolExamples.MinimumCount),
        ];
        foreach ((string tool, string json) in examples)
        {
            _ = await CallAsync(tool, JsonNode.Parse(json)!.AsObject());
        }

        _ = await CallAsync(
            "stats_local_deck_summary",
            new JsonObject
            {
                ["request"] = new JsonObject
                {
                    ["deckId"] = main.DeckId.ToString(),
                    ["expectedRevision"] = main.Revision,
                    ["populationSelectors"] = new JsonArray(
                        new JsonObject { ["kind"] = "zone-names", ["zoneNames"] = Strings(["main", "maybeboard"]) }),
                    ["numericSeries"] = new JsonArray(),
                    ["percentiles"] = new JsonArray(),
                },
            });
        string entry = main.Entries[0].EntryId.ToString();
        _ = await CallAsync(
            "stats_castability",
            new JsonObject
            {
                ["source"] = new JsonObject { ["kind"] = "deck", ["deckId"] = main.DeckId.ToString() },
                ["sources"] = new JsonArray(new JsonObject
                {
                    ["id"] = "rocks",
                    ["select"] = new JsonObject { ["kind"] = "entry-ids", ["ids"] = Strings([entry]) },
                    ["produces"] = new JsonObject { ["default"] = Strings(["C"]) },
                }),
                ["turn"] = new JsonObject { ["kind"] = "fixed", ["turn"] = 3 },
                ["onThePlay"] = true,
                ["requireLandDrops"] = false,
            });
        _ = await CallAsync(
            "stats_simulate",
            new JsonObject
            {
                ["deck"] = new JsonObject { ["deckId"] = small.DeckId.ToString() },
                ["groups"] = new JsonArray(new JsonObject
                {
                    ["id"] = "lands",
                    ["select"] = new JsonObject
                    {
                        ["kind"] = "entry-ids",
                        ["ids"] = Strings([small.Entries[0].EntryId.ToString()]),
                    },
                }),
                ["openingHand"] = new JsonObject { ["size"] = 7, ["onThePlay"] = true },
                ["eachTurn"] = new JsonArray(new JsonObject
                {
                    ["kind"] = "play",
                    ["group"] = "lands",
                    ["pick"] = "random",
                    ["maxPerTurn"] = 1,
                    ["manaFrom"] = "same-turn",
                }),
                ["turns"] = 2,
                ["conditions"] = new JsonArray(new JsonObject
                {
                    ["id"] = "two-mana",
                    ["check"] = new JsonObject { ["kind"] = "mana-at-least", ["amount"] = 1, ["turn"] = 2 },
                }),
                ["samples"] = 2_000,
                ["sampleHands"] = 2,
                ["seed"] = "0123456789abcdef",
            });
    }

    /// <summary>
    /// Reads the fake Archidekt every way, then pulls its 300-card deck, edits every card, and pushes the edit.
    /// </summary>
    private async Task ReadAndWriteArchidektAsync()
    {
        _ = await CallAsync("archidekt_auth_status", []);
        await PageAsync("archidekt_deck_list", [], "nextCursor", "cursor");
        await PageAsync("archidekt_deck_get", new JsonObject { ["deckId"] = "42" }, "nextOffset", "offset");
        await PageAsync("archidekt_folders_get", new JsonObject { ["kind"] = "list" }, "folders.nextOffset", "offset");
        await PageAsync(
            "archidekt_folders_get",
            new JsonObject { ["kind"] = "get", ["folderId"] = "9" },
            "folder.nextOffset",
            "offset");
        _ = await CallAsync("archidekt_snapshots_get", new JsonObject { ["kind"] = "list", ["deckId"] = "42" });
        await PageAsync(
            "archidekt_snapshots_get",
            new JsonObject { ["kind"] = "get", ["deckId"] = "42", ["snapshotId"] = "77" },
            "snapshot.deck.nextOffset",
            "offset");
        _ = await PageTwoListsAsync(
            "archidekt_snapshot_restore_preview",
            new JsonObject { ["deckId"] = "42", ["snapshotId"] = "77" });

        ToolBudgetWorld.ToolOutcome pull = await CallAsync(
            "archidekt_pull_preview",
            new JsonObject { ["remoteDeckId"] = "42" });
        JsonElement pulled = pull.Data;
        ToolBudgetWorld.ToolOutcome applied = await CallAsync(
            "archidekt_pull_apply",
            new JsonObject
            {
                ["request"] = new JsonObject
                {
                    ["remoteDeckId"] = "42",
                    ["localDeckId"] = null,
                    ["expectedLocalRevision"] = null,
                    ["expectedRemoteFingerprint"] = pulled.GetProperty("remoteFingerprint").GetString(),
                    ["previewFingerprint"] = pulled.GetProperty("previewFingerprint").GetString(),
                },
            });
        Guid localId = applied.Data.GetProperty("localDeckId").GetGuid();
        await PageAsync(
            "archidekt_pull_preview",
            new JsonObject { ["remoteDeckId"] = "42", ["localDeckId"] = localId.ToString() },
            "nextDifferenceOffset",
            "differenceOffset");

        DeckDocument local = Assert.IsType<MtgMcp.Core.Results.OperationSuccess<DeckDocument>>(
            (await world.Store.GetAsync(localId, token)).Value).Data;
        DeckDocument edited = Assert.IsType<MtgMcp.Core.Results.OperationSuccess<DeckDocument>>(
            (await world.Store.ApplyChangesAsync(
                localId,
                local.Revision,
                [.. local.Entries.Select(
                    value => new UpdateDeckEntryChange(value with { Quantity = value.Quantity + 1 }))],
                token)).Value).Data;
        await PreviewNativeImportAsync(localId);
        await PageAsync(
            "archidekt_sync_diff",
            new JsonObject { ["localDeckId"] = localId.ToString() },
            "nextDifferenceOffset",
            "differenceOffset");
        ToolBudgetWorld.ToolOutcome last = await PageTwoListsAsync(
            "archidekt_push_preview",
            new JsonObject { ["localDeckId"] = localId.ToString() });
        JsonElement preview = last.Data;
        _ = await CallAsync(
            "archidekt_push_apply",
            new JsonObject
            {
                ["request"] = new JsonObject
                {
                    ["localDeckId"] = localId.ToString(),
                    ["expectedLocalRevision"] = edited.Revision,
                    ["expectedRemoteFingerprint"] = preview.GetProperty("remoteFingerprint").GetString(),
                    ["previewFingerprint"] = preview.GetProperty("previewFingerprint").GetString(),
                },
            });
    }

    /// <summary>
    /// Previews importing the content in one format and reads every page, each under the 40,000 character target.
    /// </summary>
    private async Task PreviewImportAsync(string format, string content)
    {
        await PageAsync(
            "local_deck_import_preview",
            new JsonObject { ["formatId"] = format, ["content"] = content },
            "nextOffset",
            "offset",
            PageTarget);
    }

    /// <summary>
    /// Reads every chunk of the native export of a deck that is bound to Archidekt and categorized, joins them, and
    /// previews importing the joined document, so the proposal carries its categories, bindings, and sync snapshots.
    /// </summary>
    private async Task PreviewNativeImportAsync(Guid deckId)
    {
        ToolBudgetWorld.ToolOutcome first = await CallAsync(
            "local_deck_export_bundle",
            new JsonObject { ["deckId"] = deckId.ToString(), ["formatId"] = "mtg-mcp-json-v1" });
        string name = first.Data.GetProperty("artifacts")[0].GetProperty("fileName").GetString()!;
        System.Text.StringBuilder content = new();
        JsonObject arguments = new()
        {
            ["deckId"] = deckId.ToString(),
            ["formatId"] = "mtg-mcp-json-v1",
            ["artifact"] = name,
        };
        while (true)
        {
            ToolBudgetWorld.ToolOutcome chunk = await CallAsync("local_deck_export_bundle", arguments);
            JsonElement data = chunk.Data.GetProperty("chunk");
            content.Append(data.GetProperty("content").GetString());
            if (!TryReadOffset(data, "nextOffset", out int next))
            {
                break;
            }

            arguments["offset"] = next;
        }

        await PreviewImportAsync("mtg-mcp-json-v1", content.ToString());
    }

    /// <summary>
    /// Writes to the local decks: import, create, batch edit, identity and category rules, and delete.
    /// </summary>
    private async Task WriteLocalDecksAsync(DeckDocument main, DeckDocument unresolved)
    {
        string text = string.Join('\n', world.Cards.Take(300).Select(card => "1 " + card["name"]!.GetValue<string>()));
        JsonObject import = new() { ["formatId"] = "generic-text-v1", ["content"] = text };
        ToolBudgetWorld.ToolOutcome importPreview = await PageAsync(
            "local_deck_import_preview",
            import,
            "nextOffset",
            "offset");
        string[] names = [.. world.Cards.Take(300).Select(card => card["name"]!.GetValue<string>())];
        await PreviewImportAsync(
            "archidekt-text-v1",
            string.Join('\n', names.Select((name, index) => $"1 {name} `Budget Category {index:000}`")));
        await PreviewImportAsync(
            "moxfield-bulk-edit-v1",
            string.Join(
                '\n',
                names.Select((name, index) => $"1 {name} #Budget Tag {index % 7} #Second Tag {index % 11}")));
        _ = await CallAsync(
            "local_deck_import_create",
            new JsonObject
            {
                ["formatId"] = "generic-text-v1",
                ["content"] = text,
                ["expectedFingerprint"] = importPreview.Data.GetProperty("fingerprint").GetString(),
            });

        ToolBudgetWorld.ToolOutcome reconcile = await PageAsync(
            "local_deck_identity_reconcile_preview",
            new JsonObject
            {
                ["deckId"] = unresolved.DeckId.ToString(),
                ["expectedRevision"] = unresolved.Revision,
                ["entryIds"] = Strings(unresolved.Entries.Take(150).Select(value => value.EntryId.ToString())),
            },
            "nextOffset",
            "offset");
        _ = await CallAsync(
            "local_deck_identity_reconcile_apply",
            new JsonObject
            {
                ["deckId"] = unresolved.DeckId.ToString(),
                ["expectedRevision"] = unresolved.Revision,
                ["previewFingerprint"] = reconcile.Data.GetProperty("previewFingerprint").GetString(),
                ["applyToken"] = reconcile.Data.GetProperty("applyToken").GetString(),
            });

        JsonObject source = new()
        {
            ["kind"] = "inline",
            ["ruleSet"] = new JsonObject
            {
                ["assignmentMode"] = "add-only",
                ["rules"] = new JsonArray(new JsonObject
                {
                    ["categoryId"] = main.Categories.Single(value => value.Name == "Ramp").CategoryId.ToString(),
                    ["allOf"] = new JsonArray(
                        new JsonObject { ["tagType"] = "oracle", ["exactSlug"] = ToolBudgetWorld.TagSlug }),
                    ["anyOf"] = new JsonArray(),
                    ["noneOf"] = new JsonArray(),
                }),
            },
        };
        ToolBudgetWorld.ToolOutcome rules = await PageAsync(
            "local_deck_category_rules_preview",
            new JsonObject
            {
                ["deckId"] = main.DeckId.ToString(),
                ["expectedRevision"] = main.Revision,
                ["source"] = source.DeepClone(),
                ["freshnessPolicy"] = "cache-only",
            },
            "nextOffset",
            "offset");
        _ = await CallAsync(
            "local_deck_category_rules_apply",
            new JsonObject
            {
                ["deckId"] = main.DeckId.ToString(),
                ["expectedRevision"] = main.Revision,
                ["expectedCorpusGeneration"] = rules.Data.GetProperty("corpusGenerationId").GetString(),
                ["source"] = source.DeepClone(),
                ["freshnessPolicy"] = "cache-only",
                ["previewFingerprint"] = rules.Data.GetProperty("previewFingerprint").GetString(),
                ["applyToken"] = rules.Data.GetProperty("applyToken").GetString(),
            });

        JsonArray drafts = [];
        JsonArray additions = [];
        foreach (JsonObject card in world.Cards.Take(300))
        {
            drafts.Add(new JsonObject { ["quantity"] = 1, ["cardName"] = card["name"]!.GetValue<string>() });
            additions.Add(new JsonObject
            {
                ["kind"] = "add-entry",
                ["entryDraft"] = new JsonObject
                {
                    ["quantity"] = 1,
                    ["cardName"] = card["name"]!.GetValue<string>(),
                    ["zone"] = "maybeboard",
                },
            });
        }

        ToolBudgetWorld.ToolOutcome created = await CallAsync(
            "local_deck_create",
            new JsonObject
            {
                ["request"] = new JsonObject
                {
                    ["name"] = "Created by name",
                    ["format"] = "commander",
                    ["entries"] = drafts,
                },
            });
        Guid createdId = created.Data.GetProperty("deckId").GetGuid();
        long revision = created.Data.GetProperty("revision").GetInt64();
        ToolBudgetWorld.ToolOutcome edited = await CallAsync(
            "local_deck_apply_changes",
            new JsonObject
            {
                ["deckId"] = createdId.ToString(),
                ["expectedRevision"] = revision,
                ["changes"] = additions,
            });
        _ = await CallAsync(
            "local_deck_delete",
            new JsonObject
            {
                ["deckId"] = createdId.ToString(),
                ["expectedRevision"] = edited.Data.GetProperty("revision").GetInt64(),
            });
    }

    /// <summary>
    /// Runs the card data writes: a second sync, a rollback to the first, snapshot removal, and deletion last.
    /// </summary>
    private async Task WriteScryfallAsync()
    {
        _ = await CallAsync("scryfall_card_data_status", []);
        ToolBudgetWorld.ToolOutcome list = await CallAsync(
            "scryfall_request_snapshots_get",
            new JsonObject { ["kind"] = "list" });
        JsonElement first = list.Data.GetProperty("snapshots").GetProperty("items")[0];
        string snapshotId = first.GetProperty("snapshotId").GetString()!;
        _ = await CallAsync(
            "scryfall_request_snapshots_get",
            new JsonObject { ["kind"] = "get", ["snapshotId"] = snapshotId });
        _ = await CallAsync(
            "scryfall_request_snapshot_delete",
            new JsonObject
            {
                ["snapshotId"] = snapshotId,
                ["expectedChecksum"] = first.GetProperty("checksum").GetString(),
                ["acknowledgeDataLoss"] = true,
            });
        world.PublishNewerCardData();
        _ = await CallAsync("scryfall_card_data_sync", new JsonObject { ["metadataPolicy"] = "refresh" });
        ToolBudgetWorld.ToolOutcome after = await CallAsync("scryfall_card_data_status", []);
        JsonElement active = after.Data;
        _ = await CallAsync(
            "scryfall_card_data_rollback",
            new JsonObject
            {
                ["expectedActiveGeneration"] = active.GetProperty("active").GetProperty("generationId").GetString(),
                ["expectedPreviousGeneration"] = active.GetProperty("previous").GetProperty("generationId").GetString(),
                ["acknowledgeActivationChange"] = true,
            });
    }

    /// <summary>
    /// Lists the backups, restores the first, and removes both the first and the rollback backup.
    /// </summary>
    private async Task RestoreBackupAsync(ToolBudgetWorld.ToolOutcome firstBackup)
    {
        string backupId = firstBackup.Data.GetProperty("backupId").GetString()!;
        ToolBudgetWorld.ToolOutcome list = await CallAsync("local_deck_backup_list", []);
        ToolBudgetWorld.ToolOutcome restored = await CallAsync(
            "local_deck_backup_restore",
            new JsonObject
            {
                ["backupId"] = backupId,
                ["expectedDatabaseFingerprint"] = list.Data.GetProperty("currentDatabaseFingerprint").GetString(),
            });
        _ = await CallAsync(
            "local_deck_backup_delete",
            new JsonObject { ["backupId"] = restored.Data.GetProperty("rollbackBackupId").GetString() });
        _ = await CallAsync(
            "scryfall_card_data_delete",
            ScryfallDeleteArguments(await CallAsync("scryfall_card_data_status", [])));
    }

    /// <summary>
    /// Builds the arguments that delete the active card data.
    /// </summary>
    private static JsonObject ScryfallDeleteArguments(ToolBudgetWorld.ToolOutcome status)
    {
        return new JsonObject
        {
            ["expectedActiveGeneration"] = status.Data.GetProperty("active").GetProperty("generationId").GetString(),
            ["acknowledgeDataLoss"] = true,
        };
    }

    /// <summary>
    /// Builds a JSON array of strings.
    /// </summary>
    private static JsonArray Strings(IEnumerable<string> values)
    {
        JsonArray array = [];
        foreach (string value in values)
        {
            array.Add(value);
        }

        return array;
    }
}
