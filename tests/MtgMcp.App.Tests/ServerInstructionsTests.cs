using System.Text.RegularExpressions;
using MtgMcp.App.Capabilities;
using MtgMcp.App.Configuration;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies the server instructions are short, state the agent's role and the untrusted text rule, and name only
/// tools that the active selection and mode show.
/// </summary>
public sealed partial class ServerInstructionsTests
{
    /// <summary>
    /// Verifies the instructions for the default selection with every credential, in every mode.
    /// </summary>
    [Theory]
    [InlineData("default", "remote")]
    [InlineData("default", "local")]
    [InlineData("default", "read-only")]
    [InlineData("none", "remote")]
    [InlineData("decks", "remote")]
    [InlineData("scryfall,stats", "read-only")]
    [InlineData("archidekt", "local")]
    [InlineData("playgroup,spellbook", "remote")]
    public void Build_AnySelectionAndMode_IsShortAndNamesOnlyVisibleTools(string toolsets, string modeName)
    {
        OperationMode mode = ParseMode(modeName);
        CapabilityToolsetSelection selection = Resolve(toolsets);

        string instructions = ServerInstructions.Build(selection, mode);

        Assert.True(instructions.Length <= ServerInstructions.MaximumLength, $"{instructions.Length} characters");
        Assert.Contains("you make every deckbuilding judgment", instructions, StringComparison.Ordinal);
        Assert.Contains(UntrustedTextNotes.Marker, instructions, StringComparison.Ordinal);
        HashSet<string> visible = VisibleTools(selection, mode);
        foreach (Match match in ToolLikeName().Matches(instructions))
        {
            Assert.True(visible.Contains(match.Value), $"'{match.Value}' is named but not visible.");
        }
    }

    /// <summary>
    /// Verifies each job appears with its first tool when its toolset is on, and only then.
    /// </summary>
    [Fact]
    public void Build_JobsFollowTheSelectedToolsets()
    {
        string everything = ServerInstructions.Build(Resolve("default"), OperationMode.Remote);
        string decksOnly = ServerInstructions.Build(Resolve("decks"), OperationMode.Remote);
        string readOnly = ServerInstructions.Build(Resolve("decks"), OperationMode.ReadOnly);

        Assert.Contains("scryfall_search", everything, StringComparison.Ordinal);
        Assert.Contains("local_deck_get", everything, StringComparison.Ordinal);
        Assert.Contains("local_deck_apply_changes", everything, StringComparison.Ordinal);
        Assert.Contains("stats_hypergeometric", everything, StringComparison.Ordinal);
        Assert.Contains("archidekt_push_preview", everything, StringComparison.Ordinal);
        Assert.Contains("playgroup_playgroup_games_list", everything, StringComparison.Ordinal);
        Assert.Contains("spellbook_deck_combos_find", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("scryfall_search", decksOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("archidekt_", decksOnly, StringComparison.Ordinal);
        Assert.Contains("local_deck_apply_changes", decksOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("local_deck_apply_changes", readOnly, StringComparison.Ordinal);
        Assert.Contains("local_deck_get", readOnly, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a missing selection is refused.
    /// </summary>
    [Fact]
    public void Build_NullSelection_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ServerInstructions.Build(null!, OperationMode.Remote));
    }

    /// <summary>
    /// Reads an operation mode from its configuration value.
    /// </summary>
    private static OperationMode ParseMode(string value)
    {
        return Assert.IsType<OperationSuccess<OperationMode>>(OperationModeParser.Parse(value).Value).Data;
    }

    /// <summary>
    /// Resolves a toolset selection as if every provider credential were present.
    /// </summary>
    private static CapabilityToolsetSelection Resolve(string toolsets)
    {
        OperationResult<CapabilityToolsetSelection> result = CapabilityToolsetRegistry.Resolve(toolsets);
        return Assert.IsType<OperationSuccess<CapabilityToolsetSelection>>(result.Value).Data;
    }

    /// <summary>
    /// Lists the tool names the selection shows in one mode.
    /// </summary>
    private static HashSet<string> VisibleTools(CapabilityToolsetSelection selection, OperationMode mode)
    {
        HashSet<string> visible = new(StringComparer.Ordinal);
        foreach (CapabilityToolsetDescriptor descriptor in CapabilityToolsetRegistry.Implemented)
        {
            if (selection.Includes(descriptor.Toolset))
            {
                visible.UnionWith(descriptor.GetVisibleToolNames(mode));
            }
        }

        return visible;
    }

    /// <summary>
    /// Matches a word that looks like one of this server's tool names.
    /// </summary>
    [GeneratedRegex("\\b(?:local_deck|scryfall|stats|card|archidekt|playgroup|spellbook)_[a-z_]+\\b")]
    private static partial Regex ToolLikeName();
}
