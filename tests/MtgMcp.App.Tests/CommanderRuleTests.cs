using System.Text.Json;
using System.Text.Json.Nodes;
using MtgMcp.App.Decks;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Decks;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies the Commander rule facts of <c>local_deck_validate</c> over real Scryfall card data recorded for these
/// tests: size, commanders, identity, singleton, legality, Game Changers, pairing, and companions (RULE-001 to
/// RULE-004, cases R01 to R21).
/// </summary>
public sealed class CommanderRuleTests
{
    /// <summary>
    /// Holds the real cards recorded from Scryfall, by name.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, JsonObject> Recorded = CardJson.Recorded();

    /// <summary>
    /// Lists seven different mono-green or colorless cards that never break a rule on their own.
    /// </summary>
    private static readonly string[] GreenStaples =
    [
        "Llanowar Elves", "Birds of Paradise", "Cultivate", "Sol Ring", "Arcane Signet", "Command Tower",
        "Evolving Wilds",
    ];

    /// <summary>
    /// Verifies a legal 100-card deck passes every fact (case R01).
    /// </summary>
    [Fact]
    public void Evaluate_ALegalDeck_PassesEveryFact()
    {
        CommanderRuleFacts facts = CommanderRules.Evaluate(LegalDeck());

        Assert.Equal("commander", facts.Rules);
        Assert.True(facts.DeckSize.Passed);
        Assert.Equal(100, facts.DeckSize.Count);
        Assert.True(facts.Commanders.Passed);
        Assert.Equal("legendary-creature", Assert.Single(facts.Commanders.Items).Via);
        Assert.True(facts.ColorIdentity.Passed);
        Assert.Equal(["G"], facts.ColorIdentity.Identity);
        Assert.True(facts.Singleton.Passed);
        Assert.True(facts.Legality.Passed);
        Assert.Equal("known", facts.GameChangers.Status);
        Assert.Equal(0, facts.GameChangers.Count);
        Assert.Null(facts.Pairing);
        Assert.Empty(facts.Companions);
        Assert.Empty(facts.Unresolved);
    }

    /// <summary>
    /// Verifies a 99-card deck fails the size fact with the count (case R02).
    /// </summary>
    [Fact]
    public void Evaluate_A99CardDeck_FailsTheSizeFactWithTheCount()
    {
        List<RuleEntry> deck = LegalDeck();
        deck.RemoveAt(1);
        deck.Add(Entry("Forest", 91));

        CommanderRuleFacts facts = CommanderRules.Evaluate(deck);

        Assert.False(facts.DeckSize.Passed);
        Assert.Equal(99, facts.DeckSize.Count);
    }

    /// <summary>
    /// Verifies a hybrid card whose identity includes a color outside the commander's fails the identity fact and
    /// names the entry (case R03).
    /// </summary>
    [Fact]
    public void Evaluate_AHybridCardOutsideTheIdentity_FailsTheIdentityFactNamingTheEntry()
    {
        List<RuleEntry> deck = [.. LegalDeck(), Entry("Kitchen Finks", 1), Entry("Lightning Bolt", 1)];

        CommanderRuleFacts facts = CommanderRules.Evaluate(deck);

        Assert.False(facts.ColorIdentity.Passed);
        Assert.Equal(["Kitchen Finks", "Lightning Bolt"], facts.ColorIdentity.OffIdentity.Select(entry => entry.Name));
    }

    /// <summary>
    /// Verifies a repeated nonbasic fails singleton while 20 Forests and 10 Relentless Rats do not (case R04).
    /// </summary>
    [Fact]
    public void Evaluate_RepeatedNonbasic_FailsSingletonOnlyForThatCard()
    {
        List<RuleEntry> deck =
        [
            Entry("Kodama of the East Tree", 1, "commander"),
            Entry("Cultivate", 1),
            Entry("Cultivate", 1),
            Entry("Forest", 20),
            Entry("Relentless Rats", 10),
            Entry("Snow-Covered Forest", 5),
            Entry("Wastes", 3),
        ];

        SingletonFact singleton = CommanderRules.Evaluate(deck).Singleton;

        Assert.False(singleton.Passed);
        SingletonBreak broken = Assert.Single(singleton.Breaks);
        Assert.Equal(("Cultivate", 2, 1), (broken.Name, broken.Count, broken.Limit));
        Assert.Equal(2, broken.EntryIds.Count);
    }

