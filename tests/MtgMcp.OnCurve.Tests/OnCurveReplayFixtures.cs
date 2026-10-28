using System.Text.Json;
using System.Text.Json.Nodes;

namespace MtgMcp.OnCurve.Tests;

/// <summary>
/// Holds one resolved deck entry of a captured on-curve replay case.
/// </summary>
/// <param name="Id">Small integer that stands for the entry GUID.</param>
/// <param name="Quantity">Number of copies.</param>
/// <param name="Produces">Colors the land makes, or null for a card that is not a candidate land.</param>
/// <param name="LandRule">The land rule name of a candidate land, or null.</param>
public sealed record ReplayEntry(int Id, int Quantity, IReadOnlyList<string>? Produces, string? LandRule);

/// <summary>
/// Holds one captured case: the inputs of a run of the original on-curve estimator and its success count.
/// </summary>
public sealed record ReplayCase(
    string Name,
    int TargetEntry,
    string TargetManaCost,
    IReadOnlyList<ReplayEntry> Entries,
    int TurnLimit,
    int SampleCount,
    bool OnThePlay,
    string Seed,
    int ExpectedSuccessCount);

/// <summary>
/// Reads the replay fixtures captured from the original on-curve estimator before it was replaced.
/// </summary>
public static class OnCurveReplayFixtures
{
    /// <summary>
    /// Reads every captured case from the fixture file next to the test assembly.
    /// </summary>
    public static IReadOnlyList<ReplayCase> Load()
    {
        JsonNode root = ReadRoot();
        List<ReplayCase> cases = [];
        foreach (JsonNode? node in root["cases"]!.AsArray())
        {
            JsonObject item = node!.AsObject();
            List<ReplayEntry> entries = [];
            foreach (JsonNode? entryNode in item["entries"]!.AsArray())
            {
                JsonObject entry = entryNode!.AsObject();
                IReadOnlyList<string>? produces = entry["produces"]?.AsArray()
                    .Select(color => color!.GetValue<string>())
                    .ToArray();
                entries.Add(new ReplayEntry(
                    entry["id"]!.GetValue<int>(),
                    entry["quantity"]!.GetValue<int>(),
                    produces,
                    entry["landRule"]?.GetValue<string>()));
            }

            cases.Add(new ReplayCase(
                item["name"]!.GetValue<string>(),
                item["targetEntry"]!.GetValue<int>(),
                item["targetManaCost"]!.GetValue<string>(),
                entries,
                item["turnLimit"]!.GetValue<int>(),
                item["sampleCount"]!.GetValue<int>(),
                item["onThePlay"]!.GetValue<bool>(),
                item["seed"]!.GetValue<string>(),
                item["expectedSuccessCount"]!.GetValue<int>()));
        }

        return cases;
    }

    /// <summary>
    /// Reads the success count the original tool returned for the App test deck.
    /// </summary>
    public static int AppFixtureSuccessCount()
    {
        return ReadRoot()["appFixture"]!["expectedSuccessCount"]!.GetValue<int>();
    }

    /// <summary>
    /// Turns the small integer used in the fixture file into the GUID with that first group.
    /// </summary>
    public static Guid Id(int value)
    {
        return OnCurveTestData.Id(value);
    }

    /// <summary>
    /// Parses the fixture file.
    /// </summary>
    private static JsonNode ReadRoot()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "on-curve-replay.json");
        return JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions())!;
    }
}
