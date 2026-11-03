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
            string[] paging = [.. ArgumentNames(method).Where(name => PagingName().IsMatch(name)).Order(StringComparer.Ordinal)];
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
                offenders.Add($"{tool}: takes cursor = {takesCursor}, returns nextCursor = {names.Contains("nextCursor")}");
            }

            foreach (string other in names.Where(name => name != "nextCursor" && NextPosition().IsMatch(name)).Order(StringComparer.Ordinal))
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
    private static Type DataType(MethodInfo method)
    {
        Type result = OperationResultSchema.ResultType(method);
        return result.IsGenericType && result.GetGenericTypeDefinition() == typeof(OperationResult<>)
            ? result.GetGenericArguments()[0]
            : result;
    }

    /// <summary>
    /// Collects the serialized property names of a type and of every type reachable from it.
    /// </summary>
    private static void CollectNames(Type type, HashSet<Type> visited, HashSet<string> names)
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
    [GeneratedRegex("^(cursor|pageSize|page|pageNumber|limit|maxResults|maxItems|offset|.+Offset|.+Cursor|.+PageSize)$")]
    private static partial Regex PagingName();

    /// <summary>
    /// Matches the names of result properties that carry where the next page starts.
    /// </summary>
    [GeneratedRegex("^(next[A-Z].*|.+Offset)$")]
    private static partial Regex NextPosition();
}
