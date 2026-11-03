using System.ComponentModel.DataAnnotations;

namespace MtgMcp.App.Decks;

/// <summary>
/// Publishes the response formats of the local deck read as a schema enum: the tool has a short form and the full
/// stored form, and no raw provider objects.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
internal sealed class DeckResponseFormatValuesAttribute()
    : AllowedValuesAttribute("concise", "standard");
