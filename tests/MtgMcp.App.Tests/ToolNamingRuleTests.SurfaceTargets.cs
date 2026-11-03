using System.Collections;
using System.Reflection;
using MtgMcp.App.Configuration;

namespace MtgMcp.App.Tests;

/// <summary>
/// Holds the argument naming rules of the surface-consistency end state: flat write arguments, one identifier name per
/// family, one confirmation form, and one preview guard name. The tests carry the <c>SurfaceConsistencyTarget</c>
/// category, so <c>task test</c> skips them until every tool family follows the rules.
/// </summary>
public sealed partial class ToolNamingRuleTests
{
    /// <summary>
    /// Lists the operations that destroy or replace data with no local undo and so take a confirmation phrase.
    /// </summary>
    private static readonly string[] ConfirmedOperations =
    [
        "archidekt_deck_delete", "archidekt_folder_delete", "archidekt_snapshot_delete",
        "archidekt_snapshot_restore_apply", "scryfall_card_data_delete", "scryfall_request_snapshot_delete",
        "scryfall_card_data_sync",
    ];

    /// <summary>
    /// Names the one document-shaped argument each write tool may keep as an object or a list of objects, by tool.
    /// Every statistics tool takes request objects and no write tool of that family exists.
    /// </summary>
    private static readonly Dictionary<string, string> KeptObjectArguments = new(StringComparer.Ordinal)
    {
        ["local_deck_apply_changes"] = "changes",
        ["archidekt_folder_move_items"] = "items",
        ["playgroup_game_events_batch_create"] = "events",
    };

