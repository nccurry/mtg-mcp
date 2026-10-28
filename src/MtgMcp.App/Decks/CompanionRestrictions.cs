using MtgMcp.Scryfall;

namespace MtgMcp.App.Decks;

/// <summary>
/// Checks the companion restrictions that card data decides without judgment. Companions the table does not list are
/// reported as not checked, never as a pass.
/// </summary>
internal static class CompanionRestrictions
{
    /// <summary>
    /// Says why a companion restriction is not checked.
    /// </summary>
    private const string NotCheckedReason = "restriction needs rules text judgment";

    /// <summary>
    /// Gives the number of cards a Yorion starting deck must hold: twenty more than the Commander minimum.
    /// </summary>
    private const int YorionMinimum = 120;

    /// <summary>
    /// Lists the card types Umori compares.
    /// </summary>
    private static readonly string[] CardTypes =
        ["Artifact", "Battle", "Creature", "Enchantment", "Instant", "Kindred", "Planeswalker", "Sorcery", "Tribal"];

    /// <summary>
    /// Lists the permanent card types, which Lurrus restricts.
    /// </summary>
    private static readonly string[] PermanentTypes =
        ["Artifact", "Battle", "Creature", "Enchantment", "Land", "Planeswalker"];

    /// <summary>
    /// Lists the creature types Kaheera allows.
    /// </summary>
    private static readonly string[] KaheeraTypes = ["Beast", "Cat", "Dinosaur", "Elemental", "Nightmare"];

    /// <summary>
    /// Checks the restriction of one companion against the starting deck, which is the commander and main zones.
    /// Returns the status (passed, failed, or not-checked), the reason, and the entries that break the restriction.
    /// </summary>
    internal static (string Status, string Reason, List<RuleEntryRef> Entries) Check(
        ScryfallCard companion,
        List<RuleEntry> starting)
    {
        List<RuleEntry> cards = [.. starting.Where(entry => entry.Card is not null)];
        string name = ScryfallCardSets.NormalizeName(CommanderCardText.FrontName(companion));
        return name switch
        {
            "gyruda, doom of depths" => Violations(
                cards, entry => ManaValue(entry) % 2 != 0, "Every card must have an even mana value."),
            "obosh, the preypiercer" => Violations(
                cards, entry => !IsLand(entry) && ManaValue(entry) % 2 == 0, "Every nonland card must have an odd " +
                    "mana value."),
            "keruga, the macrosage" => Violations(
                cards, entry => !IsLand(entry) && ManaValue(entry) < 3, "Every nonland card must have mana value 3 " +
                    "or more."),
            "lurrus of the dream-den" => Violations(
                cards, entry => IsPermanent(entry) && ManaValue(entry) > 2, "Every permanent card must have mana " +
                    "value 2 or less."),
            "jegantha, the wellspring" => Violations(
                cards, RepeatsASymbol, "No card may have more than one of the same mana symbol in its mana cost."),
            "kaheera, the orphanguard" => Violations(
                cards, BreaksKaheera, "Every creature card must be a Cat, Elemental, Nightmare, Dinosaur, or Beast."),
            "lutri, the spellchaser" => Lutri(cards),
            "umori, the collector" => Umori(cards),
            "yorion, sky nomad" => Yorion(starting),
            _ => ("not-checked", NotCheckedReason, []),
        };
    }

    /// <summary>
    /// Builds the result of a restriction that every counted entry must satisfy.
    /// </summary>
    private static (string Status, string Reason, List<RuleEntryRef> Entries) Violations(
        List<RuleEntry> cards,
        Func<RuleEntry, bool> breaks,
        string rule)
    {
        List<RuleEntryRef> offenders =
        [
            .. cards.Where(breaks).Select(entry => new RuleEntryRef(entry.Entry.EntryId, entry.Card!.Name)),
        ];
        return offenders.Count == 0
            ? ("passed", rule, [])
            : ("failed", $"{rule} {offenders.Count} entr{(offenders.Count == 1 ? "y breaks" : "ies break")} it.",
                offenders);
    }

