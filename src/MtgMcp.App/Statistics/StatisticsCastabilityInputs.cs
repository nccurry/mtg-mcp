using System.ComponentModel;
using System.Text.Json.Serialization;

namespace MtgMcp.App.Statistics;

/// <summary>
/// Chooses the entries of a mana source group by ID.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(CastabilityEntryIdsSelect), "entry-ids")]
[JsonDerivedType(typeof(CastabilityCategoryIdsSelect), "category-ids")]
internal abstract record CastabilitySelectInput;

/// <summary>
/// Selects exact entry IDs.
/// </summary>
internal sealed record CastabilityEntryIdsSelect(
    [property: Description("One or more exact entry UUIDs.")] IReadOnlyList<Guid> Ids)
    : CastabilitySelectInput;

/// <summary>
/// Selects every entry assigned to one of the categories.
/// </summary>
internal sealed record CastabilityCategoryIdsSelect(
    [property: Description("One or more exact category UUIDs.")] IReadOnlyList<Guid> Ids)
    : CastabilitySelectInput;

/// <summary>
/// Gives the colors of the listed entries when they differ from the group's default colors.
/// </summary>
internal sealed record CastabilityProducesOverride(
    [property: Description("Entries of the group that make these colors instead of the default.")]
    IReadOnlyList<Guid> EntryIds,
    [property: Description("One or more of W, U, B, R, G, C that each of these entries can make.")]
    IReadOnlyList<string> Colors);

/// <summary>
/// Says which colors the cards of a group can make, one unit each.
/// </summary>
internal sealed record CastabilityProducesInput(
    [property: Description("One or more of W, U, B, R, G, C that each card of the group can make.")]
    IReadOnlyList<string> Default,
    [property: Description("Optional entries that make other colors than the default.")]
    IReadOnlyList<CastabilityProducesOverride>? ByEntry = null);

/// <summary>
/// Declares one group of mana sources chosen by the caller.
/// </summary>
internal sealed record CastabilitySourceGroupInput(
    [property: Description("Unique name for the group.")] string Id,
    [property: Description("Which entries are in the group. Groups must not overlap.")]
    CastabilitySelectInput Select,
    [property: Description("The colors the group's cards can make.")] CastabilityProducesInput Produces);

/// <summary>
/// Chooses the turn each spell is checked on.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ManaValueTurnInput), "mana-value")]
[JsonDerivedType(typeof(FixedTurnInput), "fixed")]
internal abstract record CastabilityTurnInput;

/// <summary>
/// Checks each spell on the turn equal to its mana value, at least turn 1.
/// </summary>
internal sealed record ManaValueTurnInput : CastabilityTurnInput;

/// <summary>
/// Checks every spell on one turn.
/// </summary>
internal sealed record FixedTurnInput(
    [property: Description("The turn number to check every spell on, from 1 to 99.")] int Turn)
    : CastabilityTurnInput;