    /// <summary>
    /// Verifies a banned card fails the legality fact naming it (case R05).
    /// </summary>
    [Fact]
    public void Evaluate_ABannedCard_FailsTheLegalityFactNamingIt()
    {
        List<RuleEntry> deck = [.. LegalDeck(), Entry("Black Lotus", 1)];

        LegalityFact legality = CommanderRules.Evaluate(deck).Legality;

        Assert.False(legality.Passed);
        LegalityIssue issue = Assert.Single(legality.Issues);
        Assert.Equal(("Black Lotus", "banned"), (issue.Name, issue.Status));
    }

    /// <summary>
    /// Verifies four Game Changers are counted with their list, and the fact has no passed flag and names no bracket
    /// (case R06 and RULE-002).
    /// </summary>
    [Fact]
    public void Evaluate_FourGameChangers_AreCountedWithoutAPassedFlagOrABracket()
    {
        List<RuleEntry> deck =
        [
            .. LegalDeck(),
            Entry("Rhystic Study", 1),
            Synthetic("Changer B", gameChanger: true),
            Synthetic("Changer C", gameChanger: true),
            Synthetic("Changer D", gameChanger: true),
        ];

        CommanderRuleFacts facts = CommanderRules.Evaluate(deck);

        Assert.Equal(4, facts.GameChangers.Count);
        Assert.Equal(4, facts.GameChangers.Entries.Count);
        string json = JsonSerializer.Serialize(facts, ToolCallPolicy.SerializerOptions);
        JsonNode changers = JsonNode.Parse(json)!["gameChangers"]!;
        Assert.Null(changers["passed"]);
        Assert.DoesNotContain("bracket", json, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies an entry whose data lacks the Game Changer field makes the fact unknown, naming it, while the count
    /// covers the other entries (case R09).
    /// </summary>
    [Fact]
    public void Evaluate_AnEntryWithoutGameChangerData_MakesTheFactUnknown()
    {
        List<RuleEntry> deck = [.. LegalDeck(), Entry("Rhystic Study", 1), Synthetic("No data", gameChanger: null)];

        GameChangersFact changers = CommanderRules.Evaluate(deck).GameChangers;

        Assert.Equal("unknown", changers.Status);
        Assert.Equal(1, changers.Count);
        Assert.Equal("No data", Assert.Single(changers.UnknownEntries!).Name);
    }

    /// <summary>
    /// Verifies two commanders are listed with their text and a pairing fact (case R07).
    /// </summary>
    [Fact]
    public void Evaluate_TwoCommanders_AreListedWithTheirTextAndAPairingFact()
    {
        CommanderRuleFacts facts = CommanderRules.Evaluate(
            [Entry("Tymna the Weaver", 1, "commander"), Entry("Thrasios, Triton Hero", 1, "commander")]);

        Assert.Equal(2, facts.Commanders.Items.Count);
        Assert.All(facts.Commanders.Items, item => Assert.False(string.IsNullOrEmpty(item.OracleText)));
        Assert.Equal("passed", facts.Pairing!.Status);
        Assert.Equal(["W", "U", "B", "G"], facts.ColorIdentity.Identity);
    }

    /// <summary>
    /// Verifies the pairing rows: Partner, Partner with, Friends forever, Choose a Background, Doctor's companion,
    /// and the other partner variants, each with a passing and a failing pair (cases R10 to R14).
    /// </summary>
    [Theory]
    [InlineData("Tymna the Weaver", "Thrasios, Triton Hero", "passed")]
    [InlineData("Pir, Imaginative Rascal", "Toothy, Imaginary Friend", "passed")]
    [InlineData("Will Kenrith", "Rowan Kenrith", "passed")]
    [InlineData("Pir, Imaginative Rascal", "Tymna the Weaver", "failed")]
    [InlineData("Toothy, Imaginary Friend", "Will Kenrith", "failed")]
    [InlineData("Elmar, Ulvenwald Informant", "Hargilde, Kindly Runechanter", "passed")]
    [InlineData("Elmar, Ulvenwald Informant", "Tymna the Weaver", "failed")]
    [InlineData("Joel, Resolute Survivor", "Ellie, Vengeful Hunter", "passed")]
    [InlineData("Leonardo, the Balance", "Donatello, the Brains", "passed")]
    [InlineData("Atreus, Impulsive Son", "Kratos, Stoic Father", "passed")]
    [InlineData("Atreus, Impulsive Son", "Joel, Resolute Survivor", "failed")]
    [InlineData("Wilson, Refined Grizzly", "Acolyte of Bahamut", "passed")]
    [InlineData("Acolyte of Bahamut", "Gale, Waterdeep Prodigy", "passed")]
    [InlineData("Wilson, Refined Grizzly", "Rhystic Study", "failed")]
    [InlineData("Wilson, Refined Grizzly", "Tymna the Weaver", "failed")]
    [InlineData("Ace, Fearless Rebel", "The Eleventh Doctor", "passed")]
    [InlineData("The Eighth Doctor", "Amy Pond", "passed")]
    [InlineData("Ace, Fearless Rebel", "Gandalf the Grey", "failed")]
    [InlineData("Gandalf the Grey", "Silas Renn, Seeker Adept", "failed")]
    [InlineData("Halana, Kessig Ranger", "Alena, Kessig Trapper", "passed")]
    [InlineData("Wilson, Refined Grizzly", "Agent of the Iron Throne", "passed")]
    [InlineData("Karlach, Raging Tiefling", "Acolyte of Bahamut", "failed")]
    public void Evaluate_Pairing_FollowsTheRulesTable(string first, string second, string status)
    {
        List<RuleEntry> deck =
        [
            Entry(first, 1, "commander"),
            Entry(second, 1, "commander"),
        ];

        PairingFact? pairing = CommanderRules.Evaluate(deck).Pairing;

        Assert.Equal(status, pairing!.Status);
        Assert.Equal(status == "passed", pairing.Passed);
    }

    /// <summary>
    /// Verifies a failed Partner with pair names the partner the card requires (case R11).
    /// </summary>
    [Fact]
    public void Evaluate_PartnerWithMismatch_NamesTheRequiredPartner()
    {
        PairingFact pairing = CommanderRules.Evaluate(
            [Entry("Pir, Imaginative Rascal", 1, "commander"), Entry("Tymna the Weaver", 1, "commander")]).Pairing!;

        Assert.Equal("failed", pairing.Status);
        Assert.Contains("Toothy, Imaginary Friend", pairing.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a Partner variant that the rules table does not list is unknown, naming the variant (case R15).
    /// </summary>
    [Fact]
    public void Evaluate_AnUnlistedPartnerVariant_IsUnknownWithTheVariantNamed()
    {
        ScryfallCard odd = Map(CardJson.Make(
            "Odd Partner",
            "Legendary Creature — Human",
            keywords: ["Partner"],
            oracleText: "Partner—Hypothetical (You can have two commanders if both have this ability.)"));
        RuleEntry other = Entry("Tymna the Weaver", 1, "commander");

        PairingFact pairing = CommanderRules.Evaluate(
            [new RuleEntry(DeckEntry("Odd Partner", 1, "commander"), odd, null), other]).Pairing!;

        Assert.Equal("unknown", pairing.Status);
        Assert.Null(pairing.Passed);
        Assert.Contains("Hypothetical", pairing.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a Background in the commander zone is eligible next to a Choose a Background commander and pairs with
    /// it (case R19), and is not eligible alone.
    /// </summary>
    [Fact]
    public void Evaluate_ABackgroundNextToAChooseABackgroundCommander_IsEligibleAndPaired()
    {
        CommanderRuleFacts together = CommanderRules.Evaluate(
            [Entry("Wilson, Refined Grizzly", 1, "commander"), Entry("Acolyte of Bahamut", 1, "commander")]);
        CommanderRuleFacts alone = CommanderRules.Evaluate([Entry("Acolyte of Bahamut", 1, "commander")]);

        Assert.All(together.Commanders.Items, item => Assert.True(item.Eligible));
        Assert.Equal("background", together.Commanders.Items[1].Via);
        Assert.Equal("passed", together.Pairing!.Status);
        Assert.False(alone.Commanders.Items[0].Eligible);
        Assert.False(alone.Commanders.Passed);
    }

    /// <summary>
    /// Verifies commander eligibility for each card kind in Comprehensive Rules 903.3: legendary creatures, Vehicles,
    /// Spacecraft with power and toughness, cards that say they can be a commander, and no one else.
    /// </summary>
    [Theory]
    [InlineData("Gandalf the Grey", true, "legendary-creature")]
    [InlineData("Valki, God of Lies // Tibalt, Cosmic Impostor", true, "legendary-creature")]
    [InlineData("Adrestia", true, "legendary-vehicle")]
    [InlineData("Esika's Chariot", true, "legendary-vehicle")]
    [InlineData("Dawnsire, Sunstar Dreadnought", true, "legendary-spacecraft")]
    [InlineData("Deep Space 9", false, null)]
    [InlineData("Aminatou, the Fateshifter", true, "can-be-your-commander")]
    [InlineData("Karlach, Raging Tiefling", true, "legendary-creature")]
    [InlineData("Heart of Kiran", true, "legendary-vehicle")]
    [InlineData("Smuggler's Copter", false, null)]
    [InlineData("Sol Ring", false, null)]
    [InlineData("Delver of Secrets // Insectile Aberration", false, null)]
    public void Evaluate_CommanderEligibility_FollowsRule903Point3(string name, bool eligible, string? via)
    {
        CommandersFact commanders = CommanderRules.Evaluate([Entry(name, 1, "commander")]).Commanders;

        CommanderItem item = Assert.Single(commanders.Items);
        Assert.Equal(eligible, item.Eligible);
        Assert.Equal(via, item.Via);
        Assert.Equal(eligible, commanders.Passed);
        Assert.Equal(eligible, item.Reason is null);
    }

    /// <summary>
    /// Verifies no commander and three commanders fail the commander fact with a reason.
    /// </summary>
    [Fact]
    public void Evaluate_ZeroOrThreeCommanders_FailTheCommanderFact()
    {
        CommandersFact none = CommanderRules.Evaluate([Entry("Forest", 1)]).Commanders;
        CommandersFact three = CommanderRules.Evaluate(
        [
            Entry("Tymna the Weaver", 1, "commander"),
            Entry("Thrasios, Triton Hero", 1, "commander"),
            Entry("Kodama of the East Tree", 1, "commander"),
        ]).Commanders;
        PairingFact twice = CommanderRules.Evaluate([Entry("Tymna the Weaver", 2, "commander")]).Pairing!;

        Assert.False(none.Passed);
        Assert.Contains("no commander", none.Reason, StringComparison.Ordinal);
        Assert.False(three.Passed);
        Assert.Contains("at most two", three.Reason, StringComparison.Ordinal);
        Assert.Equal("failed", twice.Status);
    }

    /// <summary>
    /// Verifies copy limits: seven Seven Dwarves pass and eight fail naming the card and its limit of 7, a Nazgul
    /// limit of 9 comes from its text, a card that merely searches for "up to three cards named" keeps the limit of 1,
    /// and "any number" cards have no limit (cases R04 and R20).
    /// </summary>
    [Fact]
    public void Evaluate_CopyLimits_ComeFromTheCardText()
    {
        CommanderRuleFacts seven = CommanderRules.Evaluate([Entry("Seven Dwarves", 7)]);
        CommanderRuleFacts eight = CommanderRules.Evaluate([Entry("Seven Dwarves", 4), Entry("Seven Dwarves", 4)]);
        CommanderRuleFacts others = CommanderRules.Evaluate(
        [
            Entry("Nazgûl", 9),
            Entry("Howling Wolf", 2),
            Entry("Hare Apparent", 30),
            Entry("Cid, Timeless Artificer", 12),
            Entry("Andromedan Automatons", 3),
        ]);

        Assert.True(seven.Singleton.Passed);
        SingletonBreak broken = Assert.Single(eight.Singleton.Breaks);
        Assert.Equal(("Seven Dwarves", 8, 7), (broken.Name, broken.Count, broken.Limit));
        Assert.Equal(
            [("Andromedan Automatons", 3, 1), ("Howling Wolf", 2, 1)],
            others.Singleton.Breaks.Select(value => (value.Name, value.Count, value.Limit)));
    }

    /// <summary>
    /// Verifies the checked companion restrictions with a passing and a failing deck (cases R16, R17, and R21).
    /// </summary>
    [Theory]
    [InlineData("Lurrus of the Dream-Den", "Kitchen Finks", "failed")]
    [InlineData("Lurrus of the Dream-Den", "Llanowar Elves", "passed")]
    [InlineData("Jegantha, the Wellspring", "Cultivate", "passed")]
    [InlineData("Jegantha, the Wellspring", "Kitchen Finks", "failed")]
    [InlineData("Jegantha, the Wellspring", "Counterspell", "failed")]
    [InlineData("Jegantha, the Wellspring", "Bonecrusher Giant // Stomp", "passed")]
    [InlineData("Gyruda, Doom of Depths", "Cultivate", "failed")]
    [InlineData("Gyruda, Doom of Depths", "Arcane Signet", "passed")]
    [InlineData("Obosh, the Preypiercer", "Arcane Signet", "failed")]
    [InlineData("Obosh, the Preypiercer", "Cultivate", "passed")]
    [InlineData("Obosh, the Preypiercer", "Mana Crypt", "failed")]
    [InlineData("Keruga, the Macrosage", "Sol Ring", "failed")]
    [InlineData("Keruga, the Macrosage", "Cultivate", "passed")]
    [InlineData("Kaheera, the Orphanguard", "Llanowar Elves", "failed")]
    [InlineData("Kaheera, the Orphanguard", "Universal Automaton", "passed")]
    [InlineData("Umori, the Collector", "Cultivate", "passed")]
    [InlineData("Umori, the Collector", "Llanowar Elves", "passed")]
    [InlineData("Yorion, Sky Nomad", "Cultivate", "failed")]
    [InlineData("Zirda, the Dawnwaker", "Cultivate", "not-checked")]
    public void Evaluate_CompanionRestrictions_AreCheckedFromCardData(string companion, string card, string status)
    {
        List<RuleEntry> deck = [Entry(card, 1), Entry(companion, 1, "companion")];

        CompanionFact fact = Assert.Single(CommanderRules.Evaluate(deck).Companions);

        Assert.Equal(status, fact.RestrictionStatus);
        Assert.False(string.IsNullOrEmpty(fact.RestrictionReason));
        if (status == "failed")
        {
            Assert.Contains("failed", fact.Status, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Verifies Umori fails when the nonland cards share no type, naming the cards that lack the most common type.
    /// </summary>
    [Fact]
    public void Evaluate_UmoriWithMixedTypes_FailsNamingTheCardsThatLackTheCommonType()
    {
        List<RuleEntry> deck =
        [
            Entry("Cultivate", 1),
            Entry("Rhystic Study", 1),
            Entry("Swords to Plowshares", 1),
            Entry("Umori, the Collector", 1, "companion"),
        ];

        CompanionFact umori = Assert.Single(CommanderRules.Evaluate(deck).Companions);

        Assert.Equal("failed", umori.RestrictionStatus);
        Assert.Equal(2, umori.RestrictionEntries.Count);
        Assert.Contains("most common", umori.RestrictionReason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a failed Lurrus names the entry that breaks it, and an unchecked companion reports its restriction
    /// text with a reason (cases R16 and R18).
    /// </summary>
    [Fact]
    public void Evaluate_CompanionFailureAndNotChecked_NameTheEntryAndTheReason()
    {
        RuleEntry finks = Entry("Kitchen Finks", 1);
        List<RuleEntry> lurrus =
        [
            Entry("Kodama of the East Tree", 1, "commander"),
            finks,
            Entry("Lurrus of the Dream-Den", 1, "companion"),
        ];
        List<RuleEntry> zirda = [Entry("Zirda, the Dawnwaker", 1, "companion")];

        CompanionFact failed = Assert.Single(CommanderRules.Evaluate(lurrus).Companions);
        CompanionFact notChecked = Assert.Single(CommanderRules.Evaluate(zirda).Companions);

        Assert.Contains(failed.RestrictionEntries, entry => entry.EntryId == finks.Entry.EntryId);
        Assert.Contains("mana value 2 or less", failed.Restriction, StringComparison.Ordinal);
        Assert.Equal("not-checked", notChecked.RestrictionStatus);
        Assert.Contains("rules text judgment", notChecked.RestrictionReason, StringComparison.Ordinal);
        Assert.Contains("activated ability", notChecked.Restriction, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies Lutri fails with a repeated nonland name, a companion outside the commanders' identity fails the
    /// identity check naming the companion, and a companion that is not legal in Commander fails (case R21).
    /// </summary>
    [Fact]
    public void Evaluate_LutriIdentityAndLegality_AreChecked()
    {
        List<RuleEntry> deck =
        [
            Entry("Kodama of the East Tree", 1, "commander"),
            Entry("Relentless Rats", 3),
            Entry("Lutri, the Spellchaser", 1, "companion"),
        ];
        ScryfallCard banned = Map(CardJson.Make("Banned Companion", "Legendary Creature — Cat", commander: "banned"));

        CompanionFact lutri = Assert.Single(CommanderRules.Evaluate(deck).Companions);
        CompanionFact illegal = Assert.Single(CommanderRules.Evaluate(
            [new RuleEntry(DeckEntry("Banned Companion", 1, "companion"), banned, null)]).Companions);

        Assert.Equal("failed", lutri.Status);
        Assert.False(lutri.InCommanderIdentity);
        Assert.Contains(lutri.RestrictionEntries, entry => entry.Name == "Relentless Rats");
        Assert.False(illegal.LegalInCommander);
        Assert.Equal("failed", illegal.Status);
    }

    /// <summary>
    /// Verifies entries with no Scryfall card are listed as unresolved and facts cover the rest.
    /// </summary>
    [Fact]
    public void Evaluate_EntriesWithoutACard_AreListedAsUnresolved()
    {
        List<RuleEntry> deck =
        [
            .. LegalDeck(),
            new RuleEntry(DeckEntry("Proxy", 1, "main"), null, "no-scryfall-identity"),
            new RuleEntry(DeckEntry("Mystery commander", 1, "commander"), null, "not-in-card-data"),
            new RuleEntry(DeckEntry("Sideboard thing", 1, "sideboard"), null, "not-in-card-data"),
        ];

        CommanderRuleFacts facts = CommanderRules.Evaluate(deck);

        Assert.Equal(["Proxy", "Mystery commander"], facts.Unresolved.Select(entry => entry.Name));
        Assert.Equal("no-scryfall-identity", facts.Unresolved[0].Reason);
        Assert.Contains(facts.Commanders.Items, item => !item.Eligible && item.Name == "Mystery commander");
        Assert.NotEmpty(facts.Limits);
    }

    /// <summary>
    /// Verifies a commander with no card data makes the color identity fact unknown: no entry is called outside an
    /// identity nobody could read, and a companion is not-checked rather than failed.
    /// </summary>
    [Fact]
    public void Evaluate_UnresolvedCommander_MakesTheColorIdentityUnknown()
    {
        List<RuleEntry> deck =
        [
            new RuleEntry(DeckEntry("Mystery commander", 1, "commander"), null, "no-scryfall-identity"),
            Entry("Forest", 1),
            Entry("Rhystic Study", 1),
            Entry("Jegantha, the Wellspring", 1, "companion"),
        ];

        CommanderRuleFacts facts = CommanderRules.Evaluate(deck);

        Assert.Null(facts.ColorIdentity.Passed);
        Assert.Empty(facts.ColorIdentity.OffIdentity);
        CompanionFact companion = Assert.Single(facts.Companions);
        Assert.Null(companion.InCommanderIdentity);
        Assert.Equal("not-checked", companion.Status);
    }

    /// <summary>
    /// Builds the legal deck of case R01: one green commander, seven different staples, and Forests to make 100.
    /// </summary>
    private static List<RuleEntry> LegalDeck()
    {
        List<RuleEntry> deck = [Entry("Kodama of the East Tree", 1, "commander"), Entry("Forest", 92)];
        deck.AddRange(GreenStaples.Select(name => Entry(name, 1)));
        return deck;
    }

    /// <summary>
    /// Builds an entry for a recorded card.
    /// </summary>
    private static RuleEntry Entry(string name, int quantity, string zone = "main")
    {
        return new RuleEntry(DeckEntry(name, quantity, zone), Map(Recorded[name]), null);
    }

    /// <summary>
    /// Builds an entry for a synthetic Game Changer card, or one without the field when null.
    /// </summary>
    private static RuleEntry Synthetic(string name, bool? gameChanger)
    {
        return new RuleEntry(
            DeckEntry(name, 1, "main"),
            Map(CardJson.Make(name, colors: ["G"], gameChanger: gameChanger)),
            null);
    }

    /// <summary>
    /// Builds a deck entry with its own ID.
    /// </summary>
    private static DeckEntry DeckEntry(string name, int quantity, string zone)
    {
        return new DeckEntry(Guid.NewGuid(), quantity, name, null, null, null, null, "en", "nonfoil", zone, 0);
    }

    /// <summary>
    /// Maps a card object the way installed card data is mapped.
    /// </summary>
    private static ScryfallCard Map(JsonObject card)
    {
        using JsonDocument document = JsonDocument.Parse(card.ToJsonString());
        return ScryfallMapper.Card(document.RootElement, DateTimeOffset.UnixEpoch, "test", includeRaw: false);
    }
}
