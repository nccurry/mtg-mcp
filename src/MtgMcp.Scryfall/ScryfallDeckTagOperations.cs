using MtgMcp.Core.Results;

namespace MtgMcp.Scryfall;

/// <summary>
/// Identifies one official Scryfall tag by its type and one exact source identity.
/// </summary>
internal sealed record ScryfallTagIdentity(
    string TagType,
    Guid? TagId = null,
    string? ExactSlug = null);

/// <summary>
/// Carries resolved source tag IDs from one installed Scryfall generation in input order.
/// </summary>
internal sealed record ScryfallTagResolution(
    Guid GenerationId,
    IReadOnlyList<Guid> TagIds);

/// <summary>
/// Carries one direct Scryfall tag assignment and every source parent that reaches it.
/// </summary>
internal sealed record ScryfallDirectTagEvidence(
    Guid TagId,
    string TagType,
    string Slug,
    string Weight,
    IReadOnlyList<Guid> AncestorTagIds);

/// <summary>
/// Carries complete or missing tag evidence for one requested deck card.
/// </summary>
internal sealed record ScryfallDeckTagEntryEvidence(
    bool IsComplete,
    IReadOnlyList<ScryfallDirectTagEvidence> Tags);

/// <summary>
/// Carries tag evidence for requested deck cards in their original order.
/// </summary>
internal sealed record ScryfallDeckTagEvidence(
    Guid GenerationId,
    IReadOnlyList<ScryfallDeckTagEntryEvidence> Entries);

/// <summary>
/// Owns installed Scryfall tag reads used by deterministic deck categorization.
/// </summary>
internal sealed class ScryfallDeckTagOperations
{
    /// <summary>
    /// Reads installed card and tag data.
    /// </summary>
    private readonly ScryfallCardDataStore cardDataStore;

    /// <summary>
    /// Creates tag operations around the shared installed card-data store.
    /// </summary>
    internal ScryfallDeckTagOperations(ScryfallCardDataStore cardDataStore)
    {
        ArgumentNullException.ThrowIfNull(cardDataStore);
        this.cardDataStore = cardDataStore;
    }

    /// <summary>
    /// Resolves exact tag identities against the installed active generation.
    /// </summary>
    internal async Task<OperationResult<ScryfallTagResolution>> ResolveTagIdentitiesAsync(
        IReadOnlyList<ScryfallTagIdentity>? identities,
        string freshnessPolicy,
        CancellationToken cancellationToken)
    {
        if (freshnessPolicy is not ("default" or "cache-only" or "refresh"))
        {
            return new OperationInvalidInput(
                "invalid-freshness-policy",
                "Freshness policy must be default, cache-only, or refresh.");
        }

        if (string.Equals(freshnessPolicy, "refresh", StringComparison.Ordinal))
        {
            return new OperationUnsupported(
                "category-rules-require-explicit-card-data-sync",
                "Category rules use installed Scryfall tag data. Run an explicit card-data sync before requesting refreshed tags.");
        }

        return await cardDataStore.ResolveTagIdentitiesAsync(identities, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads direct tag assignments and source parents from one caller-selected generation.
    /// </summary>
    internal Task<OperationResult<ScryfallDeckTagEvidence>> ReadDeckTagEvidenceAsync(
        Guid generationId,
        IReadOnlyList<ScryfallCardLookup>? lookups,
        CancellationToken cancellationToken)
    {
        return cardDataStore.ReadDeckTagEvidenceAsync(generationId, lookups, cancellationToken);
    }
}
