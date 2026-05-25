using MtgMcp.Core;

namespace MtgMcp.Benchmarks;

/// <summary>
/// Builds deterministic offline fixtures for benchmark coverage.
/// </summary>
internal static class BenchmarkDeckFactory
{
    /// <summary>
    /// Creates a Commander deck with representative lands, ramp, draw, interaction, and win routes.
    /// </summary>
    public static DeckWorkspace CreateCommanderPerformanceDeck()
    {
        return new DeckWorkspace
        {
            Id = "benchmark-commander",
            Name = "Benchmark Azorius Value",
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
                Card("Benchmark Commander", 1, DeckRoles.Commander, "Legendary Creature - Advisor", "{2}{W}{U}", 4, "Whenever you draw your second card each turn, create a 1/1 creature token.", ["W", "U"]),
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
            ]
        };
    }

    /// <summary>
    /// Creates a wide workspace with many distinct cards for whole-deck analyzer scaling.
    /// </summary>
    public static DeckWorkspace CreateWideWorkspace(int distinctCards)
    {
        DeckWorkspace workspace = new()
        {
            Id = $"benchmark-wide-{distinctCards}",
            Name = "Benchmark Wide Deck",
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
    /// Creates representative cards for role-classifier benchmarks.
    /// </summary>
    public static DeckCard[] CreateRepresentativeCards()
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
    /// Creates a spell card with the supplied mana cost and text.
    /// </summary>
    public static DeckCard CreateSpell(
        string name,
        string manaCost,
        double manaValue,
        string oracleText,
        List<string> colorIdentity)
    {
        return Card(name, 1, DeckDefaults.Mainboard, "Instant", manaCost, manaValue, oracleText, colorIdentity);
    }

    /// <summary>
    /// Creates a land card with explicit produced mana.
    /// </summary>
    public static DeckCard CreateLand(
        string name,
        List<string> producedMana,
        string typeLine = "Basic Land",
        string oracleText = "",
        int quantity = 1)
    {
        return Card(name, quantity, DeckRoles.Lands, typeLine, null, 0, oracleText, [], producedMana);
    }

    /// <summary>
    /// Creates a card facet snapshot with workspace, tagger, and numeric metadata facets.
    /// </summary>
    public static CardFacetSnapshot CreateFacetSnapshot()
    {
        return new CardFacetSnapshot
        {
            WorkspaceId = "benchmark-workspace",
            CardName = "Chart a Course",
            Quantity = 1,
            IncludedInDeck = true,
            Facets = new Dictionary<string, CardFacet>(StringComparer.OrdinalIgnoreCase)
            {
                [CardFacetNames.WorkspacePrimaryCategory] = Facet(
                    CardFacetNames.WorkspacePrimaryCategory,
                    CardFacetSourceNames.Workspace,
                    [DeckRoles.Draw]),
                [CardFacetNames.TaggerOracleTags] = Facet(
                    CardFacetNames.TaggerOracleTags,
                    CardFacetSourceNames.Tagger,
                    ["card draw", "discard outlet"]),
                ["metadata.mana_value"] = Facet(
                    "metadata.mana_value",
                    CardFacetSourceNames.Metadata,
                    ["2"]),
            },
        };
    }

    /// <summary>
    /// Creates a deterministic wide-card fixture for a given index.
    /// </summary>
    private static DeckCard CreateWideCard(int index)
    {
        return (index % 8) switch
        {
            0 => CreateLand($"Benchmark Island {index}", ["U"]),
            1 => Card($"Benchmark Ramp {index}", 1, DeckRoles.Ramp, "Artifact", "{2}", 2, "{T}: Add one mana of any color.", []),
            2 => Card($"Benchmark Draw {index}", 1, DeckRoles.Draw, "Instant", "{2}{U}", 3, "Draw two cards, then discard a card.", ["U"]),
            3 => Card($"Benchmark Removal {index}", 1, DeckRoles.Interaction, "Instant", "{1}{W}", 2, "Exile target creature.", ["W"]),
            4 => Card($"Benchmark Wipe {index}", 1, DeckRoles.BoardWipes, "Sorcery", "{2}{W}{W}", 4, "Destroy all creatures.", ["W"]),
            5 => Card($"Benchmark Protection {index}", 1, DeckRoles.Protection, "Artifact - Equipment", "{2}", 2, "Equipped creature has hexproof.", []),
            6 => Card($"Benchmark Finisher {index}", 1, DeckRoles.Wincons, "Creature - Avatar", "{5}{G}{G}", 7, "Creatures you control get +X/+X and gain trample until end of turn.", ["G"]),
            _ => Card($"Benchmark Synergy {index}", 1, DeckRoles.Synergy, "Creature - Wizard", "{2}{U}", 3, "Whenever you draw a card, create a token.", ["U"]),
        };
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
    /// Creates a normalized facet object.
    /// </summary>
    private static CardFacet Facet(string name, string source, List<string> values)
    {
        return new CardFacet
        {
            Name = name,
            Source = source,
            Values = values,
        };
    }
}
