using System.Text.Json;
using System.Text.Json.Nodes;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies how <c>stats_simulate</c> binds its arguments: every required field, a union kind in any position, an
/// unknown kind, and an unknown property at every depth (SIM-001, SCHEMA-007, S13).
/// </summary>
public sealed class StatisticsSimulateBindingTests : IClassFixture<ToolCatalogFixture>
{
    /// <summary>
    /// Holds a request that uses every kind of rule, check, and select once, so each can be broken in turn.
    /// </summary>
    private const string Full =
        """
        {
          "deck": { "deckId": "0193e97a-ef95-7700-8000-000000000001", "expectedRevision": 3 },
          "groups": [
            {
              "id": "lands",
              "select": { "kind": "category-ids", "ids": ["0193e97a-ef95-7700-8000-000000000021"] },
              "produces": {
                "default": ["G"],
                "byEntry": [ { "entryIds": ["0193e97a-ef95-7700-8000-000000000011"], "colors": ["B", "G"] } ]
              }
            },
            { "id": "ramp", "select": { "kind": "entry-ids", "ids": ["0193e97a-ef95-7700-8000-000000000012"] } }
          ],
          "openingHand": {
            "size": 7,
            "onThePlay": true,
            "mulligan": {
              "rule": "london",
              "freeFirstMulligan": true,
              "maxMulligans": 2,
              "keepWhen": {
                "kind": "all",
                "checks": [
                  { "kind": "count-between", "group": "lands", "min": 2, "max": 5 },
                  { "kind": "any", "checks": [ { "kind": "count-between", "group": "ramp", "min": 0, "max": 2 } ] }
                ]
              },
              "bottom": { "preferGroups": ["ramp"], "otherwise": "random" }
            }
          },
          "eachTurn": [
            {
              "kind": "play", "group": "lands", "pick": "target-first", "maxPerTurn": 1, "addsMana": 1,
              "manaFrom": "same-turn", "targetCost": "{1}{G}",
              "manaFromByEntry": [ { "entryIds": ["0193e97a-ef95-7700-8000-000000000011"], "manaFrom": "next-turn" } ]
            },
            { "kind": "cast-if-able", "group": "ramp", "pick": "listed-order", "maxPerTurn": 1, "cost": 2,
              "manaFrom": "next-turn" },
            { "kind": "draw-cards", "group": "ramp", "pick": "random", "maxPerTurn": 1, "cost": 3, "cards": 2,
              "effect": "once" }
          ],
          "turns": 5,
          "conditions": [
            {
                 "id": "a",
                "check": {
                     "kind": "can-cast",
                    "manaCost": "{2}{B}{G}",
                    "turn": 4,
                    "inHand": "0193e97a-ef95-7700-8000-000000000013" } },
            { "id": "b", "check": { "kind": "all", "checks": [
                { "kind": "mana-at-least", "amount": 4, "turn": 4 },
                { "kind": "any", "checks": [
                  { "kind": "drawn-at-least", "group": "ramp", "count": 1, "byTurn": 2 },
                  { "kind": "on-battlefield-at-least", "group": "lands", "count": 3, "byTurn": 3 }
                ] }
            ] } }
          ],
          "samples": 1000,
          "sampleHands": 3,
          "seed": "9f3c1a7e5b2d4c60"
        }
        """;

    /// <summary>
    /// Holds the argument catalog of one full registration.
    /// </summary>
    private readonly ToolArgumentCatalog catalog;

    /// <summary>
    /// Takes the catalog the fixture built.
    /// </summary>
    public StatisticsSimulateBindingTests(ToolCatalogFixture fixture)
    {
        catalog = fixture.Catalog;
    }

    /// <summary>
    /// Verifies the request that uses every kind binds.
    /// </summary>
    [Fact]
    public void Full_BindsWithoutDiagnosis()
    {
        Assert.Null(Diagnose(Parse(Full)));
        Assert.Null(ToolCallPolicy.FindUnknownArgument(catalog, "stats_simulate", Arguments(Parse(Full))));
    }