    /// <summary>
    /// Verifies a write tool takes no object argument except the deck graph of <c>local_deck_create</c>, the
    /// <c>changes</c> of <c>local_deck_apply_changes</c>, the <c>items</c> of <c>archidekt_folder_move_items</c>, the
    /// <c>events</c> of the Playgroup event batch, and statistics requests.
    /// </summary>
    [Fact]
    [Trait("Category", "SurfaceConsistencyTarget")]
    public void WriteTools_TakeNoObjectArgumentOutsideTheAllowlist()
    {
        using RegisteredToolSet registered = new(OperationMode.Remote);
        List<string> offenders = [];
        foreach (ModelContextProtocol.Server.McpServerTool tool in registered.Tools)
        {
            string name = tool.ProtocolTool.Name;
            if (tool.ProtocolTool.Annotations?.ReadOnlyHint != false || name.StartsWith("stats_", StringComparison.Ordinal))
            {
                continue;
            }

            MethodInfo method = registered.Catalog.Methods[name];
            int graphs = 0;
            foreach (ParameterInfo parameter in ArgumentParameters(method).Where(value => IsObject(value.ParameterType)))
            {
                graphs++;
                bool kept = name == "local_deck_create" ||
                    (KeptObjectArguments.TryGetValue(name, out string? allowed) && allowed == parameter.Name);
                if (!kept || (name == "local_deck_create" && graphs > 1))
                {
                    offenders.Add($"{name}: {parameter.Name}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "Write tools with an object argument off the allowlist:\n" +
            string.Join("\n", offenders));
    }

    /// <summary>
    /// Verifies <c>deckId</c> appears only on tools that read local decks, and that Archidekt and Playgroup tools name
    /// their deck identifiers <c>localDeckId</c>, <c>remoteDeckId</c>, and <c>playgroupDeckId</c>.
    /// </summary>
    [Fact]
    [Trait("Category", "SurfaceConsistencyTarget")]
    public void DeckIdentifiers_AreNamedByTheDeckTheyIdentify()
    {
        using RegisteredToolSet registered = new(OperationMode.Remote);
        List<string> offenders = [];
        foreach ((string tool, MethodInfo method) in SurfaceConsistencyTargetTests.Ordered(registered))
        {
            foreach (string argument in SurfaceConsistencyTargetTests.ArgumentNames(method))
            {
                bool badArchidekt = tool.StartsWith("archidekt_", StringComparison.Ordinal) &&
                    (argument == "deckId" || (argument.EndsWith("DeckId", StringComparison.Ordinal) &&
                        argument is not ("localDeckId" or "remoteDeckId")));
                bool badPlaygroup = tool.StartsWith("playgroup_", StringComparison.Ordinal) &&
                    (argument == "deckId" || (argument.EndsWith("DeckId", StringComparison.Ordinal) &&
                        argument != "playgroupDeckId"));
                if (badArchidekt || badPlaygroup)
                {
                    offenders.Add($"{tool}: {argument}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "Deck identifiers with the wrong name:\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// Verifies the Scryfall request-snapshot tools name their identifier <c>requestSnapshotId</c>, and no Scryfall
    /// tool takes a bare <c>snapshotId</c>.
    /// </summary>
    [Fact]
    [Trait("Category", "SurfaceConsistencyTarget")]
    public void ScryfallRequestSnapshots_AreIdentifiedByRequestSnapshotId()
    {
        using RegisteredToolSet registered = new(OperationMode.Remote);
        IReadOnlyDictionary<string, MethodInfo> methods = registered.Catalog.Methods;
        List<string> offenders =
        [
            .. methods
                .Where(pair => pair.Key.StartsWith("scryfall_", StringComparison.Ordinal))
                .Where(pair => SurfaceConsistencyTargetTests.ArgumentNames(pair.Value).Contains("snapshotId"))
                .Select(pair => $"{pair.Key}: snapshotId"),
        ];
        if (!SurfaceConsistencyTargetTests.ArgumentNames(methods["scryfall_request_snapshot_delete"])
            .Contains("requestSnapshotId"))
        {
            offenders.Add("scryfall_request_snapshot_delete: no requestSnapshotId");
        }

        Assert.True(offenders.Count == 0, "Request snapshot identifiers with the wrong name:\n" +
            string.Join("\n", offenders));
    }

    /// <summary>
    /// Verifies each operation that destroys or replaces data takes a text <c>confirmation</c>, no tool takes an
    /// <c>acknowledge</c> argument, and <c>confirmRemovals</c> stays a count.
    /// </summary>
    [Fact]
    [Trait("Category", "SurfaceConsistencyTarget")]
    public void DestructiveOperations_TakeAConfirmationPhrase_AndNoAcknowledgeBooleans()
    {
        using RegisteredToolSet registered = new(OperationMode.Remote);
        List<string> offenders = [];
        foreach ((string tool, MethodInfo method) in SurfaceConsistencyTargetTests.Ordered(registered))
        {
            ParameterInfo[] arguments = [.. ArgumentParameters(method)];
            foreach (ParameterInfo parameter in arguments)
            {
                if (parameter.Name!.StartsWith("acknowledge", StringComparison.Ordinal))
                {
                    offenders.Add($"{tool}: {parameter.Name} must be a confirmation phrase");
                }

                if (parameter.Name == "confirmRemovals" &&
                    (Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType) != typeof(int))
                {
                    offenders.Add($"{tool}: confirmRemovals must stay a count");
                }
            }

            if (ConfirmedOperations.Contains(tool) &&
                !arguments.Any(parameter => parameter.Name == "confirmation" &&
                    (Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType) == typeof(string)))
            {
                offenders.Add($"{tool}: no confirmation text");
            }
        }

        Assert.True(offenders.Count == 0, "Confirmation rule broken:\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// Names the guards of remote or stored state that are not preview fingerprints, so they keep their own names.
    /// </summary>
    private static readonly string[] NonPreviewFingerprintGuards =
    [
        "expectedRemoteFingerprint", "expectedTreeFingerprint", "expectedSnapshotContentFingerprint",
    ];

    /// <summary>
    /// Verifies the argument that guards a preview is always named <c>previewFingerprint</c>.
    /// </summary>
    [Fact]
    [Trait("Category", "SurfaceConsistencyTarget")]
    public void FingerprintArguments_AreAllNamedPreviewFingerprint()
    {
        using RegisteredToolSet registered = new(OperationMode.Remote);
        List<string> offenders = [];
        foreach ((string tool, MethodInfo method) in SurfaceConsistencyTargetTests.Ordered(registered))
        {
            offenders.AddRange(SurfaceConsistencyTargetTests.ArgumentNames(method)
                .Where(name => name.Contains("ingerprint", StringComparison.Ordinal) && name != "previewFingerprint" &&
                    !NonPreviewFingerprintGuards.Contains(name))
                .Select(name => $"{tool}: {name}"));
        }

        Assert.True(offenders.Count == 0, "Fingerprint arguments not named previewFingerprint:\n" +
            string.Join("\n", offenders));
    }

    /// <summary>
    /// Lists the parameters a caller supplies, leaving out those the host injects.
    /// </summary>
    private static IEnumerable<ParameterInfo> ArgumentParameters(MethodInfo method)
    {
        return method.GetParameters().Where(MtgMcp.App.Hosting.ToolCallPolicy.IsArgumentParameter);
    }

    /// <summary>
    /// Reports whether a parameter type is an object or a list of objects rather than text, a number, a flag, an
    /// identifier, an enum, or a list of those.
    /// </summary>
    private static bool IsObject(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsArray)
        {
            type = type.GetElementType()!;
        }
        else if (type != typeof(string) && type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type) &&
            type.GetGenericArguments().Length == 1)
        {
            type = type.GetGenericArguments()[0];
        }

        type = Nullable.GetUnderlyingType(type) ?? type;
        return !(type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(Guid) ||
            type == typeof(decimal) || type == typeof(DateTime) || type == typeof(DateTimeOffset));
    }
}
