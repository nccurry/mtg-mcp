using FluentAssertions;
using Microsoft.Extensions.Options;
using MtgMcp.CommanderSpellbook;
using MtgMcp.Core;
using RichardSzalay.MockHttp;

namespace MtgMcp.CommanderSpellbook.Tests;

/// <summary>
/// Contains tests for Commander Spellbook combo catalog behavior.
/// </summary>
public sealed class CommanderSpellbookComboCatalogTests
{
    /// <summary>
    /// Verifies that find-my-combos responses map completed combos and near misses.
    /// </summary>
    [Fact]
    public async Task FindCombos_MapsIncludedAndAlmostIncludedCombos()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp.When(HttpMethod.Post, "https://spellbook.test/find-my-combos")
            .WithContent("Basalt Monolith\nRings of Brighthearth")
            .Respond("application/json", ComboResponseJson);
        CommanderSpellbookComboCatalog catalog = CreateCatalog(mockHttp);

        DeckComboReport report = await catalog.FindCombosAsync(
            new ComboCatalogQuery
            {
                CardNames = ["Rings of Brighthearth", "Basalt Monolith"],
                Format = "commander"
            },
            TestContext.Current.CancellationToken);
        report.Combos[0].Cards.Add("mutated");
        DeckComboReport cached = await catalog.FindCombosAsync(
            new ComboCatalogQuery
            {
                CardNames = ["Basalt Monolith", "Rings of Brighthearth"],
                Format = "commander"
            },
            TestContext.Current.CancellationToken);

