using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Server;
using MtgMcp.App.Hosting;
using MtgMcp.App.Statistics;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;
using MtgMcp.Statistics;

namespace MtgMcp.App.Tests;

/// <summary>
/// Runs the example request published in each statistics tool description, so examples cannot drift.
/// </summary>
public sealed class StatisticsToolExampleTests
{
    /// <summary>
    /// Verifies every statistics tool description carries an example that binds and returns success.
    /// </summary>
    [Fact]
    public async Task EveryStatisticsExample_BindsAndSucceeds()
    {
        string root = Path.Combine(Path.GetTempPath(), $"mtg-mcp-stats-examples-{Guid.NewGuid():N}");
        try
        {
            using SqliteDeckStore store = new(root, "test");
            DeckDocument deck = Assert.IsType<OperationSuccess<DeckDocument>>((await store.CreateAsync(
                new DeckCreateRequest(
                    "Example Deck",
                    Format: "commander",
                    Entries: [new DeckEntryDraft(36, "Forest"), new DeckEntryDraft(1, "Sol Ring")]),
                TestContext.Current.CancellationToken)).Value).Data;
            StatisticsReadTools tools = new(
                new StatisticsDeckResolver(store),
                new ExactStatisticsCalculator("test"));
            List<MethodInfo> methods = typeof(StatisticsReadTools)
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
                .ToList();

            Assert.Equal(8, methods.Count);
            foreach (MethodInfo method in methods)
            {
                string name = method.GetCustomAttribute<McpServerToolAttribute>()!.Name!;
                string example = ReadExample(method)
                    .Replace(StatisticsToolExamples.PlaceholderDeckId, deck.DeckId.ToString(), StringComparison.Ordinal);
                if (name == "stats_deck_summary")
                {
                    example = example.Replace(
                        "\"expectedRevision\":1",
                        $"\"expectedRevision\":{deck.Revision}",
                        StringComparison.Ordinal);
                }

                object result = await InvokeAsync(tools, method, example);

                string kind = JsonSerializer.SerializeToElement(result, ToolCallPolicy.SerializerOptions)
                    .GetProperty("kind")
                    .GetString()!;
                Assert.True(kind == "success", $"{name} example returned {kind}.");
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// Reads the JSON example that follows the shared marker in a tool's description.
    /// </summary>
    private static string ReadExample(MethodInfo method)
    {
        string description = method.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()!.Description;
        int index = description.IndexOf(StatisticsToolExamples.Marker, StringComparison.Ordinal);
        Assert.True(index >= 0, $"{method.Name} has no example.");
        return description[(index + StatisticsToolExamples.Marker.Length)..];
    }

    /// <summary>
    /// Binds the example's request argument to the tool parameter type and awaits the tool's result.
    /// </summary>
    private static async Task<object> InvokeAsync(StatisticsReadTools tools, MethodInfo method, string example)
    {
        using JsonDocument document = JsonDocument.Parse(example);
        ParameterInfo requestParameter = method.GetParameters()[0];
        object request = document.RootElement.GetProperty(requestParameter.Name!)
            .Deserialize(requestParameter.ParameterType, ToolCallPolicy.SerializerOptions)!;
        Task task = (Task)method.Invoke(tools, [request, TestContext.Current.CancellationToken])!;
        await task;
        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }
}
