using MtgMcp.App.Configuration;

namespace MtgMcp.App.Tests;

/// <summary>
/// Calls every tool the server registers in remote mode against the largest fixtures the offline fakes can hold, and
/// fails when a successful result is longer than the 50,000 characters at which Claude Code saves a tool result to a
/// file instead of showing it. A tool that cannot be driven offline is listed with its reason, and a newly registered
/// tool fails the coverage test until it is driven or listed.
/// </summary>
public sealed class ToolResultBudgetTests
{
    /// <summary>
    /// Holds the most characters a successful tool text result may have: the point at which Claude Code stops showing
    /// a result inline and keeps only a file path.
    /// </summary>
    private const int HardCeiling = 50_000;

    /// <summary>
    /// Lists every tool the test does not call, each with the reason it cannot be driven offline. The reason must say
    /// why the result cannot grow past the limit or why no offline fixture can fill it.
    /// </summary>
    private static readonly Dictionary<string, string> NotDriven = BuildNotDriven();

    /// <summary>
    /// Lists the tools whose results are known to pass the ceiling with the test fixtures, each with the arguments that
    /// make it so. Each is named in the plan's risks; the list shrinks as each is fixed. A tool
    /// on this list must still pass the ceiling, so a fix that lands without removing its entry fails the test.
    /// </summary>
    private static readonly Dictionary<string, string> KnownOverCeiling = new(StringComparer.Ordinal)
    {
        ["local_deck_export_bundle"] = "a 300-entry deck in any format",
        ["local_deck_identity_reconcile_preview"] = "150 entries, the most one call accepts",
        ["local_deck_import_preview"] = "300 lines of text",
        ["local_deck_category_rules_preview"] = "a rule over a 300-entry deck",
        ["scryfall_search"] = "responseFormat full, 25 cards a page",
        ["scryfall_cards_get"] = "responseFormat full, 25 cards a page",
        ["scryfall_installed_cards_by_tag"] = "responseFormat full, one page",
    };

    /// <summary>
    /// Verifies every successful result of every tool the test can drive stays under the ceiling, except the listed
    /// known cases, which must still be over it, and every driven tool answered with a success at least once.
    /// </summary>
    [Fact]
    public async Task EveryDrivenTool_ReturnsASuccessUnderTheCeiling_WithLargeFixtures()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ToolBudgetWorld world = await ToolBudgetWorld.CreateAsync(token);
        List<ToolBudgetWorld.ToolOutcome> outcomes = await new ToolBudgetScript(world, token).RunAsync();

        List<ToolBudgetWorld.ToolOutcome> over =
            [.. outcomes.Where(value => !value.IsError && value.Characters > HardCeiling)];
        string[] unexpected =
            [.. over.Where(value => !KnownOverCeiling.ContainsKey(value.Tool))
                .Select(value => $"{value.Tool} returned {value.Characters} characters")];
        Assert.True(unexpected.Length == 0, string.Join("; ", unexpected));
        string[] fixedAlready = [.. KnownOverCeiling.Keys.Except(over.Select(value => value.Tool))];
        Assert.True(
            fixedAlready.Length == 0,
            "These tools no longer pass the ceiling; remove them from the known list: " + string.Join(", ", fixedAlready));
        HashSet<string> succeeded = [.. outcomes.Where(value => !value.IsError).Select(value => value.Tool)];
        HashSet<string> expected = [.. DrivenTools(world.PublishedTools)];
        Assert.True(
            expected.SetEquals(succeeded),
            "Tools without a success: " + string.Join(", ", expected.Except(succeeded)) +
            "; successes from tools the test does not list: " + string.Join(", ", succeeded.Except(expected)));
        Assert.True(
            outcomes.Max(value => value.Characters) > 5_000,
            "The fixtures never produced a large result, so the test proves nothing.");
    }

    /// <summary>
    /// Verifies every registered tool is either driven by the script or listed with a reason, and that no entry names a
    /// tool that is not registered.
    /// </summary>
    [Fact]
    public void EveryRegisteredTool_IsDrivenOrListedWithAReason()
    {
        using RegisteredToolSet set = new(OperationMode.Remote);
        HashSet<string> registered = [.. set.Tools.Select(tool => tool.ProtocolTool.Name)];

        string[] unknown = [.. NotDriven.Keys.Except(registered)];
        string[] neither = [.. registered.Except(NotDriven.Keys).Except(ToolBudgetScript.CoveredTools)];

        Assert.True(unknown.Length == 0, "Listed but not registered: " + string.Join(", ", unknown));
        Assert.True(
            neither.Length == 0,
            "Registered but neither driven nor listed with a reason: " + string.Join(", ", neither));
        Assert.All(NotDriven.Values, reason => Assert.False(string.IsNullOrWhiteSpace(reason)));
        Assert.Empty(ToolBudgetScript.CoveredTools.Intersect(NotDriven.Keys));
    }

    /// <summary>
    /// Lists the driven tools that the registered server published to the client.
    /// </summary>
    private static IEnumerable<string> DrivenTools(IReadOnlyList<string> published)
    {
        return ToolBudgetScript.CoveredTools.Where(published.Contains);
    }

    /// <summary>
    /// Builds the list of tools that are not driven, with a reason for each.
    /// </summary>
    private static Dictionary<string, string> BuildNotDriven()
    {
        const string playgroup =
            "Playgroup HTTP transport takes no injected handler, so no offline fixture can fill it; its list results are " +
            "cut by the provider's own page and limit arguments.";
        const string spellbook =
            "Commander Spellbook HTTP transport takes no injected handler, so no offline fixture can fill it; its " +
            "results are cut by the limit and offset arguments.";
        const string archidektWrite =
            "The fake Archidekt has no route for this write, and the result is one fixed-size outcome that does not grow " +
            "with the size of a deck or a list.";
        Dictionary<string, string> reasons = new(StringComparer.Ordinal);
        foreach (string tool in new[]
        {
            "playgroup_auth_status", "playgroup_me_get", "playgroup_commander_get", "playgroup_commander_get_by_name",
            "playgroup_commander_turn_damage_get", "playgroup_deck_get", "playgroup_deck_elo_history_get",
            "playgroup_user_get", "playgroup_user_decks_list", "playgroup_user_playgroups_list",
            "playgroup_user_playgroup_get", "playgroup_playgroup_members_list", "playgroup_playgroup_games_list",
            "playgroup_playgroup_game_get", "playgroup_games_stats_get", "playgroup_game_events_batch_create",
            "playgroup_live_session_create",
        })
        {
            reasons[tool] = playgroup;
        }

        foreach (string tool in new[]
        {
            "spellbook_variant_search", "spellbook_variant_get", "spellbook_deck_combos_find",
            "spellbook_deck_bracket_facts_get",
        })
        {
            reasons[tool] = spellbook;
        }

        foreach (string tool in new[]
        {
            "archidekt_deck_create", "archidekt_deck_delete", "archidekt_folder_create", "archidekt_folder_update",
            "archidekt_folder_move_items", "archidekt_folder_delete", "archidekt_snapshot_create",
            "archidekt_snapshot_update", "archidekt_snapshot_delete", "archidekt_snapshot_restore_apply",
        })
        {
            reasons[tool] = archidektWrite;
        }

        return reasons;
    }
}
