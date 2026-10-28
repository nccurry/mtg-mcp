using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using MtgMcp.App.Capabilities;
using MtgMcp.App.Configuration;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Tests;

/// <summary>
/// Checks the rules that every tool, or every merged tool, must follow: the four merge rules, closed input
/// schemas, source-first descriptions, untrusted-text notes, confirmation metadata, and secret-free results.
/// </summary>
public sealed class ToolSurfaceRulesTests : IClassFixture<ToolCatalogFixture>
{
    /// <summary>
    /// Holds the argument catalog of one full registration.
    /// </summary>
    private readonly ToolArgumentCatalog catalog;

    /// <summary>
    /// Takes the catalog the fixture built, so the policy can look tools up by name.
    /// </summary>
    public ToolSurfaceRulesTests(ToolCatalogFixture fixture)
    {
        catalog = fixture.Catalog;
    }

    /// <summary>
    /// Names each merged tool with what the rules say about it: whether it reaches providers, which fields are
    /// required, which field picks the job, and which fields apply to only some choices.
    /// </summary>
    private static readonly MergedTool[] MergedTools =
    [
        new("scryfall_cards_get", true, ["lookups"], null, []),
        new("scryfall_card_details", true, ["include", "lookup"], "include", ["printsCursor", "rulingsCursor"]),
        new("scryfall_reference_get", true, ["kind"], "kind",
            ["codeOrId", "catalogName", "includeRaw", "cursor", "pageSize"]),
        new("scryfall_search_snapshots_get", false, ["kind"], "kind",
            ["snapshotId", "operation", "retrievedAfterUtc", "retrievedBeforeUtc", "includeRaw"]),
        new("archidekt_folders_get", true, ["kind"], "kind", ["folderId"]),
        new("archidekt_snapshots_get", true, ["deckId", "kind"], "kind", ["snapshotId"]),
    ];

    /// <summary>
    /// Names the tools whose descriptions must say that provider text is untrusted data.
    /// </summary>
    private static readonly string[] UntrustedTextTools =
    [
        "archidekt_deck_list", "archidekt_deck_get", "archidekt_folders_get", "archidekt_snapshots_get",
        "archidekt_sync_diff", "archidekt_pull_preview", "archidekt_push_preview",
        "archidekt_snapshot_restore_preview", "archidekt_push_apply", "archidekt_snapshot_restore_apply",
        "archidekt_deck_create", "archidekt_folder_create",
        "archidekt_folder_update", "archidekt_snapshot_create", "archidekt_snapshot_update",
        "playgroup_me_get", "playgroup_commander_get", "playgroup_commander_get_by_name",
        "playgroup_commander_turn_damage_get", "playgroup_deck_get", "playgroup_deck_elo_history_get",
        "playgroup_user_get", "playgroup_user_decks_list", "playgroup_user_playgroups_list",
        "playgroup_user_playgroup_get", "playgroup_playgroup_members_list", "playgroup_playgroup_games_list",
        "playgroup_playgroup_game_get", "playgroup_game_events_batch_create", "playgroup_live_session_create",
        "spellbook_variant_search", "spellbook_variant_get", "spellbook_deck_combos_find",
    ];

    /// <summary>
    /// Names the destructive remote writes that ask the client to confirm with the user.
    /// </summary>
    private static readonly string[] ConfirmationTools =
    [
        "archidekt_deck_delete", "archidekt_folder_delete", "archidekt_folder_move_items", "archidekt_push_apply",
        "archidekt_snapshot_delete", "archidekt_snapshot_restore_apply", "playgroup_game_events_batch_create",
    ];

    /// <summary>
    /// Lists the leading words each description may start with, one per data source.
    /// </summary>
    private static readonly string[] SourceLabels =
    [
        "Local deck store:", "Scryfall:", "Saved Scryfall requests:", "Installed Scryfall card data:", "Exact math:",
        "Archidekt:",
        "Playgroup:", "Commander Spellbook:",
    ];

