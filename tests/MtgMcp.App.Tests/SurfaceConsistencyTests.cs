using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using MtgMcp.App.Configuration;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Tests;

/// <summary>
/// States what the published tool surface must look like once every tool family follows the shared paging rule, and
/// runs every tool driven by the budget script to prove each refusal of a value names its path.
/// </summary>
public sealed partial class SurfaceConsistencyTests
{
    /// <summary>
    /// Names the only paging arguments a tool may take.
    /// </summary>
    private static readonly string[] PagingArguments = ["cursor", "pageSize"];

    /// <summary>
    /// Names every tool that takes a cursor, so a paged tool cannot be added or lost without this list changing.
    /// </summary>
    private static readonly string[] ExpectedPagedTools =
    [
        "archidekt_deck_get", "archidekt_deck_list", "archidekt_folders_get", "archidekt_pull_preview",
        "archidekt_push_preview", "archidekt_snapshot_restore_preview", "archidekt_snapshots_get",
        "archidekt_sync_diff", "card_list_query", "card_lists_compare", "local_deck_category_rules_preview",
        "local_deck_export_bundle", "local_deck_get", "local_deck_identity_reconcile_preview",
        "local_deck_import_preview", "local_deck_list", "playgroup_playgroup_games_list", "scryfall_autocomplete",
        "scryfall_card_details", "scryfall_cards_get", "scryfall_installed_cards_by_tag", "scryfall_reference_get",
        "scryfall_request_snapshots_get", "scryfall_search", "scryfall_tags_search", "spellbook_deck_combos_find",
        "spellbook_variant_search",
    ];

    /// <summary>
    /// Verifies the remote-mode surface holds every tool, so the scans over it cannot pass on a thin registration.
    /// </summary>
    [Fact]
    public void RemoteMode_PublishesEightySixTools()
    {
        using RegisteredToolSet registered = new(OperationMode.Remote);

        Assert.Equal(86, registered.Tools.Count);
        Assert.Equal(86, registered.Catalog.Methods.Count);
    }

    /// <summary>
    /// Verifies the tools that take a cursor are exactly the expected set, and that the set is large enough for the
    /// paging scans to mean something.
    /// </summary>
    [Fact]
    public void PagedTools_AreTheExpectedSet()
    {
        using RegisteredToolSet registered = new(OperationMode.Remote);
        string[] paged =
        [
            .. Ordered(registered)
                .Where(entry => ArgumentNames(entry.Method).Contains("cursor", StringComparer.Ordinal))
                .Select(entry => entry.Tool),
        ];

        Assert.True(paged.Length >= 20, $"Only {paged.Length} paged tools were found.");
        Assert.Equal(ExpectedPagedTools.Order(StringComparer.Ordinal), paged);
    }