        report.Combos.Should().ContainSingle(combo => combo.Name.Contains("Basalt Monolith", StringComparison.OrdinalIgnoreCase)
            && combo.WinRoute.Contains("Infinite colorless mana", StringComparison.OrdinalIgnoreCase));
        cached.Combos.Should().ContainSingle(combo => !combo.Cards.Contains("mutated"));
        cached.NearMisses.Should().ContainSingle(combo => combo.MissingCards.Contains("Forsaken Monument"));
        mockHttp.VerifyNoOutstandingExpectation();
    }

    /// <summary>
    /// Verifies that unexpected response shapes return an empty report with a diagnostic note.
    /// </summary>
    [Fact]
    public async Task FindCombos_MissingResultsReturnsEmptyReport()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp.When(HttpMethod.Post, "https://spellbook.test/find-my-combos")
            .WithContent("Sol Ring")
            .Respond("application/json", """{ "detail": "temporarily unavailable" }""");
        CommanderSpellbookComboCatalog catalog = CreateCatalog(mockHttp);

        DeckComboReport report = await catalog.FindCombosAsync(
            new ComboCatalogQuery
            {
                CardNames = ["Sol Ring"],
                Format = "commander"
            },
            TestContext.Current.CancellationToken);

        report.Combos.Should().BeEmpty();
        report.NearMisses.Should().BeEmpty();
        report.Notes.Should().Contain(note =>
            note.Contains("did not include combo results", StringComparison.OrdinalIgnoreCase));
        mockHttp.VerifyNoOutstandingExpectation();
    }

    /// <summary>
    /// Verifies that card-scoped combo search preserves raw catalog fields.
    /// </summary>
    [Fact]
    public async Task SearchCombosByCard_MapsRawCatalogEvidence()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp.When(HttpMethod.Get, "https://spellbook.test/variants*")
            .Respond("application/json", VariantSearchJson);
        CommanderSpellbookComboCatalog catalog = CreateCatalog(mockHttp);

        IReadOnlyList<ComboEvidence> combos = await catalog.SearchCombosByCardAsync(
            new ComboCardSearchQuery
            {
                CardName = "Basalt Monolith",
                Format = "commander",
                Limit = 5
            },
            TestContext.Current.CancellationToken);

        ComboEvidence combo = combos.Should().ContainSingle().Subject;
        combo.ComboId.Should().Be("4131-4235");
        combo.Cards.Should().Contain(["Rings of Brighthearth", "Basalt Monolith"]);
        combo.ProducedFeatures.Should().Contain("Infinite colorless mana");
        combo.Templates.Should().Contain("mana rock");
        combo.Prerequisites.Should().Contain("Basalt Monolith can tap.");
        combo.Prerequisites.Should().Contain("No summoning sickness.");
        combo.Steps.Should().Contain("Activate Basalt Monolith.");
        combo.Steps.Should().Contain("Repeat the untap loop.");
        combo.BracketTag.Should().Be("bracket-3");
        combo.Legalities.Should().ContainKey("commander").WhoseValue.Should().BeTrue();
        combo.RouteClassifications.Single().RouteTypes.Should().Contain(WinRouteLabels.InfiniteMana);
        combo.Metadata.Notes.Should().Contain(note => note.Contains("catalog evidence", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that combo detail lookup maps one variant object.
    /// </summary>
    [Fact]
    public async Task GetComboDetails_MapsVariantObject()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp.When(HttpMethod.Get, "https://spellbook.test/variants/4131-4235")
            .Respond("application/json", VariantObjectJson);
        CommanderSpellbookComboCatalog catalog = CreateCatalog(mockHttp);

        ComboEvidence? combo = await catalog.GetComboDetailsAsync(
            new ComboDetailsQuery { ComboId = "4131-4235" },
            TestContext.Current.CancellationToken);

        combo.Should().NotBeNull();
        combo!.ComboId.Should().Be("4131-4235");
        combo.ColorIdentity.Should().BeEmpty();
        combo.SourceUri.Should().Contain("4131-4235");
    }

    /// <summary>
    /// Verifies that configured User-Agent values flow through catalog options.
    /// </summary>
    [Fact]
    public void Constructor_AppliesConfiguredUserAgent()
    {
        MockHttpMessageHandler mockHttp = new();
        HttpClient httpClient = mockHttp.ToHttpClient();
        httpClient.BaseAddress = new Uri("https://spellbook.test/");

        _ = new CommanderSpellbookComboCatalog(
            httpClient,
            Options.Create(new CommanderSpellbookOptions
            {
                BaseAddress = new Uri("https://spellbook.test/"),
                UserAgent = "spellbook-test/1.0",
            }),
            new MemoryCorpusCache(new MtgMcpCorpusCacheOptions()),
            Options.Create(new MtgMcpOptions()));

        httpClient.DefaultRequestHeaders.UserAgent.ToString().Should().Be("spellbook-test/1.0");
    }

    /// <summary>
    /// Creates a catalog with a mocked HTTP client.
    /// </summary>
    private static CommanderSpellbookComboCatalog CreateCatalog(MockHttpMessageHandler mockHttp)
    {
        HttpClient httpClient = mockHttp.ToHttpClient();
        httpClient.BaseAddress = new Uri("https://spellbook.test/");
        return new CommanderSpellbookComboCatalog(
            httpClient,
            Options.Create(new CommanderSpellbookOptions { BaseAddress = new Uri("https://spellbook.test/") }),
            new MemoryCorpusCache(new MtgMcpCorpusCacheOptions()),
            Options.Create(new MtgMcpOptions()));
    }

    /// <summary>
    /// Stores a representative Commander Spellbook response.
    /// </summary>
    private const string ComboResponseJson =
        """
        {
          "results": {
            "included": [
              {
                "id": "4131-4235",
                "uses": [
                  { "card": { "name": "Rings of Brighthearth" } },
                  { "card": { "name": "Basalt Monolith" } }
                ],
                "produces": [
                  { "feature": { "name": "Infinite colorless mana" } }
                ],
                "requires": [],
                "description": "Repeat the untap loop."
              }
            ],
            "almostIncluded": [
              {
                "id": "4131-4547",
                "uses": [
                  { "card": { "name": "Basalt Monolith" } },
                  { "card": { "name": "Forsaken Monument" } }
                ],
                "produces": [
                  { "feature": { "name": "Infinite colorless mana" } }
                ],
                "requires": [],
                "description": "Needs a mana doubler."
              }
            ]
          }
        }
        """;

    /// <summary>
    /// Stores a representative Commander Spellbook variants response.
    /// </summary>
    private const string VariantSearchJson =
        """
        {
          "count": null,
          "next": null,
          "previous": null,
          "results": [
            {
              "id": "4131-4235",
              "identity": ["C"],
              "bracketTag": { "name": "bracket-3" },
              "popularity": 18,
              "uses": [
                { "card": { "name": "Rings of Brighthearth" } },
                { "card": { "name": "Basalt Monolith" } }
              ],
              "produces": [
                { "feature": { "name": "Infinite colorless mana" } }
              ],
              "requires": [
                { "template": { "name": "mana rock" } }
              ],
              "legalities": {
                "commander": true,
                "modern": false
              },
              "prerequisites": [
                { "description": "Basalt Monolith can tap." }
              ],
              "steps": [
                { "instruction": "Activate Basalt Monolith." }
              ],
              "easyPrerequisites": "No summoning sickness.",
              "description": "Repeat the untap loop."
            },
            {
              "id": "illegal-modern-only",
              "identity": "C",
              "uses": [
                { "card": { "name": "Rings of Brighthearth" } },
                { "card": { "name": "Basalt Monolith" } }
              ],
              "produces": [
                { "feature": { "name": "Infinite colorless mana" } }
              ],
              "requires": [],
              "legalities": {
                "commander": false,
                "modern": true
              },
              "description": "This row should be filtered for Commander searches."
            }
          ]
        }
        """;

    /// <summary>
    /// Stores a representative Commander Spellbook variant object.
    /// </summary>
    private const string VariantObjectJson =
        """
        {
          "id": "4131-4235",
          "identity": "C",
          "uses": [
            { "card": { "name": "Rings of Brighthearth" } },
            { "card": { "name": "Basalt Monolith" } }
          ],
          "produces": [
            { "feature": { "name": "Infinite colorless mana" } }
          ],
          "requires": [],
          "description": "Repeat the untap loop."
        }
        """;
}
