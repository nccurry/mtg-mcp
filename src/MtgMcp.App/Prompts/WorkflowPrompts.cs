using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
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
    /// Counts the prompts this class publishes; the capabilities resource reports it.
    /// </summary>
    internal const int PromptCount = 3;

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
        [Description("Local deck ID (a UUID with no spaces).")] string? deckId)
    {
        string id = ReadId(deckId, nameof(deckId));
        return string.Create(CultureInfo.InvariantCulture, $$"""
            Audit the local deck {{id}}. Report facts and ask me what to change; change nothing yet.

            1. Call local_deck_get for the deck without include, to see its name, format, zones, and entries.
            2. Call local_deck_validate with rules "commander" and report each fact it returns: deck size,
               commanders, color identity, copy limits, format legality, Game Changers, pairing, and companions.
               If a tool says card data is not installed, tell me, and offer scryfall_card_data_sync, which is a
               large explicit download: ask before running it.
            3. For the curve, call card_list_query on source {"kind": "deck", "deckId": "{{id}}", "zones": ["main"]}
               with groupBy ["manaValue"], metrics ["quantity"], and where manaValue gt 0, and report the counts.
               Mana value 0 holds lands and free spells and is left out; say so.
            4. For colors, call card_list_query on the same source with zones ["main", "commander"],
               no groupBy and metrics ["pips", "sources"], and report the colored pips and the color sources.
            5. Call stats_castability for the deck's spells with source groups and turns we agree on, and report
               the exact chances.
            6. Call spellbook_deck_bracket_facts_get and report its flags and combos.
            7. List what the tools could not read (unresolved entries, missing card data) and ask me what I want
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
        [Description("Scryfall card ID of the commander (a UUID with no spaces).")] string? scryfallId)
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
        [Description("First local deck ID (a UUID with no spaces).")] string? deckIdA,
        [Description("Second local deck ID (a UUID with no spaces).")] string? deckIdB)
    {
        string a = ReadId(deckIdA, nameof(deckIdA));
        string b = ReadId(deckIdB, nameof(deckIdB));
        return string.Create(CultureInfo.InvariantCulture, $$"""
            Compare the local decks {{a}} (A) and {{b}} (B). Report the differences and ask me what they mean to me.

            1. Call card_lists_compare with left {"kind": "deck", "deckId": "{{a}}"}, right
               {"kind": "deck", "deckId": "{{b}}"}, and operation "difference" for the cards only in A, then swap
               the two for the cards only in B, then use "intersection" for the shared cards.
            2. For each deck, call card_list_query on source {"kind": "deck", "deckId": "<that deck's ID>",
               "zones": ["main"]} with groupBy ["manaValue"], metrics ["quantity"], and where manaValue gt 0 for
               the curve (mana value 0 holds lands and free spells and is left out; say so). Then call it again with
               zones ["main", "commander"],
               no groupBy and metrics ["pips", "sources"] for the colors. Show the two decks side by side.
            3. Report the differences as facts: counts, names, curve, pips, and sources. Do not say which deck is
               better.

            {{Closing}}
            """);
    }

    /// <summary>
    /// Wraps the prompt request pipeline so a missing required argument is an invalid-params protocol error that
    /// names the argument. The SDK fails such a request before the prompt runs and reports only an internal error.
    /// </summary>
    internal static McpRequestHandler<GetPromptRequestParams, GetPromptResult> ArgumentFilter(
        McpRequestHandler<GetPromptRequestParams, GetPromptResult> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return async (context, cancellationToken) =>
        {
            try
            {
                return await next(context, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not (McpProtocolException or OperationCanceledException))
            {
                string? problem = FindArgumentProblem(context);
                if (problem is null)
                {
                    throw;
                }

                throw new McpProtocolException(problem, exception, McpErrorCode.InvalidParams);
            }
        };
    }

    /// <summary>
    /// Finds the first required argument of the matched prompt that the request left out, left blank, or gave as
    /// something other than text, and words the error for it.
    /// </summary>
    private static string? FindArgumentProblem(RequestContext<GetPromptRequestParams> context)
    {
        if (context.MatchedPrimitive is not McpServerPrompt prompt || prompt.ProtocolPrompt.Arguments is null)
        {
            return null;
        }

        IDictionary<string, JsonElement>? given = context.Params?.Arguments;
        foreach (PromptArgument argument in prompt.ProtocolPrompt.Arguments)
        {
            if (argument.Required != true)
            {
                continue;
            }

            if (given is null || !given.TryGetValue(argument.Name, out JsonElement value) ||
                value.ValueKind == JsonValueKind.Null)
            {
                return RequiredMessage(argument.Name);
            }

            if (value.ValueKind != JsonValueKind.String)
            {
                return MustBeUuidMessage(argument.Name);
            }

            if (string.IsNullOrWhiteSpace(value.GetString()))
            {
                return RequiredMessage(argument.Name);
            }
        }

        return null;
    }

    /// <summary>
    /// Words the error for an argument that is not a UUID.
    /// </summary>
    private static string MustBeUuidMessage(string name)
    {
        return $"{name} must be a UUID such as 0193e97a-ef95-7700-8000-000000000001.";
    }

    /// <summary>
    /// Words the error for a missing argument.
    /// </summary>
    private static string RequiredMessage(string name)
    {
        return $"{name} is required: give a UUID such as 0193e97a-ef95-7700-8000-000000000001.";
    }

    /// <summary>
    /// Checks one ID argument and returns it in canonical form, so a prompt never carries free text. A missing or
    /// bad value is an invalid-params protocol error that carries this message to the client.
    /// </summary>
    private static string ReadId(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new McpProtocolException(
                RequiredMessage(name),
                McpErrorCode.InvalidParams);
        }

        if (!Guid.TryParse(value, out Guid id) || id == Guid.Empty)
        {
            throw new McpProtocolException(
                MustBeUuidMessage(name),
                McpErrorCode.InvalidParams);
        }

        return id.ToString("D", CultureInfo.InvariantCulture);
    }
}
