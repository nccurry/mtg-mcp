using System.Diagnostics;
using System.Text;
using FluentAssertions;
using MtgMcp.Core;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Emits report-only hot-path timings for release planning without making CI timing a hard gate.
/// </summary>
public sealed class PerformanceRatchetReportTests
{
    /// <summary>
    /// Writes current hot-path timings beside generous report-only budgets.
    /// </summary>
    [Fact]
    public void HotPathTimings_ReportCurrentRatchet()
    {
        List<PerformanceReportRow> rows =
        [
            Measure(
                "deck-analysis-wide-600",
                250,
                () =>
                {
                    DeckAnalysis analysis = DeckAnalyzer.Analyze(CreateWideWorkspace(600));
                    return analysis.IncludedCards
                        + analysis.RoleCounts.Count
                        + analysis.TagCounts.Count
                        + analysis.TypeCounts.Count
                        + analysis.ManaCurve.Count;
                }),
            Measure(
                "role-classifier-1000",
                75,
                () =>
                {
                    DeckCard[] cards = CreateRepresentativeCards();
                    double score = 0;
                    for (int index = 0; index < 1_000; index++)
                    {
                        CardRoleAssignment assignment = DeckRoleClassifier.Classify(cards[index % cards.Length]);
                        score += assignment.PrimaryRole.Length + assignment.Tags.Count;
                    }

                    return score;
                }),
            Measure(
                "performance-analysis-1000-sims",
                5_000,
                () =>
                {
                    DeckPerformanceAnalysis analysis = DeckPerformanceAnalyzer.Analyze(
                        CreateCommanderPerformanceDeck(),
                        "commander-default",
                        simulations: 1_000,
                        maxTurn: 6,
                        seed: 2026,
                        includeMulligans: true,
                        CancellationToken.None);

                    return analysis.OpeningHands.SevenCardKeepRate
                        + analysis.Commander.CastByTurn.Count
                        + analysis.Scenarios.Count;
                }),
        ];

        rows.Should().OnlyContain(row => row.ElapsedMilliseconds >= 0);
        rows.Should().OnlyContain(row => row.Result > 0);

        string report = BuildReport(rows);
        string artifactsDirectory = Path.Combine(FindRepositoryRoot(), "artifacts");
        Directory.CreateDirectory(artifactsDirectory);
        File.WriteAllText(Path.Combine(artifactsDirectory, "performance-report.txt"), report);

        Console.WriteLine(report);
    }

    /// <summary>
    /// Measures one hot path and returns a row for the report-only ratchet artifact.
    /// </summary>
    private static PerformanceReportRow Measure(string name, double budgetMilliseconds, Func<double> operation)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        double result = operation();
        stopwatch.Stop();

