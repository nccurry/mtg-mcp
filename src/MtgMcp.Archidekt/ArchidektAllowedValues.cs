using System.ComponentModel.DataAnnotations;

namespace MtgMcp.Archidekt;

/// <summary>
/// Publishes the visibility values Archidekt folders accept as a schema enum; folders have no unlisted state.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
internal sealed class FolderVisibilityValuesAttribute()
    : AllowedValuesAttribute("private", "public");
