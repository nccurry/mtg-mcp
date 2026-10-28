using System.ComponentModel.DataAnnotations;

namespace MtgMcp.App.Hosting;

/// <summary>
/// Publishes the cache policies accepted by Scryfall evidence tools as a schema enum.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
internal sealed class FreshnessPolicyValuesAttribute()
    : AllowedValuesAttribute("default", "cache-only", "refresh");

/// <summary>
/// Publishes the deck interchange format IDs as a schema enum; the interchange catalog must list the same IDs.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
internal sealed class InterchangeFormatValuesAttribute()
    : AllowedValuesAttribute("mtg-mcp-json-v1", "generic-text-v1", "archidekt-text-v1", "moxfield-bulk-edit-v1");

/// <summary>
/// Publishes the community tag kinds as a schema enum.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
internal sealed class TagKindValuesAttribute()
    : AllowedValuesAttribute("oracle", "art");
