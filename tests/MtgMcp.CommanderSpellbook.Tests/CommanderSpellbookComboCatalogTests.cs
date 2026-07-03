using FluentAssertions;
using Microsoft.Extensions.Options;
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
    /// Verifies the corpus provider reports disabled and enabled source capabilities accurately.
    /// </summary>
    [Fact]
    public void CorpusProvider_GetStatus_ReflectsConfiguredEnablement()
    {
        FakeComboCatalog catalog = new(new DeckComboReport());
        MtgMcpOptions disabledOptions = new();
        disabledOptions.Intelligence.Sources["CommanderSpellbook"] = new MtgMcpCorpusSourceOptions
        {
            Enabled = false
        };
        CommanderSpellbookCorpusSignalProvider disabled = new(catalog, Options.Create(disabledOptions));
        MtgMcpOptions enabledOptions = EnabledCorpusOptions();
        CommanderSpellbookCorpusSignalProvider enabled = new(catalog, Options.Create(enabledOptions));

        disabled.GetStatus().Status.Should().Be(CorpusSourceStatusKind.Disabled);
        disabled.GetStatus().Enabled.Should().BeFalse();
        enabled.GetStatus().Status.Should().Be(CorpusSourceStatusKind.Available);
        enabled.GetStatus().Enabled.Should().BeTrue();
        enabled.GetStatus().StableApi.Should().BeTrue();
        enabled.GetStatus().AttributionRequired.Should().BeTrue();
    }

    /// <summary>
    /// Verifies the corpus provider converts near misses into attributed missing-card signals.
    /// </summary>
    [Fact]
    public async Task CorpusProvider_GetSignals_MapsNearMissesAndNotes()
    {
        DeckComboReport comboReport = new()
        {
            NearMisses =
            [
                new DeckCombo
                {
                    Name = "Basalt loop",
                    WinRoute = "Infinite colorless mana",
                    Confidence = 0.85,
                    MissingCards = ["Rings of Brighthearth", "Forsaken Monument"]
                },
                new DeckCombo { Name = "Complete", MissingCards = [] }
            ],
            Notes = ["fixture note"]
        };
        FakeComboCatalog catalog = new(comboReport);
        CommanderSpellbookCorpusSignalProvider provider = new(
            catalog,
            Options.Create(EnabledCorpusOptions()));
        CorpusSignalQuery query = new()
        {
            ExistingCards = ["Basalt Monolith"],
            Commander = "Karn, Legacy Reforged",
            Format = "commander",
            Refresh = true
        };

        CorpusSignalReport report = await provider.GetSignalsAsync(
            query,
            new RecommendationAnalysisBudget { IncludeComboDetails = true },
            TestContext.Current.CancellationToken);

        report.Signals.Should().HaveCount(2);
        report.Signals.Should().OnlyContain(signal => signal.Source == "Commander Spellbook");
        report.Signals.Should().Contain(signal => signal.CardName == "Rings of Brighthearth"
            && signal.Score == 0.85
            && signal.Rationale.Contains("Basalt loop", StringComparison.Ordinal));
        report.Notes.Should().Contain("fixture note");
        catalog.LastQuery.Should().NotBeNull();
        catalog.LastQuery!.CardNames.Should().Equal("Basalt Monolith");
        catalog.LastQuery.Refresh.Should().BeTrue();
    }

    /// <summary>
    /// Verifies disabled budgets and empty card pools do not call the remote combo catalog.
    /// </summary>
    [Fact]
    public async Task CorpusProvider_GetSignals_ShortCircuitsUnavailableInputs()
    {
        FakeComboCatalog catalog = new(new DeckComboReport());
        CommanderSpellbookCorpusSignalProvider provider = new(
            catalog,
            Options.Create(EnabledCorpusOptions()));

        CorpusSignalReport noDetails = await provider.GetSignalsAsync(
            new CorpusSignalQuery { ExistingCards = ["Sol Ring"] },
            new RecommendationAnalysisBudget { IncludeComboDetails = false },
            TestContext.Current.CancellationToken);
        CorpusSignalReport noCards = await provider.GetSignalsAsync(
            new CorpusSignalQuery(),
            new RecommendationAnalysisBudget { IncludeComboDetails = true },
            TestContext.Current.CancellationToken);

        noDetails.Signals.Should().BeEmpty();
        noCards.Signals.Should().BeEmpty();
        catalog.CallCount.Should().Be(0);
    }

    /// <summary>
    /// Creates options with the Commander Spellbook evidence source enabled.
    /// </summary>
    private static MtgMcpOptions EnabledCorpusOptions()
    {
        MtgMcpOptions options = new();
        options.Intelligence.Sources["CommanderSpellbook"] = new MtgMcpCorpusSourceOptions { Enabled = true };
        return options;
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
    /// Supplies deterministic combo reports to corpus-provider tests.
    /// </summary>
    private sealed class FakeComboCatalog : IComboCatalog
    {
        /// <summary>
        /// Stores the report returned from deck-level combo queries.
        /// </summary>
        private readonly DeckComboReport report;

        /// <summary>
        /// Creates a fake around one immutable test report reference.
        /// </summary>
        public FakeComboCatalog(DeckComboReport report)
        {
            this.report = report;
        }

        /// <summary>
        /// Counts deck-level combo calls made by the provider.
        /// </summary>
        public int CallCount { get; private set; }

        /// <summary>
        /// Captures the most recent normalized combo query.
        /// </summary>
        public ComboCatalogQuery? LastQuery { get; private set; }

        /// <inheritdoc/>
        public Task<DeckComboReport> FindCombosAsync(
            ComboCatalogQuery query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastQuery = query;
            return Task.FromResult(report);
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<ComboEvidence>> SearchCombosByCardAsync(
            ComboCardSearchQuery query,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public Task<ComboEvidence?> GetComboDetailsAsync(
            ComboDetailsQuery query,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
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
