using MtgMcp.Core;

namespace MtgMcp.Archidekt;

/// <summary>
/// Coordinates archidekt gateway HTTP operations.
/// </summary>
public sealed partial class ArchidektGateway
{
    /// <summary>
    /// Persists the metadata.
    /// </summary>
    public async Task PersistMetadataAsync(
        DeckWorkspace workspace,
        CancellationToken cancellationToken
    )
    {
        await EnsureAuthenticatedAsync(required: true, cancellationToken).ConfigureAwait(false);
        string deckId = RequireDeckId(workspace);
        object payload = new
        {
            name = workspace.Name,
            deckFormat = ResolveDeckFormatForUpdate(workspace),
            description = workspace.Description,
        };

        await SendJsonAsync(
                HttpMethod.Patch,
                $"api/decks/{deckId}/update/",
                payload,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the Archidekt deck format value used by the update endpoint.
    /// </summary>
    private static object? ResolveDeckFormatForUpdate(DeckWorkspace workspace)
    {
        return workspace.ArchidektDeckFormatId
            ?? TryKnownDeckFormatId(workspace.Format)
            ?? ParseIntOrString(workspace.Format);
    }

    /// <summary>
    /// Maps common normalized format names back to Archidekt ids.
    /// </summary>
    private static int? TryKnownDeckFormatId(string? format)
    {
        string normalized = format?.Trim().ToLowerInvariant() ?? "";
        return normalized switch
        {
            "3" => 3,
            "commander" => 3,
            "edh" => 3,
            _ => null,
        };
    }
}