    /// <summary>
    /// Verifies every merged tool follows the four merge rules (SURF-003): the annotations and visibility of the
    /// tools it replaced, one required field that picks the job, fields marked as applying to only some choices, and
    /// one result shape, or a result union keyed by the same choice.
    /// </summary>
    [Fact]
    public void MergedTools_FollowTheFourMergeRules()
    {
        using RegisteredToolSet remote = new(OperationMode.Remote);
        using RegisteredToolSet readOnly = new(OperationMode.ReadOnly);
        IReadOnlyDictionary<string, MethodInfo> methods = RegisteredTools.Methods();

        foreach (MergedTool merged in MergedTools)
        {
            McpServerTool tool = Find(remote, merged.Name);
            JsonElement schema = tool.ProtocolTool.InputSchema;

            // Rule 1: the replaced tools were all non-destructive, idempotent reads.
            Assert.True(tool.ProtocolTool.Annotations!.ReadOnlyHint, merged.Name);
            Assert.False(tool.ProtocolTool.Annotations.DestructiveHint, merged.Name);
            Assert.True(tool.ProtocolTool.Annotations.IdempotentHint, merged.Name);
            Assert.Equal(merged.OpenWorld, tool.ProtocolTool.Annotations.OpenWorldHint);

            // Rule 2: reads are visible in every mode.
            Assert.Contains(readOnly.Tools, value => value.ProtocolTool.Name == merged.Name);

            // Rule 3: required fields, a published choice, and fields that say when they apply.
            string[] required = [.. schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()!)
                .Order(StringComparer.Ordinal)];
            Assert.Equal(merged.Required, required);
            string[]? choices = merged.ChoiceField is null ? null : ReadChoices(schema, merged.ChoiceField);
            if (merged.ChoiceField is not null)
            {
                Assert.NotEmpty(choices!);
            }

            foreach (string field in merged.ConditionalFields)
            {
                string description = schema.GetProperty("properties").GetProperty(field)
                    .GetProperty("description").GetString()!;
                Assert.True(
                    description.StartsWith("Only with", StringComparison.Ordinal),
                    $"{merged.Name}.{field}: {description}");
            }

            // Rule 4: one result type, or a union keyed by the same choice.
            Type result = ResultDataType(methods[merged.Name]);
            JsonPolymorphicAttribute? union = result.GetCustomAttribute<JsonPolymorphicAttribute>();
            if (merged.ChoiceField == "kind" && union is not null)
            {
                Assert.Equal("kind", union.TypeDiscriminatorPropertyName);
                string[] kinds = [.. result.GetCustomAttributes<JsonDerivedTypeAttribute>()
                    .Select(derived => (string)derived.TypeDiscriminator!)
                    .Order(StringComparer.Ordinal)];
                Assert.Equal(choices!.Order(StringComparer.Ordinal), kinds);
            }
            else
            {
                Assert.Null(union);
            }
        }
    }

    /// <summary>
    /// Verifies the merged tools that return a union are exactly the ones whose kind picks the data they read.
    /// </summary>
    [Fact]
    public void MergedTools_UnionResults_AreTheKindToolsThatReadDifferentData()
    {
        IReadOnlyDictionary<string, MethodInfo> methods = RegisteredTools.Methods();

        string[] unions = [.. MergedTools
            .Where(merged => ResultDataType(methods[merged.Name])
                .GetCustomAttribute<JsonPolymorphicAttribute>() is not null)
            .Select(merged => merged.Name)];

        Assert.Equal(
            [
                "scryfall_reference_get",
                "scryfall_search_snapshots_get",
                "archidekt_folders_get",
                "archidekt_snapshots_get",
            ],
            unions);
    }

    /// <summary>
    /// Verifies every tool's input schema rejects unknown properties and the call filter names an unknown argument
    /// (SCHEMA-004).
    /// </summary>
    [Fact]
    public void EveryTool_RejectsAnUnknownArgumentByName()
    {
        using RegisteredToolSet remote = new(OperationMode.Remote);
        Dictionary<string, JsonElement> unknown = new()
        {
            ["unknownField"] = JsonSerializer.SerializeToElement(1),
        };

        foreach (McpServerTool tool in remote.Tools)
        {
            string name = tool.ProtocolTool.Name;
            Assert.Equal(
                JsonValueKind.False,
                tool.ProtocolTool.InputSchema.GetProperty("additionalProperties").ValueKind);
            OperationInvalidInput failure = Assert.IsType<OperationInvalidInput>(
                ToolCallPolicy.FindUnknownArgument(catalog, name, unknown));
            Assert.Equal("unknownField", failure.Path);
            Assert.Contains("'unknownField'", failure.Message, StringComparison.Ordinal);
            Assert.Equal(ToolCallPolicy.InvalidArgumentsReason, failure.ReasonCode);
        }
    }

    /// <summary>
    /// Verifies known arguments and no arguments pass the unknown-argument check, a tool missing from the catalog
    /// fails loudly, and a blank or very long name is reported safely.
    /// </summary>
    [Fact]
    public void FindUnknownArgument_KnownMissingAndOddNames_AreHandled()
    {
        Dictionary<string, JsonElement> known = new()
        {
            ["deckId"] = JsonSerializer.SerializeToElement("x"),
        };
        Dictionary<string, JsonElement> blank = new() { [" "] = JsonSerializer.SerializeToElement(1) };
        Dictionary<string, JsonElement> longName = new()
        {
            [new string('a', 200)] = JsonSerializer.SerializeToElement(1),
        };

        Assert.Null(ToolCallPolicy.FindUnknownArgument(catalog, "local_deck_get", known));
        Assert.Null(ToolCallPolicy.FindUnknownArgument(catalog, "local_deck_get", null));
        Assert.Null(ToolCallPolicy.FindUnknownArgument(
            catalog, "local_deck_get", new Dictionary<string, JsonElement>()));
        Assert.Throws<ArgumentException>(() => ToolCallPolicy.FindUnknownArgument(catalog, "not_a_tool", blank));
        OperationInvalidInput blankFailure = Assert.IsType<OperationInvalidInput>(
            ToolCallPolicy.FindUnknownArgument(catalog, "local_deck_get", blank));
        OperationInvalidInput longFailure = Assert.IsType<OperationInvalidInput>(
            ToolCallPolicy.FindUnknownArgument(catalog, "local_deck_get", longName));
        Assert.Null(blankFailure.Path);
        Assert.True(longFailure.Path!.Length < 100);
        Assert.Contains("Accepted arguments: ", longFailure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a misspelled field inside a nested object is rejected with its path, not ignored (SCHEMA-004).
    /// </summary>
    [Fact]
    public void DiagnoseArguments_UnknownNestedProperty_NamesPathAndField()
    {
        Dictionary<string, JsonElement> arguments = Arguments(
            """
            {
              "deckId": "0193e97a-ef95-7700-8000-000000000001",
              "expectedRevision": 1,
              "changes": [
                { "kind": "add-entry", "entryDraft": { "quantity": 1, "cardName": "Sol Ring", "quantiy": 2 } }
              ]
            }
            """);

        OperationInvalidInput? failure = ToolCallPolicy.DiagnoseArguments(
            catalog, "local_deck_apply_changes", arguments);

        Assert.NotNull(failure);
        Assert.Equal("changes[0].entryDraft.quantiy", failure.Path);
        Assert.Contains("quantiy", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the removed category preset is refused with the one allowed source kind (FLOW-003).
    /// </summary>
    [Fact]
    public void DiagnoseArguments_PresetCategoryRuleSource_ListsInlineAsTheOnlyKind()
    {
        Dictionary<string, JsonElement> arguments = Arguments(
            """
            {
              "deckId": "0193e97a-ef95-7700-8000-000000000001",
              "source": { "kind": "preset", "presetId": "common-v1", "assignmentMode": "add-only", "bindings": [] }
            }
            """);

        OperationInvalidInput? failure = ToolCallPolicy.DiagnoseArguments(catalog, "local_deck_category_rules_preview",
            arguments);

        Assert.NotNull(failure);
        Assert.Equal("source", failure.Path);
        Assert.Contains("Allowed kinds: inline.", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies inline category rules still bind (FLOW-003); the revision is optional.
    /// </summary>
    [Fact]
    public void DiagnoseArguments_InlineCategoryRuleSource_Binds()
    {
        Dictionary<string, JsonElement> arguments = Arguments(
            """
            {
              "deckId": "0193e97a-ef95-7700-8000-000000000001",
              "source": { "kind": "inline", "ruleSet": { "assignmentMode": "add-only", "rules": [] } }
            }
            """);

        Assert.Null(ToolCallPolicy.DiagnoseArguments(catalog, "local_deck_category_rules_preview", arguments));

        Dictionary<string, JsonElement> withRevision = Arguments(
            """
            {
              "deckId": "0193e97a-ef95-7700-8000-000000000001",
              "expectedRevision": 3,
              "source": { "kind": "inline", "ruleSet": { "assignmentMode": "add-only", "rules": [] } }
            }
            """);
        Assert.Null(ToolCallPolicy.DiagnoseArguments(catalog, "local_deck_category_rules_preview", withRevision));
    }

    /// <summary>
    /// Verifies every description starts with its data source, and that a source label fits the tool's prefix
    /// (NAME-002).
    /// </summary>
    [Fact]
    public void Descriptions_StartWithTheirDataSource()
    {
        using RegisteredToolSet remote = new(OperationMode.Remote);

        foreach (McpServerTool tool in remote.Tools)
        {
            string name = tool.ProtocolTool.Name;
            string description = tool.ProtocolTool.Description!;
            string label = Assert.Single(
                SourceLabels.Where(value => description.StartsWith(value, StringComparison.Ordinal)));
            string[] allowed = name switch
            {
                _ when name.StartsWith("local_deck_", StringComparison.Ordinal) => ["Local deck store:"],
                "stats_local_deck_summary" => ["Local deck store:"],
                _ when name.StartsWith("stats_", StringComparison.Ordinal) => ["Exact math:"],
                _ when name.StartsWith("scryfall_", StringComparison.Ordinal) =>
                    ["Scryfall:", "Saved Scryfall requests:", "Installed Scryfall card data:"],
                _ when name.StartsWith("archidekt_", StringComparison.Ordinal) => ["Archidekt:"],
                _ when name.StartsWith("playgroup_", StringComparison.Ordinal) => ["Playgroup:"],
                _ when name.StartsWith("spellbook_", StringComparison.Ordinal) => ["Commander Spellbook:"],
                _ => [],
            };
            Assert.True(allowed.Contains(label), $"{name} starts with {label}.");
        }
    }

    /// <summary>
    /// Verifies tools whose names match once the source prefix is removed name different sources (NAME-002).
    /// </summary>
    [Fact]
    public void ToolsWithTheSameNameAfterThePrefix_NameDifferentSources()
    {
        using RegisteredToolSet remote = new(OperationMode.Remote);
        string[] prefixes = ["local_deck_", "scryfall_", "card_", "stats_", "archidekt_", "playgroup_", "spellbook_"];
        Dictionary<string, List<string>> sourcesByRemainder = new(StringComparer.Ordinal);

        foreach (McpServerTool tool in remote.Tools)
        {
            string name = tool.ProtocolTool.Name;
            string prefix = prefixes.First(value => name.StartsWith(value, StringComparison.Ordinal));
            string remainder = name[prefix.Length..];
            string label = SourceLabels.Single(value =>
                tool.ProtocolTool.Description!.StartsWith(value, StringComparison.Ordinal));
            if (!sourcesByRemainder.TryGetValue(remainder, out List<string>? labels))
            {
                labels = [];
                sourcesByRemainder[remainder] = labels;
            }

            labels.Add(label);
        }

        foreach ((string remainder, List<string> labels) in sourcesByRemainder)
        {
            Assert.True(labels.Count == labels.Distinct(StringComparer.Ordinal).Count(), remainder);
        }

        Assert.Contains(sourcesByRemainder, pair => pair.Value.Count > 1);
    }

    /// <summary>
    /// Verifies every tool that returns text written by provider users says it is untrusted data (SAFE-001).
    /// </summary>
    [Fact]
    public void ProviderTextTools_SayTheTextIsUntrusted()
    {
        using RegisteredToolSet remote = new(OperationMode.Remote);

        foreach (string name in UntrustedTextTools)
        {
            Assert.Contains(
                UntrustedTextNotes.Marker,
                Find(remote, name).ProtocolTool.Description,
                StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Verifies no result type of any tool has a property that holds a credential or secret value (SAFE-001). The
    /// opaque apply token of a category preview guards a write and is not a credential.
    /// </summary>
    [Fact]
    public void ToolResultTypes_HaveNoCredentialProperties()
    {
        IReadOnlyDictionary<string, MethodInfo> methods = RegisteredTools.Methods();
        string[] forbidden = ["password", "secret", "apikey", "cookie", "bearer", "authorization", "credentialsfile"];
        string[] allowedExact = ["ApplyToken"];
        List<string> offenders = [];
        HashSet<Type> visited = [];

        foreach ((string toolName, MethodInfo method) in methods)
        {
            Walk(toolName, ResultDataType(method), forbidden, allowedExact, visited, offenders, 0);
        }

        Assert.Empty(offenders);
        Assert.True(visited.Count > 100, $"Only {visited.Count} result types were inspected.");
    }

    /// <summary>
    /// Verifies the seven destructive remote writes carry the confirmation hint, and no other tool does (SAFE-002).
    /// </summary>
    [Fact]
    public void DestructiveRemoteWrites_CarryTheConfirmationHint()
    {
        using RegisteredToolSet remote = new(OperationMode.Remote);

        string[] hinted = [.. remote.Tools
            .Where(tool => tool.ProtocolTool.Meta?["anthropic/requiresUserInteraction"]?.GetValue<bool>() == true)
            .Select(tool => tool.ProtocolTool.Name)
            .Order(StringComparer.Ordinal)];

        Assert.Equal(ConfirmationTools.Order(StringComparer.Ordinal), hinted);
        foreach (string name in ConfirmationTools)
        {
            Assert.True(Find(remote, name).ProtocolTool.Annotations!.DestructiveHint, name);
        }
    }

    /// <summary>
    /// Verifies the tool list in each mode matches the manifests: read-only hides every write, local hides remote
    /// writes, and remote shows all, with the counts of a fully configured server (SURF-006, SURF-007).
    /// </summary>
    [Theory]
    [InlineData("read-only", 53)]
    [InlineData("local", 67)]
    [InlineData("remote", 80)]
    public void RegisteredTools_EachMode_MatchTheDescriptorCounts(string modeName, int expectedCount)
    {
        OperationMode mode = Assert.IsType<OperationSuccess<OperationMode>>(
            OperationModeParser.Parse(modeName).Value).Data;
        using RegisteredToolSet registered = new(mode);

        Assert.Equal(expectedCount, registered.Tools.Count);
        HashSet<string> published = [.. registered.Tools.Select(tool => tool.ProtocolTool.Name)];
        HashSet<string> declared = [];
        foreach (CapabilityToolsetDescriptor descriptor in CapabilityToolsetRegistry.Implemented)
        {
            declared.UnionWith(descriptor.GetVisibleToolNames(mode));
        }

        Assert.Equal(declared.Order(StringComparer.Ordinal), published.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Finds one registered tool by name.
    /// </summary>
    private static McpServerTool Find(RegisteredToolSet registered, string name)
    {
        return Assert.Single(registered.Tools, tool => tool.ProtocolTool.Name == name);
    }

    /// <summary>
    /// Reads the published choices of a field: its enum, or the enum of its list items.
    /// </summary>
    private static string[] ReadChoices(JsonElement schema, string field)
    {
        JsonElement node = schema.GetProperty("properties").GetProperty(field);
        if (node.TryGetProperty("enum", out JsonElement direct))
        {
            return [.. direct.EnumerateArray().Select(value => value.GetString()!)];
        }

        JsonElement items = node.TryGetProperty("items", out JsonElement itemsDirect)
            ? itemsDirect
            : node.GetProperty("anyOf").EnumerateArray().First(value => value.TryGetProperty("items", out _))
                .GetProperty("items");
        return [.. items.GetProperty("enum").EnumerateArray().Select(value => value.GetString()!)];
    }

    /// <summary>
    /// Gets the data type inside the operation result a tool method returns, through a task if it is async.
    /// </summary>
    private static Type ResultDataType(MethodInfo method)
    {
        Type type = method.ReturnType;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            type = type.GetGenericArguments()[0];
        }

        return type.GetGenericArguments()[0];
    }

    /// <summary>
    /// Walks the public properties of a result type, and the types they hold, and records credential-like names.
    /// </summary>
    private static void Walk(
        string tool,
        Type type,
        string[] forbidden,
        string[] allowedExact,
        HashSet<Type> visited,
        List<string> offenders,
        int depth)
    {
        if (depth > 8 || type.IsPrimitive || type.IsEnum || type == typeof(string) || !visited.Add(type))
        {
            return;
        }

        foreach (Type argument in type.GetGenericArguments())
        {
            Walk(tool, argument, forbidden, allowedExact, visited, offenders, depth + 1);
        }

        if (type.IsArray)
        {
            Walk(tool, type.GetElementType()!, forbidden, allowedExact, visited, offenders, depth + 1);
        }

        if (type.Namespace?.StartsWith("MtgMcp", StringComparison.Ordinal) != true)
        {
            return;
        }

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            bool credentialLike = forbidden.Any(word =>
                property.Name.Contains(word, StringComparison.OrdinalIgnoreCase)) ||
                property.Name.EndsWith("Token", StringComparison.Ordinal);
            if (credentialLike && !allowedExact.Contains(property.Name, StringComparer.Ordinal))
            {
                offenders.Add($"{tool}: {type.Name}.{property.Name}");
            }

            Walk(tool, property.PropertyType, forbidden, allowedExact, visited, offenders, depth + 1);
        }
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

    /// <summary>
    /// Describes one merged tool and the facts the merge rules check about it.
    /// </summary>
    private sealed record MergedTool(
        string Name,
        bool OpenWorld,
        string[] Required,
        string? ChoiceField,
        string[] ConditionalFields);
}