    /// <summary>
    /// Checks that no two nonland cards have the same name.
    /// </summary>
    private static (string Status, string Reason, List<RuleEntryRef> Entries) Lutri(List<RuleEntry> cards)
    {
        List<RuleEntryRef> offenders = [];
        foreach (IGrouping<string, RuleEntry> group in cards
            .Where(entry => !IsLand(entry))
            .GroupBy(entry => ScryfallCardSets.NormalizeName(entry.Card!.Name), StringComparer.Ordinal))
        {
            if (group.Sum(entry => entry.Entry.Quantity) > 1)
            {
                offenders.AddRange(group.Select(entry => new RuleEntryRef(entry.Entry.EntryId, entry.Card!.Name)));
            }
        }

        const string rule = "Every nonland card must have a different name.";
        return offenders.Count == 0 ? ("passed", rule, []) : ("failed", rule, offenders);
    }

    /// <summary>
    /// Checks that every nonland card shares one card type; on a failure it names the cards that lack the type most
    /// cards share.
    /// </summary>
    private static (string Status, string Reason, List<RuleEntryRef> Entries) Umori(List<RuleEntry> cards)
    {
        List<RuleEntry> nonland = [.. cards.Where(entry => !IsLand(entry))];
        const string rule = "Every nonland card must share a card type.";
        if (nonland.Count == 0)
        {
            return ("passed", rule, []);
        }

        string best = CardTypes
            .OrderByDescending(type => nonland.Count(entry => HasType(entry, type)))
            .ThenBy(type => type, StringComparer.Ordinal)
            .First();
        List<RuleEntryRef> lacking =
        [
            .. nonland.Where(entry => !HasType(entry, best)).Select(entry => new RuleEntryRef(entry.Entry.EntryId,
                entry.Card!.Name)),
        ];
        return lacking.Count == 0
            ? ("passed", rule, [])
            : ("failed", $"{rule} No type is shared; {best} is the most common.", lacking);
    }

    /// <summary>
    /// Checks that the starting deck is at least twenty cards over the Commander minimum.
    /// </summary>
    private static (string Status, string Reason, List<RuleEntryRef> Entries) Yorion(List<RuleEntry> starting)
    {
        int size = starting.Sum(entry => entry.Entry.Quantity);
        return size >= YorionMinimum
            ? ("passed", $"The starting deck has {size} cards.", [])
            : (
                "failed",
                $"The starting deck must have at least {YorionMinimum} cards (20 more than the minimum), but a " +
                $"Commander deck has exactly 100 and this one has {size}.",
                []);
    }

    /// <summary>
    /// Reports whether a creature card is outside the types Kaheera allows; a changeling counts as every type.
    /// </summary>
    private static bool BreaksKaheera(RuleEntry entry)
    {
        ScryfallCard card = entry.Card!;
        if (!CommanderCardText.FrontTypes(card).Contains("Creature", StringComparer.Ordinal) ||
            CommanderCardText.HasKeyword(card, "Changeling"))
        {
            return false;
        }

        return !CommanderCardText.FrontSubtypes(card).Any(type => KaheeraTypes.Contains(type, StringComparer.Ordinal));
    }

    /// <summary>
    /// Reports whether a card's mana cost has the same symbol twice.
    /// </summary>
    private static bool RepeatsASymbol(RuleEntry entry)
    {
        ScryfallCard card = entry.Card!;
        string? cost = string.IsNullOrEmpty(card.ManaCost) && card.Faces.Count > 0 ? card.Faces[0].ManaCost
            : card.ManaCost;
        return CommanderCardText.SymbolCounts(cost).Values.Any(count => count > 1);
    }

    /// <summary>
    /// Reads the mana value of an entry's card.
    /// </summary>
    private static decimal ManaValue(RuleEntry entry)
    {
        return entry.Card!.ManaValue ?? 0m;
    }

    /// <summary>
    /// Reports whether an entry's card is a land.
    /// </summary>
    private static bool IsLand(RuleEntry entry)
    {
        return HasType(entry, "Land");
    }

    /// <summary>
    /// Reports whether an entry's card is a permanent card.
    /// </summary>
    private static bool IsPermanent(RuleEntry entry)
    {
        return PermanentTypes.Any(type => HasType(entry, type));
    }

    /// <summary>
    /// Reports whether the front face of an entry's card has a card type.
    /// </summary>
    private static bool HasType(RuleEntry entry, string type)
    {
        return CommanderCardText.FrontTypes(entry.Card!).Contains(type, StringComparer.Ordinal);
    }
}
