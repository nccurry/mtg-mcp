using MtgMcp.Scryfall;

namespace MtgMcp.App.Decks;

/// <summary>
/// Decides whether two commanders may be paired, from the partner abilities that Scryfall's keywords and card text
/// show. The rows follow Comprehensive Rules 702.124; an ability the table does not list is reported as unknown, never
/// as a pass or a fail.
/// </summary>
internal static class CommanderPairing
{
    /// <summary>
    /// Lists the creature types that make a Time Lord Doctor, which must have no other creature types.
    /// </summary>
    private static readonly string[] TimeLordDoctorTypes = ["Doctor", "Lord", "Time"];

    /// <summary>
    /// Checks one pair of commanders.
    /// </summary>
    internal static PairingFact Check(RuleEntry first, RuleEntry second)
    {
        ScryfallCard a = first.Card!;
        ScryfallCard b = second.Card!;
        List<PairingAbility> abilitiesA = AbilitiesOf(a);
        List<PairingAbility> abilitiesB = AbilitiesOf(b);
        List<PairingCard> cards =
        [
            new PairingCard(first.Entry.EntryId, a.Name, abilitiesA),
            new PairingCard(second.Entry.EntryId, b.Name, abilitiesB),
        ];
        string? matched = MatchingRow(a, b, abilitiesA, abilitiesB);
        if (matched is not null)
        {
            return new PairingFact("passed", true, cards, $"The commanders can be paired: {matched}.");
        }

        string? unlisted = UnlistedVariant(abilitiesA) ?? UnlistedVariant(abilitiesB);
        if (unlisted is not null)
        {
            return new PairingFact(
                "unknown",
                null,
                cards,
                $"A commander has the partner variant '{unlisted}', which the rules table does not list, so the " +
                "pairing is not judged.");
        }

        return new PairingFact("failed", false, cards, FailureReason(a, b, abilitiesA, abilitiesB));
    }

    /// <summary>
    /// Reads the pairing abilities and traits of one card.
    /// </summary>
    private static List<PairingAbility> AbilitiesOf(ScryfallCard card)
    {
        List<PairingAbility> abilities = [];
        string? with = CommanderCardText.HasKeyword(card, "Partner with")
            ? CommanderCardText.PartnerWithName(card)
            : null;
        IReadOnlyList<string> variants = CommanderCardText.PartnerVariants(card);
        if (with is not null)
        {
            abilities.Add(new PairingAbility("partner-with", with));
        }

        foreach (string variant in variants)
        {
            abilities.Add(new PairingAbility("partner-variant", variant));
        }

        if (CommanderCardText.HasKeyword(card, "Partner") && with is null && variants.Count == 0)
        {
            abilities.Add(new PairingAbility("partner", null));
        }

        if (CommanderCardText.HasKeyword(card, "Choose a Background"))
        {
            abilities.Add(new PairingAbility("choose-a-background", null));
        }

        if (CommanderCardText.HasKeyword(card, "Doctor's companion"))
        {
            abilities.Add(new PairingAbility("doctors-companion", null));
        }

        if (CommanderCardText.IsBackground(card))
        {
            abilities.Add(new PairingAbility("background-card", null));
        }

        if (IsTimeLordDoctor(card))
        {
            abilities.Add(new PairingAbility("time-lord-doctor", null));
        }

        return abilities;
    }

    /// <summary>
    /// Finds the rule row that lets the two cards be paired, or null when none does.
    /// </summary>
    private static string? MatchingRow(
        ScryfallCard a,
        ScryfallCard b,
        List<PairingAbility> abilitiesA,
        List<PairingAbility> abilitiesB)
    {
        if (Has(abilitiesA, "partner") && Has(abilitiesB, "partner"))
        {
            return "both have Partner";
        }

        if (NamesEachOther(a, b, abilitiesA, abilitiesB))
        {
            return "each has Partner with the other's name";
        }

        string? variant = SharedKnownVariant(abilitiesA, abilitiesB);
        if (variant is not null)
        {
            return $"both have Partner—{variant}";
        }

        if ((Has(abilitiesA, "choose-a-background") && Has(abilitiesB, "background-card")) ||
            (Has(abilitiesB, "choose-a-background") && Has(abilitiesA, "background-card")))
        {
            return "Choose a Background with a legendary Background";
        }

        if ((Has(abilitiesA, "doctors-companion") && Has(abilitiesB, "time-lord-doctor")) ||
            (Has(abilitiesB, "doctors-companion") && Has(abilitiesA, "time-lord-doctor")))
        {
            return "Doctor's companion with a Time Lord Doctor";
        }

        return null;
    }

