using System.Text.Json;

namespace MtgMcp.Archidekt;

/// <summary>
/// Owns Archidekt named snapshot provider routes.
/// </summary>
internal sealed class ArchidektSnapshotTransport
{
    /// <summary>
    /// Sends shared authenticated provider requests for snapshot routes.
    /// </summary>
    private readonly ArchidektSession session;

    /// <summary>
    /// Creates named snapshot route operations around one shared provider session.
    /// </summary>
    internal ArchidektSnapshotTransport(ArchidektSession session)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
    }

    /// <summary>
    /// Lists named snapshots for one deck.
    /// </summary>
    internal async Task<RemoteNamedSnapshotPage> ListAsync(
        string deckId,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        deckId = ArchidektContract.Required(deckId, nameof(deckId));
        ArchidektSession.ProviderResponse response = await session.SendAsync(
            HttpMethod.Get,
            $"api/decks/{Uri.EscapeDataString(deckId)}/snapshots/",
            payload: null,
            requiresAuthentication: true,
            idempotentRead: true,
            budget,
            cancellationToken).ConfigureAwait(false);
        using JsonDocument document = ArchidektSession.ParseJson(response.Json);
        return ArchidektSnapshotContractMapper.MapSnapshotPage(
            document.RootElement,
            deckId,
            response.Json,
            response.RetrievedAtUtc,
            "GET /api/decks/{deckId}/snapshots/");
    }

    /// <summary>
    /// Gets one complete named snapshot.
    /// </summary>
    internal async Task<RemoteNamedSnapshot> GetAsync(
        string deckId,
        string snapshotId,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        deckId = ArchidektContract.Required(deckId, nameof(deckId));
        snapshotId = ArchidektContract.Required(snapshotId, nameof(snapshotId));
        ArchidektSession.ProviderResponse response = await session.SendAsync(
            HttpMethod.Get,
            $"api/decks/snapshots/{Uri.EscapeDataString(snapshotId)}/",
            payload: null,
            requiresAuthentication: true,
            idempotentRead: true,
            budget,
            cancellationToken).ConfigureAwait(false);
        using JsonDocument document = ArchidektSession.ParseJson(response.Json);
        return ArchidektSnapshotContractMapper.MapSnapshot(
            document.RootElement,
            deckId,
            response.Json,
            response.RetrievedAtUtc,
            "GET /api/decks/snapshots/{snapshotId}/");
    }

    /// <summary>
    /// Sends one named snapshot creation payload.
    /// </summary>
    internal Task SendCreateAsync(
        string deckId,
        object payload,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return session.SendMutationAsync(
            HttpMethod.Post,
            $"api/decks/{Uri.EscapeDataString(deckId)}/snapshots/",
            payload,
            budget,
            cancellationToken);
    }

    /// <summary>
    /// Sends one named snapshot metadata update.
    /// </summary>
    internal Task SendUpdateAsync(
        string snapshotId,
        object payload,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return session.SendMutationAsync(
            HttpMethod.Patch,
            $"api/decks/snapshots/{Uri.EscapeDataString(snapshotId)}/",
            payload,
            budget,
            cancellationToken);
    }

    /// <summary>
    /// Sends one named snapshot deletion.
    /// </summary>
    internal Task SendDeleteAsync(
        string snapshotId,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return session.SendMutationAsync(
            HttpMethod.Delete,
            $"api/decks/snapshots/{Uri.EscapeDataString(snapshotId)}/",
            payload: null,
            budget,
            cancellationToken);
    }
}