        return new PerformanceReportRow(
            name,
            stopwatch.Elapsed.TotalMilliseconds,
            budgetMilliseconds,
            result);
    }

    /// <summary>
    /// Creates a markdown timing report that CI can publish or display.
    /// </summary>
    private static string BuildReport(List<PerformanceReportRow> rows)
    {
        StringBuilder report = new();
        report.AppendLine("Performance ratchet report (report-only)");
        report.AppendLine();
        report.AppendLine("| Hot path | Elapsed ms | Report budget ms | Status |");
        report.AppendLine("| --- | ---: | ---: | --- |");
        foreach (PerformanceReportRow row in rows)
        {
            string status = row.ElapsedMilliseconds <= row.BudgetMilliseconds
                ? "within-report-budget"
                : "over-report-budget";
            report.AppendLine(FormattableString.Invariant(
                $"| {row.Name} | {row.ElapsedMilliseconds:F1} | {row.BudgetMilliseconds:F0} | {status} |"));
        }

        report.AppendLine();
        report.AppendLine("Budgets are informational and intentionally do not fail CI.");
        return report.ToString();
    }

    /// <summary>
    /// Creates a Commander deck with representative lands, ramp, draw, interaction, and win routes.
    /// </summary>
    private static DeckWorkspace CreateCommanderPerformanceDeck()
    {
        return new DeckWorkspace
        {
            Id = "performance-report-commander",
            Name = "Performance Report Azorius Value",
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckRoles.Commander, IncludedInDeck = true },
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Lands, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Ramp, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Draw, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Interaction, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Protection, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Wincons, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Synergy, IncludedInDeck = true },
            ],
            Cards =
            [
                Card("Performance Report Commander", 1, DeckRoles.Commander, "Legendary Creature - Advisor", "{2}{W}{U}", 4, "Whenever you draw your second card each turn, create a 1/1 creature token.", ["W", "U"]),
                CreateLand("Plains", ["W"], quantity: 17),
                CreateLand("Island", ["U"], quantity: 17),
                CreateLand("Command Tower", ["W", "U"], quantity: 2),
                Card("Azorius Signet", 8, DeckRoles.Ramp, "Artifact", "{2}", 2, "{1}, {T}: Add {W}{U}.", [], ["W", "U"]),
                Card("Chart a Course", 10, DeckRoles.Draw, "Sorcery", "{1}{U}", 2, "Draw two cards, then discard a card unless you attacked this turn.", ["U"]),
                Card("Counterspell", 10, DeckRoles.Interaction, "Instant", "{U}{U}", 2, "Counter target spell.", ["U"]),
                Card("Swiftfoot Boots", 6, DeckRoles.Protection, "Artifact - Equipment", "{2}", 2, "Equipped creature has hexproof and haste.", []),
                Card("Token Engine", 12, DeckRoles.Synergy, "Creature - Artificer", "{2}{W}", 3, "Whenever you draw a card, create a 1/1 creature token.", ["W"]),
                Card("Combo A", 2, DeckRoles.Synergy, "Artifact", "{2}", 2, "Combo. Untap target permanent. Copy target activated ability.", []),
                Card("Combo B", 2, DeckRoles.Synergy, "Artifact", "{2}", 2, "Whenever an ability is copied, untap target permanent.", []),
                Card("Overwhelming Finale", 4, DeckRoles.Wincons, "Sorcery", "{5}{W}{U}", 7, "Creatures you control get +X/+X and gain flying until end of turn.", ["W", "U"]),
                Card("Utility Spell", 8, DeckDefaults.Mainboard, "Sorcery", "{3}", 3, "Scry 2.", []),
            ],
        };
    }

    /// <summary>
    /// Creates a wide workspace with many distinct cards for whole-deck analyzer scaling.
    /// </summary>
    private static DeckWorkspace CreateWideWorkspace(int distinctCards)
    {
        DeckWorkspace workspace = new()
        {
            Id = $"performance-report-wide-{distinctCards}",
            Name = "Performance Report Wide Deck",
            Format = "commander",
            Categories =
            [
                new DeckCategory { Name = DeckDefaults.Mainboard, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Lands, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Ramp, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Draw, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Interaction, IncludedInDeck = true },
                new DeckCategory { Name = DeckRoles.Wincons, IncludedInDeck = true },
            ],
        };

        for (int index = 0; index < distinctCards; index++)
        {
            workspace.Cards.Add(CreateWideCard(index));
        }

        return workspace;
    }

    /// <summary>
    /// Creates representative cards for role-classifier timing.
    /// </summary>
    private static DeckCard[] CreateRepresentativeCards()
    {
        return
        [
            CreateLand("Island", ["U"]),
            CreateLand("Command Tower", ["W", "U", "B", "R", "G"]),
            Card("Arcane Signet", 1, DeckRoles.Ramp, "Artifact", "{2}", 2, "{T}: Add one mana of any color.", []),
            Card("Rhystic Study", 1, DeckRoles.Draw, "Enchantment", "{2}{U}", 3, "Whenever an opponent casts a spell, you may draw a card unless that player pays {1}.", ["U"]),
            Card("Swords to Plowshares", 1, DeckRoles.Interaction, "Instant", "{W}", 1, "Exile target creature. Its controller gains life.", ["W"]),
            Card("Supreme Verdict", 1, DeckRoles.BoardWipes, "Sorcery", "{1}{W}{W}{U}", 4, "Destroy all creatures.", ["W", "U"]),
            Card("Lightning Greaves", 1, DeckRoles.Protection, "Artifact - Equipment", "{2}", 2, "Equipped creature has shroud and haste.", []),
            Card("Reanimate", 1, DeckRoles.Recursion, "Sorcery", "{B}", 1, "Return target creature card from a graveyard to the battlefield.", ["B"]),
            Card("Demonic Tutor", 1, DeckRoles.Tutors, "Sorcery", "{1}{B}", 2, "Search your library for a card, put that card into your hand, then shuffle.", ["B"]),
            Card("Craterhoof Behemoth", 1, DeckRoles.Wincons, "Creature - Beast", "{5}{G}{G}{G}", 8, "Creatures you control get +X/+X and gain trample until end of turn.", ["G"]),
            Card("Blood Artist", 1, DeckRoles.Payoffs, "Creature - Vampire", "{1}{B}", 2, "Whenever Blood Artist or another creature dies, target player loses 1 life and you gain 1 life.", ["B"]),
            Card("Combo Engine", 1, DeckRoles.Synergy, "Artifact", "{3}", 3, "Combo. Untap target permanent and copy target activated ability.", []),
        ];
    }

    /// <summary>
    /// Creates a deterministic wide-card fixture for a given index.
    /// </summary>
    private static DeckCard CreateWideCard(int index)
    {
        return (index % 8) switch
        {
            0 => CreateLand($"Performance Report Island {index}", ["U"]),
            1 => Card($"Performance Report Ramp {index}", 1, DeckRoles.Ramp, "Artifact", "{2}", 2, "{T}: Add one mana of any color.", []),
            2 => Card($"Performance Report Draw {index}", 1, DeckRoles.Draw, "Instant", "{2}{U}", 3, "Draw two cards, then discard a card.", ["U"]),
            3 => Card($"Performance Report Removal {index}", 1, DeckRoles.Interaction, "Instant", "{1}{W}", 2, "Exile target creature.", ["W"]),
            4 => Card($"Performance Report Wipe {index}", 1, DeckRoles.BoardWipes, "Sorcery", "{2}{W}{W}", 4, "Destroy all creatures.", ["W"]),
            5 => Card($"Performance Report Protection {index}", 1, DeckRoles.Protection, "Artifact - Equipment", "{2}", 2, "Equipped creature has hexproof.", []),
            6 => Card($"Performance Report Finisher {index}", 1, DeckRoles.Wincons, "Creature - Avatar", "{5}{G}{G}", 7, "Creatures you control get +X/+X and gain trample until end of turn.", ["G"]),
            _ => Card($"Performance Report Synergy {index}", 1, DeckRoles.Synergy, "Creature - Wizard", "{2}{U}", 3, "Whenever you draw a card, create a token.", ["U"]),
        };
    }

    /// <summary>
    /// Creates a land card with explicit produced mana.
    /// </summary>
    private static DeckCard CreateLand(
        string name,
        List<string> producedMana,
        string typeLine = "Basic Land",
        string oracleText = "",
        int quantity = 1)
    {
        return Card(name, quantity, DeckRoles.Lands, typeLine, null, 0, oracleText, [], producedMana);
    }

    /// <summary>
    /// Creates a benchmark card with cached snapshot data.
    /// </summary>
    private static DeckCard Card(
        string name,
        int quantity,
        string category,
        string typeLine,
        string? manaCost,
        double manaValue,
        string oracleText,
        List<string> colorIdentity,
        List<string>? producedMana = null)
    {
        return new DeckCard
        {
            Name = name,
            Quantity = quantity,
            PrimaryCategory = category,
            Categories = [category],
            Snapshot = new CardSnapshot
            {
                TypeLine = typeLine,
                ManaCost = manaCost,
                ManaValue = manaValue,
                OracleText = oracleText,
                ColorIdentity = colorIdentity,
                ProducedMana = producedMana ?? [],
            },
        };
    }

    /// <summary>
    /// Finds the repository root from the test output directory.
    /// </summary>
    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "mtg-mcp.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to find repository root.");
    }

    /// <summary>
    /// Captures one measured hot path and its report-only budget.
    /// </summary>
    private sealed record PerformanceReportRow(
        string Name,
        double ElapsedMilliseconds,
        double BudgetMilliseconds,
        double Result);
}
