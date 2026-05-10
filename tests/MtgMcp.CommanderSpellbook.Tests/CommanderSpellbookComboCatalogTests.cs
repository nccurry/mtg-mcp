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

        report.Combos.Should().ContainSingle(combo => combo.Name.Contains("Basalt Monolith", StringComparison.OrdinalIgnoreCase)
            && combo.WinRoute.Contains("Infinite colorless mana", StringComparison.OrdinalIgnoreCase));
        report.NearMisses.Should().ContainSingle(combo => combo.MissingCards.Contains("Forsaken Monument"));
        mockHttp.VerifyNoOutstandingExpectation();
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
            Options.Create(new CommanderSpellbookOptions { BaseAddress = new Uri("https://spellbook.test/") }));
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
}
