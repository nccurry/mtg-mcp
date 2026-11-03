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

/// <summary>
/// Publishes the card response formats as a schema enum; the Scryfall card format parser accepts the same values.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
internal sealed class ResponseFormatValuesAttribute()
    : AllowedValuesAttribute("concise", "standard", "full");

/// <summary>
/// Publishes the two response formats of tools that have no raw provider objects to add, so no full format, as a
/// schema enum.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
internal sealed class ConciseResponseFormatValuesAttribute()
    : AllowedValuesAttribute("concise", "standard");

/// <summary>
/// Publishes the bulk metadata cache policies a card-data sync accepts as a schema enum. A sync has no cache-only
/// value because it cannot install anything without the network.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
internal sealed class SyncFreshnessPolicyValuesAttribute()
    : AllowedValuesAttribute("default", "refresh");
