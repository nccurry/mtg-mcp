using System.ComponentModel.DataAnnotations;

namespace MtgMcp.App.Archidekt;

/// <summary>
/// Publishes the response formats the Archidekt tools offer as a schema enum. Archidekt has no raw provider objects to
/// add, so there is no full format.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
internal sealed class ArchidektResponseFormatValuesAttribute()
    : AllowedValuesAttribute("concise", "standard");
