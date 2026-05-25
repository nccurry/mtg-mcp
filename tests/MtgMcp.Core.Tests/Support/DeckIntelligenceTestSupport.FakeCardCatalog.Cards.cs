namespace MtgMcp.Core.Tests;

/// <summary>
/// Contains fake card factory data for deck intelligence tests.
/// </summary>
public sealed partial class DeckIntelligenceTests
{
    /// <summary>
    /// Provides card data for deck intelligence tests.
    /// </summary>
    private sealed partial class FakeCardCatalog
    {
        /// <summary>
        /// Creates a fake card.
        /// </summary>
        private static CardInfo CreateCard(string name)
        {
            return name switch
            {
                "Arcane Signet" => new CardInfo
                {
                    Id = "arcane-signet",
                    OracleId = "oracle-arcane-signet",
                    Name = "Arcane Signet",
                    ManaCost = "{2}",
                    ManaValue = 2,
                    TypeLine = "Artifact",
                    OracleText = "{T}: Add one mana of any color in your commander's color identity.",
                    ColorIdentity = [],
                    ProducedMana = ["W", "U", "B", "R", "G"],
                    EdhrecRank = 5,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "1.00" }
                },
                "Phyrexian Arena" => new CardInfo
                {
                    Id = "phyrexian-arena",
                    OracleId = "oracle-phyrexian-arena",
                    Name = "Phyrexian Arena",
                    ManaCost = "{1}{B}{B}",
                    ManaValue = 3,
                    TypeLine = "Enchantment",
                    OracleText = "At the beginning of your upkeep, you draw a card and you lose 1 life.",
                    ColorIdentity = ["B"],
                    EdhrecRank = 250,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["commander"] = "legal",
                        ["modern"] = "legal"
                    },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "3.00" }
                },
                "Rhystic Study" => new CardInfo
                {
                    Id = "rhystic-study",
                    OracleId = "oracle-rhystic-study",
                    Name = "Rhystic Study",
                    ManaCost = "{2}{U}",
                    ManaValue = 3,
                    TypeLine = "Enchantment",
                    OracleText = "Whenever an opponent casts a spell, you may draw a card unless that player pays {1}.",
                    ColorIdentity = ["U"],
                    EdhrecRank = 20,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["commander"] = "legal",
                        ["modern"] = "not_legal"
                    },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "40.00" }
                },
                "Necropotence" => new CardInfo
                {
                    Id = "necropotence",
                    OracleId = "oracle-necropotence",
                    Name = "Necropotence",
                    ManaCost = "{B}{B}{B}",
                    ManaValue = 3,
                    TypeLine = "Enchantment",
                    OracleText = "Skip your draw step. Pay 1 life: Exile the top card of your library face down.",
                    ColorIdentity = ["B"],
                    EdhrecRank = 30,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["commander"] = "not_legal",
                        ["modern"] = "not_legal"
                    },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "25.00" }
                },
                "Lightning Greaves" => new CardInfo
                {
                    Id = "lightning-greaves",
                    OracleId = "oracle-lightning-greaves",
                    Name = "Lightning Greaves",
                    ManaCost = "{2}",
                    ManaValue = 2,
                    TypeLine = "Artifact — Equipment",
                    OracleText = "Equipped creature has haste and shroud. Equip {0}.",
                    ColorIdentity = [],
                    EdhrecRank = 40,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "6.00" }
                },
                "Hero's Downfall" => new CardInfo
                {
                    Id = "heros-downfall",
                    OracleId = "oracle-heros-downfall",
                    Name = "Hero's Downfall",
                    ManaCost = "{1}{B}{B}",
                    ManaValue = 3,
                    TypeLine = "Instant",
                    OracleText = "Destroy target creature or planeswalker.",
                    ColorIdentity = ["B"],
                    EdhrecRank = 3_000,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "0.25" }
                },
                "Command Tower" => new CardInfo
                {
                    Id = "command-tower",
                    OracleId = "oracle-command-tower",
                    Name = "Command Tower",
                    TypeLine = "Land",
                    OracleText = "{T}: Add one mana of any color in your commander's color identity.",
                    ColorIdentity = [],
                    ProducedMana = ["W", "U", "B", "R", "G"],
                    EdhrecRank = 10,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "1.50" }
                },
                "Temple of Silence" => new CardInfo
                {
                    Id = "temple-of-silence",
                    OracleId = "oracle-temple-of-silence",
                    Name = "Temple of Silence",
                    TypeLine = "Land",
                    OracleText = "Temple of Silence enters the battlefield tapped. When it enters, scry 1. {T}: Add {W} or {B}.",
                    ColorIdentity = [],
                    ProducedMana = ["W", "B"],
                    EdhrecRank = 1,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "0.20" }
                },
                "Opt" => new CardInfo
                {
                    Id = "opt",
                    OracleId = "oracle-opt",
                    Name = "Opt",
                    ManaCost = "{U}",
                    ManaValue = 1,
                    TypeLine = "Instant",
                    OracleText = "Scry 1. Draw a card.",
                    ColorIdentity = ["U"],
                    EdhrecRank = 100,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "0.10" }
                },
                "Syphon Mind" => new CardInfo
                {
                    Id = "syphon-mind",
                    OracleId = "oracle-syphon-mind",
                    Name = "Syphon Mind",
                    ManaCost = "{3}{B}",
                    ManaValue = 4,
                    TypeLine = "Sorcery",
                    OracleText = "Each other player discards a card. You draw a card for each card discarded this way.",
                    ColorIdentity = ["B"],
                    EdhrecRank = 2_500,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "0.50" }
                },
                "Waste Not" => new CardInfo
                {
                    Id = "waste-not",
                    OracleId = "oracle-waste-not",
                    Name = "Waste Not",
                    ManaCost = "{1}{B}",
                    ManaValue = 2,
                    TypeLine = "Enchantment",
                    OracleText = "Whenever an opponent discards a creature card, create a 2/2 black Zombie creature token. Whenever an opponent discards a land card, add {B}{B}. Whenever an opponent discards a noncreature, nonland card, draw a card.",
                    ColorIdentity = ["B"],
                    EdhrecRank = 1_400,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "2.00" }
                },
                "Geth's Grimoire" => new CardInfo
                {
                    Id = "geths-grimoire",
                    OracleId = "oracle-geths-grimoire",
                    Name = "Geth's Grimoire",
                    ManaCost = "{4}",
                    ManaValue = 4,
                    TypeLine = "Artifact",
                    OracleText = "Whenever an opponent discards a card, you may draw a card.",
                    ColorIdentity = [],
                    EdhrecRank = 1_800,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "4.00" }
                },
                "Torment of Hailfire" => new CardInfo
                {
                    Id = "torment-of-hailfire",
                    OracleId = "oracle-torment-of-hailfire",
                    Name = "Torment of Hailfire",
                    ManaCost = "{X}{B}{B}",
                    ManaValue = 2,
                    TypeLine = "Sorcery",
                    OracleText = "Repeat the following process X times. Each opponent loses 3 life unless they sacrifice a nonland permanent or discard a card.",
                    ColorIdentity = ["B"],
                    EdhrecRank = 400,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "8.00" }
                },
                "Zulaport Cutthroat" => new CardInfo
                {
                    Id = "zulaport-cutthroat",
                    OracleId = "oracle-zulaport-cutthroat",
                    Name = "Zulaport Cutthroat",
                    ManaCost = "{1}{B}",
                    ManaValue = 2,
                    TypeLine = "Creature — Human Rogue Ally",
                    OracleText = "Whenever Zulaport Cutthroat or another creature you control dies, each opponent loses 1 life and you gain 1 life.",
                    ColorIdentity = ["B"],
                    EdhrecRank = 800,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "1.00" }
                },
                "Mirkwood Bats" => new CardInfo
                {
                    Id = "mirkwood-bats",
                    OracleId = "oracle-mirkwood-bats",
                    Name = "Mirkwood Bats",
                    ManaCost = "{3}{B}",
                    ManaValue = 4,
                    TypeLine = "Creature — Bat",
                    OracleText = "Whenever you create or sacrifice a token, each opponent loses 1 life.",
                    ColorIdentity = ["B"],
                    EdhrecRank = 900,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "0.75" }
                },
                "Blasphemous Act" => new CardInfo
                {
                    Id = "blasphemous-act",
                    OracleId = "oracle-blasphemous-act",
                    Name = "Blasphemous Act",
                    ManaCost = "{8}{R}",
                    ManaValue = 9,
                    TypeLine = "Sorcery",
                    OracleText = "Blasphemous Act deals 13 damage to each creature.",
                    ColorIdentity = ["R"],
                    EdhrecRank = 300,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "3.00" }
                },
                "Court of Ambition" => new CardInfo
                {
                    Id = "court-of-ambition",
                    OracleId = "oracle-court-of-ambition",
                    Name = "Court of Ambition",
                    ManaCost = "{2}{B}{B}",
                    ManaValue = 4,
                    TypeLine = "Enchantment",
                    OracleText = "When Court of Ambition enters the battlefield, you become the monarch. At the beginning of your upkeep, each opponent loses 3 life unless they discard a card.",
                    ColorIdentity = ["B"],
                    EdhrecRank = 2_200,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "4.50" }
                },
                "Illness in the Ranks" => new CardInfo
                {
                    Id = "illness-in-the-ranks",
                    OracleId = "oracle-illness-in-the-ranks",
                    Name = "Illness in the Ranks",
                    ManaCost = "{B}",
                    ManaValue = 1,
                    TypeLine = "Enchantment",
                    OracleText = "Creature tokens get -1/-1.",
                    ColorIdentity = ["B"],
                    EdhrecRank = 8_000,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "1.00" }
                },
                "Crawlspace" => new CardInfo
                {
                    Id = "crawlspace",
                    OracleId = "oracle-crawlspace",
                    Name = "Crawlspace",
                    ManaCost = "{3}",
                    ManaValue = 3,
                    TypeLine = "Artifact",
                    OracleText = "No more than two creatures can attack you each combat.",
                    ColorIdentity = [],
                    EdhrecRank = 2_000,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "4.00" }
                },
                "Season of Loss" => new CardInfo
                {
                    Id = "season-of-loss",
                    OracleId = "oracle-season-of-loss",
                    Name = "Season of Loss",
                    ManaCost = "{3}{B}{B}",
                    ManaValue = 5,
                    TypeLine = "Sorcery",
                    OracleText = "Choose modes. Each opponent sacrifices a creature. Create two tapped creature tokens. You draw two cards.",
                    Set = "tst",
                    ReleasedAt = new DateOnly(2026, 2, 1),
                    ColorIdentity = ["B"],
                    EdhrecRank = 1_500,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "2.00" }
                },
                _ => new CardInfo
                {
                    Id = name.ToLowerInvariant().Replace(' ', '-'),
                    OracleId = $"oracle-{name}",
                    Name = name,
                    ManaCost = "{1}",
                    ManaValue = name.Equals("Sol Ring", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
                    TypeLine = "Artifact",
                    OracleText = "{T}: Add {C}{C}.",
                    ColorIdentity = [],
                    ProducedMana = ["C"],
                    EdhrecRank = 1,
                    Legalities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["commander"] = "legal" },
                    Prices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["usd"] = "1.25" }
                }
            };
        }
    }
}
