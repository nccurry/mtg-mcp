using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;

namespace MtgMcp.App.Prompts;

/// <summary>
/// Offers three MCP prompts that start common deckbuilding workflows. A prompt costs no model context until a user
/// runs it. Each takes IDs only, never free text, so a client that splits prompt arguments on spaces still works, and
/// a prompt names only tools of the default selection that needs no provider credentials. When the session hides
/// one of those tools (a narrower toolset list or the read-only mode), the text tells the agent to say so and skip
/// that step.
/// </summary>
internal sealed class WorkflowPrompts
{
    /// <summary>
    /// Names the audit prompt.
    /// </summary>
    internal const string AuditDeckName = "audit-deck";

    /// <summary>
    /// Names the build prompt.
    /// </summary>
    internal const string BuildFromCommanderName = "build-from-commander";

    /// <summary>
    /// Names the comparison prompt.
    /// </summary>
    internal const string CompareDecksName = "compare-decks";

    /// <summary>
    /// Ends every prompt with who decides and what to do when a tool is hidden.
    /// </summary>
    private const string Closing =
        "You report facts and exact numbers; the user and you make every judgment, and you do not call any fact " +
        "good, bad, legal, or illegal on the server's behalf. Name the tool behind each number. If a tool named " +
        "here is not in your tool list (the session may have a narrower toolset list or a read-only mode), say " +
        "which step you skipped and continue with the rest.";

    /// <summary>
    /// Describes the audit workflow for one local deck.
    /// </summary>
    [McpServerPrompt(Name = AuditDeckName, Title = "Audit a Local Deck")]
    [Description(
        "Read one local deck and report its facts: Commander rule facts, curve, color pips, color sources, " +
        "castability, and bracket facts. Takes the local deck ID.")]
    internal static string AuditDeck(
        [Description("Local deck ID (a UUID with no spaces).")] string deckId)
    {
        string id = ReadId(deckId, nameof(deckId));
        return string.Create(CultureInfo.InvariantCulture, $$"""
            Audit the local deck {{id}}. Report facts and ask me what to change; change nothing yet.

            1. Call local_deck_get with include ["cards", "tags"] to read the deck. If entries say card data is not
               installed, tell me, and offer scryfall_card_data_sync, which is a large explicit download: ask before
               running it.
            2. Call local_deck_validate with rules "commander" and report each fact it returns: deck size,
               commanders, color identity, copy limits, format legality, Game Changers, pairing, and companions.
            3. Call card_list_query on source {"kind": "deck", "deckId": "{{id}}"} grouped by manaValue with metrics
               quantity, pips, and sources, and report the curve, the colored pips, and the color sources.
            4. Call stats_castability for the deck's spells with source groups and turns we agree on, and report
               the exact chances.
            5. Call spellbook_deck_bracket_facts_get and report its flags and combos.
            6. List what the tools could not read (unresolved entries, missing card data) and ask me what I want
               to change.

            {{Closing}}
            """);
    }

    /// <summary>
    /// Describes the workflow that builds a new local deck around a commander.
    /// </summary>
    [McpServerPrompt(Name = BuildFromCommanderName, Title = "Build a Deck from a Commander")]
    [Description(
        "Read a commander, ask the user for budget, bracket, and plan, search by tags, and build a local deck. " +
        "Takes the commander's Scryfall card ID.")]
    internal static string BuildFromCommander(
        [Description("Scryfall card ID of the commander (a UUID with no spaces).")] string scryfallId)
    {
        string id = ReadId(scryfallId, nameof(scryfallId));
        return string.Create(CultureInfo.InvariantCulture, $$"""
            Help me build a Commander deck around the card with Scryfall ID {{id}}.

            1. Call scryfall_cards_get with one lookup for that ID and show me the commander's color identity,
               cost, and text.
            2. Ask me for my budget, my target bracket, and the plan I want. Wait for my answers.
            3. Find candidates with scryfall_search (use otag: to filter by oracle tag, and id<= for the color
               identity). If card data is installed, scryfall_installed_cards_by_tag reads cards by tag without a
               request to Scryfall; scryfall_tags_search finds tag names. Prefer a concise responseFormat.
            4. Show me candidate groups and let me choose. Use card_list_query to total prices and count the curve
               of a list when I ask.
            5. With my go-ahead, call local_deck_create for the new deck, then add and file cards in batches with
               local_deck_apply_changes.
            6. Call local_deck_validate with rules "commander" and report the facts. Ask me what to change.

            {{Closing}}
            """);
    }

    /// <summary>
    /// Describes the workflow that compares two local decks.
    /// </summary>
    [McpServerPrompt(Name = CompareDecksName, Title = "Compare Two Local Decks")]
    [Description("Compare two local decks card by card and by curve and pips. Takes two local deck IDs.")]
    internal static string CompareDecks(
        [Description("First local deck ID (a UUID with no spaces).")] string deckIdA,
        [Description("Second local deck ID (a UUID with no spaces).")] string deckIdB)
    {
        string a = ReadId(deckIdA, nameof(deckIdA));
        string b = ReadId(deckIdB, nameof(deckIdB));
        return string.Create(CultureInfo.InvariantCulture, $$"""
            Compare the local decks {{a}} (A) and {{b}} (B). Report the differences and ask me what they mean to me.

            1. Call card_lists_compare with left {"kind": "deck", "deckId": "{{a}}"}, right
               {"kind": "deck", "deckId": "{{b}}"}, and operation "difference" for the cards only in A, then swap
               the two for the cards only in B, then use "intersection" for the shared cards.
            2. Call card_list_query on each deck, grouped by manaValue with metrics quantity, pips, and sources,
               and show the two results side by side.
            3. Report the differences as facts: counts, names, curve, pips, and sources. Do not say which deck is
               better.

            {{Closing}}
            """);
    }

    /// <summary>
    /// Checks one ID argument and returns it in canonical form, so a prompt never carries free text.
    /// </summary>
    private static string ReadId(string value, string name)
    {
        if (!Guid.TryParse(value, out Guid id) || id == Guid.Empty)
        {
            throw new ArgumentException($"{name} must be a UUID such as 0193e97a-ef95-7700-8000-000000000001.", name);
        }

        return id.ToString("D", CultureInfo.InvariantCulture);
    }
}
