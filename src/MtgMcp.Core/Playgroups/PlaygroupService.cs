namespace MtgMcp.Core;

/// <summary>
/// Aggregates Playgroup.gg data into provider-neutral deck, user, and ranking results.
/// </summary>
public sealed partial class PlaygroupService
{
    /// <summary>
    /// Caps Playgroup API game pages at the documented maximum.
    /// </summary>
    private const int ApiPageLimit = 100;

    /// <summary>
    /// Bounds deck enrichment fan-out for one tool call.
    /// </summary>
    private const int MaximumDeckLimit = 200;

    /// <summary>
    /// Bounds user discovery fan-out for one tool call.
    /// </summary>
    private const int MaximumUserLimit = 200;

    /// <summary>
    /// Bounds game pagination for one tool call.
    /// </summary>
    private const int MaximumGameFetchLimit = 1_000;

    /// <summary>
    /// Marks Playgroup power estimates that should be hidden by default.
    /// </summary>
    private const double LowConfidenceThreshold = 0.5d;

    /// <summary>
    /// Reads normalized data from the Playgroup adapter.
    /// </summary>
    private readonly IPlaygroupGateway gateway;

    /// <summary>
    /// Creates a service that derives Playgroup views from public API data.
    /// </summary>
    public PlaygroupService(IPlaygroupGateway gateway)
    {
        this.gateway = gateway;
    }

    /// <summary>
    /// Gets redacted Playgroup authentication status.
    /// </summary>
    public Task<PlaygroupAuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken)
    {
        return gateway.GetAuthStatusAsync(cancellationToken);
    }

    /// <summary>
    /// Gets a playgroup visible to the configured or supplied user.
    /// </summary>
    public async Task<PlaygroupSummary> GetPlaygroupAsync(
        string playgroupIdOrUrl,
        long? userId,
        CancellationToken cancellationToken
    )
    {
        long playgroupId = ParsePlaygroupId(playgroupIdOrUrl);
        long effectiveUserId = userId ?? (await gateway
            .GetCurrentUserAsync(cancellationToken)
            .ConfigureAwait(false)).Id;

        return await gateway
            .GetUserPlaygroupAsync(effectiveUserId, playgroupId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Gets one Playgroup deck by id.
    /// </summary>
    public Task<PlaygroupDeck> GetDeckAsync(long deckId, CancellationToken cancellationToken)
    {
        return gateway.GetDeckAsync(deckId, cancellationToken);
    }
}
