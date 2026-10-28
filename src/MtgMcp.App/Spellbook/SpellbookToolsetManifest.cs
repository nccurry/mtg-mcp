using Microsoft.Extensions.DependencyInjection;
using MtgMcp.App.Capabilities;
using MtgMcp.App.Hosting;
using MtgMcp.Decks;
using MtgMcp.Spellbook;

namespace MtgMcp.App.Spellbook;

/// <summary>
/// Owns the Commander Spellbook evidence surface and its fixed registration.
/// </summary>
internal static class SpellbookToolsetManifest
{
    /// <summary>
    /// Gets the descriptor for bounded variant and one local-deck evidence workflows.
    /// </summary>
    internal static CapabilityToolsetDescriptor Descriptor { get; } = new(
        CapabilityToolset.Spellbook,
        CapabilityToolsetStability.Stable,
        "Bounded Commander Spellbook source evidence for variants and one revisioned local deck. It never recommends deck changes.",
        [
            "spellbook_deck_bracket_facts_get",
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
        SpellbookDeckInputResolver resolver = new(deckStore);
        builder
            .WithRegisteredTools(ToolRegistration.Create(new SpellbookReadTools(service, resolver)))
            .WithRegisteredTools(ToolRegistration.Create(new SpellbookBracketTools(service, resolver, deckStore)));
    }
}