    /// <summary>
    /// Reports whether each card has a Partner with ability that names the other card.
    /// </summary>
    private static bool NamesEachOther(
        ScryfallCard a,
        ScryfallCard b,
        List<PairingAbility> abilitiesA,
        List<PairingAbility> abilitiesB)
    {
        return Names(abilitiesA, b) && Names(abilitiesB, a);
    }

    /// <summary>
    /// Reports whether a list of abilities has a Partner with ability that names a card.
    /// </summary>
    private static bool Names(List<PairingAbility> abilities, ScryfallCard card)
    {
        string full = ScryfallCardSets.NormalizeName(card.Name);
        string front = ScryfallCardSets.NormalizeName(CommanderCardText.FrontName(card));
        return abilities.Any(ability => ability.Kind == "partner-with" && ability.Detail is not null &&
            ScryfallCardSets.NormalizeName(ability.Detail) is var named && (named == full || named == front));
    }

    /// <summary>
    /// Finds a partner variant that both cards have and the rules table lists.
    /// </summary>
    private static string? SharedKnownVariant(List<PairingAbility> abilitiesA, List<PairingAbility> abilitiesB)
    {
        foreach (PairingAbility ability in abilitiesA.Where(ability => ability.Kind == "partner-variant"))
        {
            string? known = CommanderCardText.KnownPartnerVariants.FirstOrDefault(
                variant => variant.Equals(ability.Detail, StringComparison.OrdinalIgnoreCase));
            if (known is not null && abilitiesB.Any(other => other.Kind == "partner-variant" &&
                string.Equals(other.Detail, ability.Detail, StringComparison.OrdinalIgnoreCase)))
            {
                return known;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds a partner variant on a card that the rules table does not list.
    /// </summary>
    private static string? UnlistedVariant(List<PairingAbility> abilities)
    {
        return abilities
            .Where(ability => ability.Kind == "partner-variant")
            .Select(ability => ability.Detail)
            .FirstOrDefault(detail => !CommanderCardText.KnownPartnerVariants.Contains(
                detail!,
                StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Explains why two cards cannot be paired, naming the partner a Partner with card requires.
    /// </summary>
    private static string FailureReason(
        ScryfallCard a,
        ScryfallCard b,
        List<PairingAbility> abilitiesA,
        List<PairingAbility> abilitiesB)
    {
        PairingAbility? withA = abilitiesA.FirstOrDefault(ability => ability.Kind == "partner-with");
        if (withA is not null && !Names(abilitiesA, b))
        {
            return $"{a.Name} has Partner with {withA.Detail}, so its only partner is {withA.Detail}.";
        }

        PairingAbility? withB = abilitiesB.FirstOrDefault(ability => ability.Kind == "partner-with");
        if (withB is not null && !Names(abilitiesB, a))
        {
            return $"{b.Name} has Partner with {withB.Detail}, so its only partner is {withB.Detail}.";
        }

        return "The commanders have no partner ability in common. Different partner abilities cannot be combined.";
    }

    /// <summary>
    /// Reports whether a list of abilities holds one kind.
    /// </summary>
    private static bool Has(List<PairingAbility> abilities, string kind)
    {
        return abilities.Any(ability => ability.Kind == kind);
    }

    /// <summary>
    /// Reports whether a card is a legendary Time Lord Doctor creature with no other creature types.
    /// </summary>
    private static bool IsTimeLordDoctor(ScryfallCard card)
    {
        IReadOnlyList<string> types = CommanderCardText.FrontTypes(card);
        return types.Contains("Legendary", StringComparer.Ordinal) &&
            types.Contains("Creature", StringComparer.Ordinal) &&
            CommanderCardText.FrontSubtypes(card).Order(StringComparer.Ordinal).SequenceEqual(TimeLordDoctorTypes);
    }
}