    /// <summary>
    /// Verifies a request that leaves out any one required field is refused and the failure names that field
    /// (SIM-001, S13).
    /// </summary>
    [Theory]
    [InlineData("deck.deckId", "deck.deckId")]
    [InlineData("samples", "samples")]
    [InlineData("sampleHands", "sampleHands")]
    [InlineData("groups[0].id", "groups[0].id")]
    [InlineData("groups[0].select", "groups[0].select")]
    [InlineData("groups[0].produces.default", "groups[0].produces.default")]
    [InlineData("groups[0].produces.byEntry[0].entryIds", "groups[0].produces.byEntry[0].entryIds")]
    [InlineData("groups[0].produces.byEntry[0].colors", "groups[0].produces.byEntry[0].colors")]
    [InlineData("openingHand.size", "openingHand.size")]
    [InlineData("openingHand.onThePlay", "openingHand.onThePlay")]
    [InlineData("openingHand.mulligan.rule", "openingHand.mulligan.rule")]
    [InlineData("openingHand.mulligan.freeFirstMulligan", "openingHand.mulligan.freeFirstMulligan")]
    [InlineData("openingHand.mulligan.maxMulligans", "openingHand.mulligan.maxMulligans")]
    [InlineData("openingHand.mulligan.keepWhen", "openingHand.mulligan.keepWhen")]
    [InlineData("openingHand.mulligan.bottom", "openingHand.mulligan.bottom")]
    [InlineData("openingHand.mulligan.bottom.preferGroups", "openingHand.mulligan.bottom.preferGroups")]
    [InlineData("openingHand.mulligan.bottom.otherwise", "openingHand.mulligan.bottom.otherwise")]
    [InlineData("openingHand.mulligan.keepWhen.checks[0].group", "openingHand.mulligan.keepWhen.checks[0].group")]
    [InlineData("openingHand.mulligan.keepWhen.checks[0].min", "openingHand.mulligan.keepWhen.checks[0].min")]
    [InlineData("openingHand.mulligan.keepWhen.checks[0].max", "openingHand.mulligan.keepWhen.checks[0].max")]
    [InlineData("openingHand.mulligan.keepWhen.checks[1].checks", "openingHand.mulligan.keepWhen.checks[1].checks")]
    [InlineData("eachTurn[0].group", "eachTurn[0].group")]
    [InlineData("eachTurn[0].pick", "eachTurn[0].pick")]
    [InlineData("eachTurn[0].maxPerTurn", "eachTurn[0].maxPerTurn")]
    [InlineData("eachTurn[0].manaFrom", "eachTurn[0].manaFrom")]
    [InlineData("eachTurn[0].manaFromByEntry[0].entryIds", "eachTurn[0].manaFromByEntry[0].entryIds")]
    [InlineData("eachTurn[0].manaFromByEntry[0].manaFrom", "eachTurn[0].manaFromByEntry[0].manaFrom")]
    [InlineData("eachTurn[1].pick", "eachTurn[1].pick")]
    [InlineData("eachTurn[1].maxPerTurn", "eachTurn[1].maxPerTurn")]
    [InlineData("eachTurn[1].cost", "eachTurn[1].cost")]
    [InlineData("eachTurn[1].manaFrom", "eachTurn[1].manaFrom")]
    [InlineData("eachTurn[2].pick", "eachTurn[2].pick")]
    [InlineData("eachTurn[2].maxPerTurn", "eachTurn[2].maxPerTurn")]
    [InlineData("eachTurn[2].cost", "eachTurn[2].cost")]
    [InlineData("eachTurn[2].cards", "eachTurn[2].cards")]
    [InlineData("eachTurn[2].effect", "eachTurn[2].effect")]
    [InlineData("conditions[0].id", "conditions[0].id")]
    [InlineData("conditions[0].check", "conditions[0].check")]
    [InlineData("conditions[0].check.manaCost", "conditions[0].check.manaCost")]
    [InlineData("conditions[1].check.checks", "conditions[1].check.checks")]
    [InlineData("conditions[1].check.checks[0].amount", "conditions[1].check.checks[0].amount")]
    [InlineData("conditions[1].check.checks[0].turn", "conditions[1].check.checks[0].turn")]
    [InlineData("conditions[1].check.checks[1].checks[0].group", "conditions[1].check.checks[1].checks[0].group")]
    [InlineData("conditions[1].check.checks[1].checks[0].count", "conditions[1].check.checks[1].checks[0].count")]
    [InlineData("conditions[1].check.checks[1].checks[0].byTurn", "conditions[1].check.checks[1].checks[0].byTurn")]
    [InlineData("conditions[1].check.checks[1].checks[1].group", "conditions[1].check.checks[1].checks[1].group")]
    [InlineData("conditions[1].check.checks[1].checks[1].count", "conditions[1].check.checks[1].checks[1].count")]
    [InlineData("conditions[1].check.checks[1].checks[1].byTurn", "conditions[1].check.checks[1].checks[1].byTurn")]
    public void WithoutARequiredField_NamesThatField(string remove, string path)
    {
        JsonNode request = Parse(Full);
        Remove(request, remove);

        OperationInvalidInput? failure = Diagnose(request);

        Assert.NotNull(failure);
        Assert.Equal(path, failure.Path);
        Assert.Equal("invalid-arguments", failure.ReasonCode);
        Assert.DoesNotContain("MtgMcp", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a union entry binds with its <c>kind</c> as the last property, in every union.
    /// </summary>
    [Theory]
    [InlineData("groups[0].select")]
    [InlineData("openingHand.mulligan.keepWhen")]
    [InlineData("openingHand.mulligan.keepWhen.checks[1]")]
    [InlineData("eachTurn[0]")]
    [InlineData("eachTurn[1]")]
    [InlineData("eachTurn[2]")]
    [InlineData("conditions[0].check")]
    [InlineData("conditions[1].check")]
    [InlineData("conditions[1].check.checks[1]")]
    [InlineData("conditions[1].check.checks[1].checks[0]")]
    public void KindLast_BindsInEveryUnion(string path)
    {
        JsonNode request = Parse(Full);
        JsonObject union = Assert.IsType<JsonObject>(Select(request, path));
        string kind = union["kind"]!.GetValue<string>();
        union.Remove("kind");
        union["kind"] = kind;

        Assert.Null(Diagnose(request));
    }

    /// <summary>
    /// Verifies the preset binds with its <c>kind</c> last, and an unknown preset kind is reported with the allowed
    /// kinds.
    /// </summary>
    [Fact]
    public void Preset_BindsKindLastAndReportsAnUnknownKind()
    {
        const string preset =
            """
            {
              "deck": { "deckId": "0193e97a-ef95-7700-8000-000000000001" },
              "preset": {
                "targetEntryId": "0193e97a-ef95-7700-8000-000000000013",
                "landRules": [ { "entryId": "0193e97a-ef95-7700-8000-000000000011", "rule": "one-mana-same-turn" } ],
                "turnLimit": 4, "onThePlay": true, "kind": "on-curve"
              },
              "samples": 100, "sampleHands": 0
            }
            """;
        JsonNode unknown = Parse(preset);
        unknown["preset"]!["kind"] = "goldfish";

        Assert.Null(Diagnose(Parse(preset)));
        OperationInvalidInput? failure = Diagnose(unknown);
        Assert.NotNull(failure);
        Assert.Equal("preset", failure.Path);
        Assert.Contains("on-curve", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies an unknown kind in each union is reported at its path with the allowed kinds, including kinds nested
    /// inside <c>all</c> and <c>any</c>.
    /// </summary>
    [Theory]
    [InlineData("groups[0].select", "entry-ids")]
    [InlineData("openingHand.mulligan.keepWhen", "count-between")]
    [InlineData("openingHand.mulligan.keepWhen.checks[1].checks[0]", "count-between")]
    [InlineData("eachTurn[1]", "cast-if-able")]
    [InlineData("conditions[0].check", "can-cast")]
    [InlineData("conditions[1].check.checks[0]", "mana-at-least")]
    [InlineData("conditions[1].check.checks[1].checks[1]", "on-battlefield-at-least")]
    public void UnknownKind_IsReportedAtItsPathWithTheAllowedKinds(string path, string allowed)
    {
        JsonNode request = Parse(Full);
        Assert.IsType<JsonObject>(Select(request, path))["kind"] = "made-up";

        OperationInvalidInput? failure = Diagnose(request);

        Assert.NotNull(failure);
        Assert.Equal(path, failure.Path);
        Assert.Contains(allowed, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies an unknown property is refused at every depth, naming the property and its place.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("deck")]
    [InlineData("groups[0]")]
    [InlineData("groups[0].select")]
    [InlineData("groups[0].produces")]
    [InlineData("groups[0].produces.byEntry[0]")]
    [InlineData("openingHand")]
    [InlineData("openingHand.mulligan")]
    [InlineData("openingHand.mulligan.bottom")]
    [InlineData("openingHand.mulligan.keepWhen")]
    [InlineData("openingHand.mulligan.keepWhen.checks[0]")]
    [InlineData("openingHand.mulligan.keepWhen.checks[1].checks[0]")]
    [InlineData("eachTurn[0]")]
    [InlineData("eachTurn[0].manaFromByEntry[0]")]
    [InlineData("eachTurn[1]")]
    [InlineData("eachTurn[2]")]
    [InlineData("conditions[0]")]
    [InlineData("conditions[0].check")]
    [InlineData("conditions[1].check")]
    [InlineData("conditions[1].check.checks[0]")]
    [InlineData("conditions[1].check.checks[1].checks[0]")]
    public void UnknownProperty_IsRefusedAtEveryDepth(string path)
    {
        JsonNode request = Parse(Full);
        Assert.IsType<JsonObject>(path.Length == 0 ? request : Select(request, path))["surprise"] = 1;

        OperationInvalidInput? failure = path.Length == 0
            ? ToolCallPolicy.FindUnknownArgument(catalog, "stats_simulate", Arguments(request))
            : Diagnose(request);

        Assert.NotNull(failure);
        Assert.Contains("surprise", failure.Message, StringComparison.Ordinal);
        Assert.Contains("surprise", failure.Path ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies an unknown property inside the preset is refused.
    /// </summary>
    [Fact]
    public void UnknownProperty_InThePreset_IsRefused()
    {
        JsonNode request = Parse(
            """
            {
              "deck": { "deckId": "0193e97a-ef95-7700-8000-000000000001" },
              "preset": {
                "kind": "on-curve", "targetEntryId": "0193e97a-ef95-7700-8000-000000000013",
                "landRules": [ { "entryId": "0193e97a-ef95-7700-8000-000000000011", "rule": "not-modeled", "x": 1 } ],
                "turnLimit": 4, "onThePlay": true
              },
              "samples": 100, "sampleHands": 0
            }
            """);

        OperationInvalidInput? failure = Diagnose(request);

        Assert.NotNull(failure);
        Assert.Contains("'x'", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a wrongly typed number is reported with its path.
    /// </summary>
    [Fact]
    public void WrongType_IsReportedWithItsPath()
    {
        JsonNode request = Parse(Full);
        request["eachTurn"]![0]!["maxPerTurn"] = "one";

        OperationInvalidInput? failure = Diagnose(request);

        Assert.NotNull(failure);
        Assert.Equal("eachTurn[0].maxPerTurn", failure.Path);
    }

    /// <summary>
    /// Verifies the published input schema marks every required field of the rule and check records as required.
    /// </summary>
    [Fact]
    public void Schema_ListsTheRequiredFieldsOfEveryRecord()
    {
        using RegisteredToolSet registered = new(MtgMcp.App.Configuration.OperationMode.Remote);
        JsonElement schema = registered.Tools.Single(tool => tool.ProtocolTool.Name == "stats_simulate")
            .ProtocolTool.InputSchema;

        Assert.Equal(["deck", "sampleHands", "samples"], Required(schema).Order(StringComparer.Ordinal));
        JsonElement rules = schema.GetProperty("properties").GetProperty("eachTurn");
        string text = rules.GetRawText();
        foreach (string field in new[] { "pick", "maxPerTurn", "manaFrom", "effect", "cards", "cost", "targetCost" })
        {
            Assert.Contains($"\"{field}\"", text, StringComparison.Ordinal);
        }

        JsonElement mulligan = schema.GetProperty("properties").GetProperty("openingHand");
        Assert.Contains("freeFirstMulligan", mulligan.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("\"bottom\"", mulligan.GetRawText(), StringComparison.Ordinal);
        Assert.False(
            schema.TryGetProperty("additionalProperties", out JsonElement open) &&
            open.ValueKind == JsonValueKind.True);
    }

    /// <summary>
    /// Reads the required property names of a schema object.
    /// </summary>
    private static string[] Required(JsonElement schema)
    {
        return [.. schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()!)];
    }

    /// <summary>
    /// Runs the shared argument diagnosis for the tool.
    /// </summary>
    private OperationInvalidInput? Diagnose(JsonNode request)
    {
        return ToolCallPolicy.DiagnoseArguments(catalog, "stats_simulate", Arguments(request));
    }

    /// <summary>
    /// Splits a request into the argument dictionary the filter reads.
    /// </summary>
    private static Dictionary<string, JsonElement> Arguments(JsonNode request)
    {
        using JsonDocument document = JsonDocument.Parse(request.ToJsonString());
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
    }

    /// <summary>
    /// Parses JSON text into a node.
    /// </summary>
    private static JsonNode Parse(string json)
    {
        return JsonNode.Parse(json)!;
    }

    /// <summary>
    /// Finds the node at a path such as <c>eachTurn[1].manaFromByEntry[0]</c>.
    /// </summary>
    private static JsonNode Select(JsonNode root, string path)
    {
        JsonNode? node = root;
        foreach (string segment in path.Split('.'))
        {
            int bracket = segment.IndexOf('[', StringComparison.Ordinal);
            string name = bracket < 0 ? segment : segment[..bracket];
            node = node![name];
            if (bracket >= 0)
            {
                node = node![int.Parse(segment[(bracket + 1)..^1], System.Globalization.CultureInfo.InvariantCulture)];
            }
        }

        return node!;
    }

    /// <summary>
    /// Removes the property at a path.
    /// </summary>
    private static void Remove(JsonNode root, string path)
    {
        int dot = path.LastIndexOf('.');
        JsonNode parent = dot < 0 ? root : Select(root, path[..dot]);
        string last = dot < 0 ? path : path[(dot + 1)..];
        Assert.True(Assert.IsType<JsonObject>(parent).Remove(last), path);
    }
}