    /// <summary>
    /// Verifies every tool that takes <c>responseFormat</c> defaults it to <c>standard</c> and publishes exactly its
    /// values: <c>full</c> only on the Scryfall reads and <c>card_lists_compare</c>, which have raw provider objects.
    /// </summary>
    [Fact]
    public void ResponseFormat_DefaultsToStandard_AndOffersFullOnlyWhereRawObjectsExist()
    {
        using RegisteredToolSet registered = new(OperationMode.Remote);
        List<string> offenders = [];
        int checkedTools = 0;
        foreach ((string tool, MethodInfo method) in Ordered(registered))
        {
            ParameterInfo? parameter = method.GetParameters().SingleOrDefault(value => value.Name == "responseFormat");
            if (parameter is null)
            {
                continue;
            }

            checkedTools++;
            if (!Equals(parameter.DefaultValue, "standard"))
            {
                offenders.Add($"{tool}: default is {parameter.DefaultValue ?? "none"}");
            }

            string[] published = [.. parameter.GetCustomAttribute<AllowedValuesAttribute>(inherit: true)!
                .Values.Cast<string>()];
            string[] expected = tool.StartsWith("scryfall_", StringComparison.Ordinal) || tool == "card_lists_compare"
                ? ["concise", "standard", "full"]
                : ["concise", "standard"];
            if (!published.SequenceEqual(expected))
            {
                offenders.Add($"{tool}: offers {string.Join(", ", published)}");
            }
        }

        Assert.True(checkedTools >= 8, $"Only {checkedTools} tools take responseFormat.");
        Assert.True(offenders.Count == 0, "responseFormat rule broken:\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// Verifies every tool that takes a paging argument takes exactly <c>cursor</c> and <c>pageSize</c> and no other
    /// offset, page, or limit argument.
    /// </summary>
    [Fact]
    public void PagedTools_TakeOnlyCursorAndPageSize()
    {
        using RegisteredToolSet registered = new(OperationMode.Remote);
        List<string> offenders = [];
        foreach ((string tool, MethodInfo method) in Ordered(registered))
        {
            string[] paging =
            [
                .. ArgumentNames(method)
                    .Where(name => PagingName().IsMatch(name))
                    .Order(StringComparer.Ordinal),
            ];
            if (paging.Length > 0 && !paging.SequenceEqual(PagingArguments))
            {
                offenders.Add($"{tool}: {string.Join(", ", paging)}");
            }
        }

        Assert.True(offenders.Count == 0, "Tools whose paging arguments are not cursor and pageSize:\n" +
            string.Join("\n", offenders));
    }

    /// <summary>
    /// Verifies a tool returns <c>nextCursor</c> exactly when it takes <c>cursor</c>, and never returns another
    /// next-position property.
    /// </summary>
    [Fact]
    public void PagedTools_ReturnNextCursorAndNoOtherNextPosition()
    {
        using RegisteredToolSet registered = new(OperationMode.Remote);
        List<string> offenders = [];
        foreach ((string tool, MethodInfo method) in Ordered(registered))
        {
            HashSet<string> names = [];
            CollectNames(DataType(method), [], names);
            bool takesCursor = ArgumentNames(method).Contains("cursor", StringComparer.Ordinal);
            if (takesCursor != names.Contains("nextCursor"))
            {
                offenders.Add(
                    $"{tool}: takes cursor = {takesCursor}, returns nextCursor = {names.Contains("nextCursor")}");
            }

            foreach (string other in names
                .Where(name => name != "nextCursor" && NextPosition().IsMatch(name))
                .Order(StringComparer.Ordinal))
            {
                offenders.Add($"{tool}: returns {other}");
            }
        }

        Assert.True(offenders.Count == 0, "Tools that do not page with nextCursor only:\n" +
            string.Join("\n", offenders));
    }

    /// <summary>
    /// Verifies no tool the budget script drives returned an invalid-input result without the path of the value it
    /// refused.
    /// </summary>
    [Fact]
    public async Task EveryInvalidInputResult_NamesItsPath()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using ToolBudgetWorld world = await ToolBudgetWorld.CreateAsync(token);
        _ = await new ToolBudgetScript(world, token).RunAsync();

        Assert.True(
            world.PathAudit.Findings.Count == 0,
            "Invalid-input results without a path:\n" + string.Join("\n", world.PathAudit.Findings));
    }

    /// <summary>
    /// Lists the registered tools by name with the method behind each.
    /// </summary>
    internal static IEnumerable<(string Tool, MethodInfo Method)> Ordered(RegisteredToolSet registered)
    {
        return registered.Catalog.Methods
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => (pair.Key, pair.Value));
    }

    /// <summary>
    /// Lists the names of the arguments a tool method takes from the caller.
    /// </summary>
    internal static IEnumerable<string> ArgumentNames(MethodInfo method)
    {
        return method.GetParameters()
            .Where(ToolCallPolicy.IsArgumentParameter)
            .Select(parameter => parameter.Name!);
    }

    /// <summary>
    /// Gets the type of the data a tool returns on success.
    /// </summary>
    internal static Type DataType(MethodInfo method)
    {
        Type result = OperationResultSchema.ResultType(method);
        return result.IsGenericType && result.GetGenericTypeDefinition() == typeof(OperationResult<>)
            ? result.GetGenericArguments()[0]
            : result;
    }

    /// <summary>
    /// Collects the serialized property names of a type and of every type reachable from it.
    /// </summary>
    internal static void CollectNames(Type type, HashSet<Type> visited, HashSet<string> names)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || !visited.Add(type))
        {
            return;
        }

        JsonTypeInfo info;
        try
        {
            info = ToolCallPolicy.SerializerOptions.GetTypeInfo(type);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        foreach (JsonPropertyInfo property in info.Properties)
        {
            names.Add(property.Name);
            CollectNames(property.PropertyType, visited, names);
        }

        if (info.ElementType is not null)
        {
            CollectNames(info.ElementType, visited, names);
        }

        if (info.PolymorphismOptions is { } polymorphism)
        {
            foreach (JsonDerivedType derived in polymorphism.DerivedTypes)
            {
                CollectNames(derived.DerivedType, visited, names);
            }
        }
    }

    /// <summary>
    /// Matches the names of arguments that page, window, or limit a result.
    /// </summary>
    [GeneratedRegex(
        "^(cursor|pageSize|page|pageNumber|limit|maxResults|maxItems|offset|.+Offset|.+Cursor|.+PageSize)$")]
    private static partial Regex PagingName();

    /// <summary>
    /// Matches the names of result properties that carry where the next page starts.
    /// </summary>
    [GeneratedRegex("^(next[A-Z].*|.+Offset)$")]
    private static partial Regex NextPosition();
}
