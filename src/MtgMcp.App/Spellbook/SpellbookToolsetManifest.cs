using Microsoft.Extensions.DependencyInjection;
using MtgMcp.App.Capabilities;
using MtgMcp.Decks;
using MtgMcp.Spellbook;

namespace MtgMcp.App.Spellbook;

/// <summary>
/// Owns the opt-in Commander Spellbook evidence surface and its fixed registration.
/// </summary>
internal static class SpellbookToolsetManifest
{
    /// <summary>
    /// Gets the descriptor for bounded variant and one local-deck evidence workflows.
    /// </summary>
    internal static CapabilityToolsetDescriptor Descriptor { get; } = new(
        CapabilityToolset.Spellbook,
        CapabilityToolsetStability.Stable,
        "Bounded Commander Spellbook source evidence for variants and one revisioned local deck. It is opt-in and never recommends deck changes.",
        [
            "spellbook_deck_combos_find",
            "spellbook_variant_get",
            "spellbook_variant_search",
        ],
        [],
        []);

    /// <summary>
    /// Registers the exact read-only Commander Spellbook tools for the static session.
    /// </summary>
    internal static void Register(
        IMcpServerBuilder builder,
        SpellbookService service,
        SqliteDeckStore deckStore)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(deckStore);
        builder.WithTools(new SpellbookReadTools(service, new SpellbookDeckInputResolver(deckStore)));
    }
}
