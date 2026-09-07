using System.Text.Json;

namespace MtgMcp.Archidekt;

/// <summary>
/// Owns Archidekt folder provider routes.
/// </summary>
internal sealed class ArchidektFolderTransport
{
    /// <summary>
    /// Sends shared authenticated provider requests for folder routes.
    /// </summary>
    private readonly ArchidektSession session;

    /// <summary>
    /// Creates folder route operations around one shared provider session.
    /// </summary>
    internal ArchidektFolderTransport(ArchidektSession session)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
    }

    /// <summary>
    /// Lists the complete folder tree.
    /// </summary>
    internal async Task<RemoteFolderTree> ListAsync(
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        ArchidektSession.ProviderResponse response = await session.SendAsync(
            HttpMethod.Get,
            "api/decks/folderTree/",
            payload: null,
            requiresAuthentication: true,
            idempotentRead: true,
            budget,
            cancellationToken).ConfigureAwait(false);
        using JsonDocument document = ArchidektSession.ParseJson(response.Json);
        return ArchidektFolderContractMapper.MapFolderTree(
            document.RootElement,
            response.Json,
            response.RetrievedAtUtc,
            "GET /api/decks/folderTree/");
    }

    /// <summary>
    /// Gets one folder detail.
    /// </summary>
    internal async Task<RemoteFolderTree> GetAsync(
        string folderId,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        folderId = ArchidektContract.Required(folderId, nameof(folderId));
        ArchidektSession.ProviderResponse response = await session.SendAsync(
            HttpMethod.Get,
            $"api/decks/folders/{Uri.EscapeDataString(folderId)}/",
            payload: null,
            requiresAuthentication: true,
            idempotentRead: true,
            budget,
            cancellationToken).ConfigureAwait(false);
        using JsonDocument document = ArchidektSession.ParseJson(response.Json);
        return ArchidektFolderContractMapper.MapFolderDetail(
            document.RootElement,
            response.Json,
            response.RetrievedAtUtc,
            "GET /api/decks/folders/{folderId}/");
    }

    /// <summary>
    /// Creates one folder with explicit visibility and parent identity.
    /// </summary>
    internal async Task<string> CreateAsync(
        ArchidektFolderCreateRequest request,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        ArchidektSession.ProviderResponse response = await session.SendAsync(
            HttpMethod.Post,
            "api/decks/folders/",
            new
            {
                name = ArchidektContract.Required(request.Name, nameof(request.Name)),
                @private = ArchidektContract.NormalizeVisibility(request.Visibility) == "private",
                parent_folder = ArchidektProviderId.Parse(request.ParentFolderId),
            },
            requiresAuthentication: true,
            idempotentRead: false,
            budget,
            cancellationToken).ConfigureAwait(false);
        using JsonDocument document = ArchidektSession.ParseJson(response.Json);
        JsonElement root = document.RootElement;
        string? folderId = root.ValueKind switch
        {
            JsonValueKind.String => root.GetString(),
            JsonValueKind.Number => root.GetRawText(),
            JsonValueKind.Object when ArchidektProviderId.TryRead(root, "id", out string? id) => id,
            _ => null,
        };
        return !string.IsNullOrWhiteSpace(folderId)
            ? folderId
            : throw new ArchidektProviderException(
                ArchidektFailureKind.Unsupported,
                "provider-contract-unsupported",
                "Archidekt did not return the created folder identity.");
    }

    /// <summary>
    /// Updates one folder through the observed detail route.
    /// </summary>
    internal Task SendUpdateAsync(
        string folderId,
        object payload,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return session.SendMutationAsync(
            HttpMethod.Patch,
            "api/massUpdate/",
            new
            {
                items = new[]
                {
                    new
                    {
                        type = "folder",
                        id = ArchidektProviderId.Parse(folderId),
                        patch = payload,
                    },
                },
            },
            budget,
            cancellationToken);
    }

    /// <summary>
    /// Moves exact typed items through the observed mass-update route.
    /// </summary>
    internal Task SendMoveAsync(
        object payload,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return session.SendMutationAsync(
            HttpMethod.Patch,
            "api/massUpdate/",
            payload,
            budget,
            cancellationToken);
    }

    /// <summary>
    /// Deletes exactly one preflighted empty folder through the observed item-delete route.
    /// </summary>
    internal Task SendDeleteAsync(
        object payload,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return session.SendMutationAsync(
            HttpMethod.Post,
            "api/decks/folders/deleteItems/",
            payload,
            budget,
            cancellationToken);
    }
}
