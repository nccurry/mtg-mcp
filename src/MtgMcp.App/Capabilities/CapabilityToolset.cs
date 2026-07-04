namespace MtgMcp.App.Capabilities;

/// <summary>
/// Identifies one stable capability family independently of operation authority.
/// </summary>
internal enum CapabilityToolset
{
    /// <summary>
    /// Covers local deck storage, editing, backup, and manual interchange.
    /// </summary>
    Decks,

    /// <summary>
    /// Covers official Scryfall evidence snapshots and cached reads.
    /// </summary>
    Scryfall,

    /// <summary>
    /// Covers provider-independent exact deck mathematics.
    /// </summary>
    Stats,

    /// <summary>
    /// Covers explicit Archidekt provider workflows.
    /// </summary>
    Archidekt,

    /// <summary>
    /// Covers the documented Playgroup public API.
    /// </summary>
    Playgroup,

}

/// <summary>
/// Identifies how the configured set of enabled toolsets was selected.
/// </summary>
internal enum CapabilityToolsetSelectionKind
{
    /// <summary>
    /// Enables each implemented descriptor marked for ordinary discovery.
    /// </summary>
    Default,

    /// <summary>
    /// Enables every implemented stable descriptor.
    /// </summary>
    All,

    /// <summary>
    /// Disables every tool while retaining server metadata resources.
    /// </summary>
    None,

    /// <summary>
    /// Enables only descriptors named explicitly by the caller.
    /// </summary>
    Explicit,
}

/// <summary>
/// Reports whether an implemented toolset can currently serve its operations.
/// </summary>
internal enum CapabilityToolsetAvailability
{
    /// <summary>
    /// The toolset's local or provider prerequisites are currently usable.
    /// </summary>
    Available,

    /// <summary>
    /// The toolset is implemented but a runtime prerequisite is unavailable.
    /// </summary>
    Unavailable,
}

/// <summary>
/// Distinguishes stable capability families from explicitly selected experiments.
/// </summary>
internal enum CapabilityToolsetStability
{
    /// <summary>
    /// The descriptor may participate in the complete stable surface.
    /// </summary>
    Stable,

    /// <summary>
    /// The descriptor requires explicit selection and never enters reserved profiles.
    /// </summary>
    Experimental,
}

/// <summary>
/// Formats stable toolset vocabulary and default-profile policy.
/// </summary>
internal static class CapabilityToolsetPolicy
{
    /// <summary>
    /// Formats one toolset using its exact lowercase configuration name.
    /// </summary>
    internal static string Format(CapabilityToolset toolset)
    {
        return toolset switch
        {
            CapabilityToolset.Decks => "decks",
            CapabilityToolset.Scryfall => "scryfall",
            CapabilityToolset.Stats => "stats",
            CapabilityToolset.Archidekt => "archidekt",
            CapabilityToolset.Playgroup => "playgroup",
            _ => throw new ArgumentOutOfRangeException(
                nameof(toolset),
                toolset,
                "Unknown capability toolset."),
        };
    }

    /// <summary>
    /// Reports whether a stable toolset enters the ordinary profile when implemented.
    /// </summary>
    internal static bool IsDefaultEnabled(CapabilityToolset toolset)
    {
        return toolset switch
        {
            CapabilityToolset.Decks or CapabilityToolset.Scryfall or CapabilityToolset.Stats => true,
            CapabilityToolset.Archidekt or CapabilityToolset.Playgroup => false,
            _ => false,
        };
    }

    /// <summary>
    /// Formats the public selection state without echoing caller input.
    /// </summary>
    internal static string Format(CapabilityToolsetSelectionKind kind)
    {
        return kind switch
        {
            CapabilityToolsetSelectionKind.Default => "default",
            CapabilityToolsetSelectionKind.All => "all",
            CapabilityToolsetSelectionKind.None => "none",
            CapabilityToolsetSelectionKind.Explicit => "explicit",
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Unknown capability selection kind."),
        };
    }

    /// <summary>
    /// Formats one toolset availability state for capability metadata.
    /// </summary>
    internal static string Format(CapabilityToolsetAvailability availability)
    {
        return availability switch
        {
            CapabilityToolsetAvailability.Available => "available",
            CapabilityToolsetAvailability.Unavailable => "unavailable",
            _ => throw new ArgumentOutOfRangeException(
                nameof(availability),
                availability,
                "Unknown capability availability."),
        };
    }

    /// <summary>
    /// Formats one toolset stability classification for capability metadata.
    /// </summary>
    internal static string Format(CapabilityToolsetStability stability)
    {
        return stability switch
        {
            CapabilityToolsetStability.Stable => "stable",
            CapabilityToolsetStability.Experimental => "experimental",
            _ => throw new ArgumentOutOfRangeException(
                nameof(stability),
                stability,
                "Unknown capability stability."),
        };
    }
}
