using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Exposes factual card facets and explicit predicate counting tools.
/// </summary>
[McpServerToolType]
public sealed class FacetTools
{
    /// <summary>
    /// Extracts and queries card facets.
    /// </summary>
    private readonly CardFacetService facets;

    /// <summary>
    /// Guards annotation writes.
    /// </summary>
    private readonly OperationModeGuard operationMode;

    /// <summary>
    /// Creates facet tools for the MCP surface.
    /// </summary>
    public FacetTools(CardFacetService facets, OperationModeGuard operationMode)
    {
        this.facets = facets;
        this.operationMode = operationMode;
    }

    /// <summary>
    /// Gets normalized factual facets for one workspace card.
    /// </summary>
    [McpServerTool(Name = "card_facets_get", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Get factual facets for one workspace card. Defaults to key facets only; detailLevel=full returns every facet.")]
    public async Task<object> GetCardFacetsAsync(
        string workspaceId,
        string cardName,
        [Description("Output detail level: summary, normal, or full.")]
        string detailLevel = "summary",
        CancellationToken cancellationToken = default)
    {
        try
        {
            CardFacetSnapshot snapshot = await facets.GetCardFacetsAsync(
                    workspaceId,
                    cardName,
                    cancellationToken)
                .ConfigureAwait(false);
            return CardFacetOutputPresenter.Present(snapshot, detailLevel);
        }
        catch (InvalidOperationException exception) when (IsCardNotFoundInWorkspace(exception))
        {
            return CardFacetOutputPresenter.NotFound(workspaceId, cardName);
        }
    }

    /// <summary>
    /// Gets normalized factual facets for cards in a workspace.
    /// </summary>
    [McpServerTool(Name = "deck_facets_get", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Get factual facets for workspace cards. IncludedOnly defaults to true so counts follow active deck categories.")]
    public Task<DeckFacetSnapshot> GetDeckFacetsAsync(
        string workspaceId,
        bool includedOnly = true,
        CancellationToken cancellationToken = default)
    {
        return facets.GetDeckFacetsAsync(workspaceId, includedOnly, cancellationToken);
    }

    /// <summary>
    /// Counts deck cards matching an explicit JSON facet predicate.
    /// </summary>
    [McpServerTool(Name = "deck_facets_count", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Count workspace cards that match a caller-supplied JSON facet predicate. The predicate defines the category; mtg-mcp only evaluates it.")]
    public Task<DeckFacetCountResult> CountDeckCardsMatchingAsync(
        string workspaceId,
        string predicateJson,
        bool includedOnly = true,
        CancellationToken cancellationToken = default)
    {
        return facets.CountDeckCardsMatchingAsync(workspaceId, predicateJson, includedOnly, cancellationToken);
    }

    /// <summary>
    /// Explains whether one card matches an explicit JSON facet predicate.
    /// </summary>
    [McpServerTool(Name = "card_facets_explain_match", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Explain whether one workspace card matches a caller-supplied JSON facet predicate, returning the exact facet evidence inspected.")]
    public Task<CardFacetMatchResult> ExplainCardMatchAsync(
        string workspaceId,
        string cardName,
        string predicateJson,
        CancellationToken cancellationToken = default)
    {
        return facets.ExplainCardMatchAsync(workspaceId, cardName, predicateJson, cancellationToken);
    }

    /// <summary>
    /// Saves local annotations that later appear as card facets.
    /// </summary>
    [McpServerTool(Name = "card_facets_set_annotations", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Set local user or Tagger facet annotations for one workspace card. This writes mtg-mcp workspace metadata only and does not write back to Archidekt.")]
    public Task<CardFacetAnnotationResult> SetCardFacetAnnotationsAsync(
        string workspaceId,
        string cardName,
        string[]? userTags = null,
        string[]? userCategories = null,
        string[]? taggerOracleTags = null,
        string[]? taggerArtTags = null,
        CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanWritePlanningState("card_facets_set_annotations");
        return facets.SetCardAnnotationsAsync(
            workspaceId,
            cardName,
            userTags,
            userCategories,
            taggerOracleTags,
            taggerArtTags,
            cancellationToken);
    }

    /// <summary>
    /// Identifies workspace-card misses so the MCP response is structured.
    /// </summary>
    private static bool IsCardNotFoundInWorkspace(InvalidOperationException exception)
    {
        return exception.Message.Contains("was not found in workspace", StringComparison.OrdinalIgnoreCase);
    }
}
