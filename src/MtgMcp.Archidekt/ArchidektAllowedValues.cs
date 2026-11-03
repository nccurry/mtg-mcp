using System.ComponentModel.DataAnnotations;

namespace MtgMcp.Archidekt;

/// <summary>
/// Publishes the visibility values Archidekt folders accept as a schema enum; folders have no unlisted state.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class FolderVisibilityValuesAttribute()
    : AllowedValuesAttribute("private", "public");

/// <summary>
/// Publishes the deck formats Archidekt deck creation maps to provider formats as a schema enum.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class DeckFormatValuesAttribute()
    : AllowedValuesAttribute(
        "standard", "modern", "commander", "edh", "legacy", "vintage", "pauper", "pioneer", "brawl", "historic",
        "oathbreaker");

/// <summary>
/// Publishes the visibility values Archidekt decks accept as a schema enum.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class DeckVisibilityValuesAttribute()
    : AllowedValuesAttribute("private", "unlisted", "public");
