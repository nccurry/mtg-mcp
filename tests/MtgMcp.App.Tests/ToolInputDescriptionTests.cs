using System.Text.Json;
using ModelContextProtocol.Server;
using MtgMcp.App.Configuration;

namespace MtgMcp.App.Tests;

/// <summary>
/// Checks that every field a caller can send, including fields inside request objects and list items, says what it
/// is for. The tools named in <see cref="OpenGaps"/> still have undescribed nested fields and are tracked as findings.
/// </summary>
public sealed class ToolInputDescriptionTests
{
    /// <summary>
    /// Names the tools whose nested input fields still have no description. Remove a tool from this list when its
    /// fields are described; the second test fails when a listed tool has no gap left.
    /// </summary>
    private static readonly string[] OpenGaps =
    [
        "local_deck_import_preview", "local_deck_export_bundle", "local_deck_import_create", "local_deck_create",
        "local_deck_category_rules_preview", "local_deck_category_rules_apply", "local_deck_apply_changes",
        "archidekt_pull_apply", "archidekt_deck_create", "archidekt_deck_delete", "archidekt_push_apply",
        "archidekt_folder_create", "archidekt_folder_update", "archidekt_folder_move_items",
        "archidekt_folder_delete", "archidekt_snapshot_create", "archidekt_snapshot_update",
        "archidekt_snapshot_delete", "archidekt_snapshot_restore_apply",
    ];

    /// <summary>
    /// Verifies every input field of every tool outside the open gaps has a description.
    /// </summary>
    [Fact]
    public void InputFields_HaveDescriptions_ExceptTheNamedOpenGaps()
    {
        using RegisteredToolSet registered = new(OperationMode.Remote);
        List<string> missing = [];
        foreach (McpServerTool tool in registered.Tools)
        {
            if (!OpenGaps.Contains(tool.ProtocolTool.Name, StringComparer.Ordinal))
            {
                CollectUndescribed(tool.ProtocolTool.InputSchema, "$", missing, tool.ProtocolTool.Name);
            }
        }

        Assert.True(missing.Count == 0, "Input fields without a description:\n" + string.Join("\n", missing));
    }

    /// <summary>
    /// Verifies each open gap is real, so the list shrinks as the fields are described.
    /// </summary>
    [Fact]
    public void OpenGaps_StillHaveUndescribedFields()
    {
        using RegisteredToolSet registered = new(OperationMode.Remote);
        foreach (string name in OpenGaps)
        {
            McpServerTool tool = Assert.Single(registered.Tools, value => value.ProtocolTool.Name == name);
            List<string> missing = [];
            CollectUndescribed(tool.ProtocolTool.InputSchema, "$", missing, name);

            Assert.True(missing.Count > 0, $"{name} has no undescribed field left; remove it from OpenGaps.");
        }
    }

    /// <summary>
    /// Verifies the Scryfall lookup kind publishes its allowed values instead of only describing them.
    /// </summary>
    [Fact]
    public void ScryfallLookupKind_PublishesItsAllowedValues()
    {
        using RegisteredToolSet registered = new(OperationMode.Remote);
        McpServerTool tool = Assert.Single(registered.Tools, value => value.ProtocolTool.Name == "scryfall_card_details");

        JsonElement kind = tool.ProtocolTool.InputSchema.GetProperty("properties").GetProperty("lookup")
            .GetProperty("properties").GetProperty("kind");

        Assert.Equal(
            ["scryfall-id", "oracle-id", "exact-name", "fuzzy-name", "printing"],
            kind.GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
    }

    /// <summary>
    /// Walks a published schema and records every typed property that has no description. A free-form JSON value has no\n    /// type and cannot carry a description, so its parent describes it.
    /// </summary>
    private static void CollectUndescribed(JsonElement node, string path, List<string> missing, string tool)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in node.EnumerateArray())
            {
                CollectUndescribed(item, path, missing, tool);
            }

            return;
        }

        if (node.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (JsonProperty member in node.EnumerateObject())
        {
            if (member.Name == "properties" && member.Value.ValueKind == JsonValueKind.Object)
            {
                CollectUndescribedProperties(member.Value, path, missing, tool);
            }
            else
            {
                CollectUndescribed(member.Value, path, missing, tool);
            }
        }
    }

    /// <summary>
    /// Records each property of one schema <c>properties</c> object that has no description, then walks into it.
    /// </summary>
    private static void CollectUndescribedProperties(
        JsonElement properties,
        string path,
        List<string> missing,
        string tool)
    {
        foreach (JsonProperty property in properties.EnumerateObject())
        {
            bool described = property.Value.TryGetProperty("description", out JsonElement text) &&
                !string.IsNullOrWhiteSpace(text.GetString());
            if (!described && property.Value.TryGetProperty("type", out _))
            {
                missing.Add($"{tool} {path}.{property.Name}");
            }

            CollectUndescribed(property.Value, $"{path}.{property.Name}", missing, tool);
        }
    }
}
