using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Decks;

/// <summary>
/// Carries category rules after local ownership checks and exact source-tag resolution.
/// </summary>
internal sealed record ResolvedDeckCategoryRules(
    CategoryRuleSet Rules,
    Guid? ScryfallGenerationId);

/// <summary>
/// Expands category rule sources and resolves their tag identities against installed Scryfall data.
/// </summary>
internal sealed class DeckCategoryRuleResolver
{
    /// <summary>
    /// Reads installed source tags.
    /// </summary>
    private readonly ScryfallService scryfall;

    /// <summary>
    /// Creates a resolver around the shared Scryfall service.
    /// </summary>
    internal DeckCategoryRuleResolver(ScryfallService scryfall)
    {
        ArgumentNullException.ThrowIfNull(scryfall);
        this.scryfall = scryfall;
    }

    /// <summary>
    /// Expands, validates, and binds a category rule source to one installed Scryfall generation.
    /// </summary>
    internal async Task<OperationResult<ResolvedDeckCategoryRules>> ResolveAsync(
        CategoryRuleSource? source,
        DeckDocument deck,
        string freshnessPolicy,
        CancellationToken cancellationToken)
    {
        if (source is null)
        {
            return new OperationInvalidInput("invalid-category-rule-source", "A category rule source is required.");
        }

        if (freshnessPolicy is not ("default" or "cache-only" or "refresh"))
        {
            return new OperationInvalidInput(
                "invalid-freshness-policy",
                "Freshness policy must be default, cache-only, or refresh.");
        }

        // Inline rules are the only source kind, so the binder has already refused any other kind.
        OperationResult<CategoryRuleSet> grammar = CategoryRuleSetValidator.Validate(
            ((InlineCategoryRuleSource)source).RuleSet);
        if (grammar is not OperationSuccess<CategoryRuleSet> validRules)
        {
            return OperationFailure.Forward<CategoryRuleSet, ResolvedDeckCategoryRules>(grammar);
        }

        OperationInvalidInput? categoryFailure = ValidateCategoryOwnership(validRules.Data, deck);
        if (categoryFailure is not null)
        {
            return categoryFailure;
        }

        List<CategoryTagSelector> selectors = CollectSelectors(validRules.Data);
        if (selectors.Count == 0)
        {
            return new OperationSuccess<ResolvedDeckCategoryRules>(new ResolvedDeckCategoryRules(
                validRules.Data,
                null));
        }

        OperationResult<ScryfallTagResolution> sourceTags = await scryfall.ResolveDeckTagIdentitiesAsync(
            selectors.Select(selector => new ScryfallTagIdentity(
                selector.TagType,
                selector.TagId,
                selector.ExactSlug)).ToArray(),
            freshnessPolicy,
            cancellationToken).ConfigureAwait(false);
        if (sourceTags is not OperationSuccess<ScryfallTagResolution> resolvedTags)
        {
            return OperationFailure.Forward<ScryfallTagResolution, ResolvedDeckCategoryRules>(sourceTags);
        }

        if (resolvedTags.Data.TagIds.Count != selectors.Count)
        {
            return new OperationUnavailable(
                "scryfall-tag-resolution-mismatch",
                "Scryfall returned an incomplete category-rule tag resolution.");
        }

        return new OperationSuccess<ResolvedDeckCategoryRules>(new ResolvedDeckCategoryRules(
            BindTagIds(validRules.Data, resolvedTags.Data.TagIds),
            resolvedTags.Data.GenerationId));
    }

    /// <summary>
    /// Validates that every rule targets one category owned by the current local deck.
    /// </summary>
    private static OperationInvalidInput? ValidateCategoryOwnership(CategoryRuleSet rules, DeckDocument deck)
    {
        HashSet<Guid> categories = deck.Categories.Select(category => category.CategoryId).ToHashSet();
        return rules.Rules.Any(rule => !categories.Contains(rule.CategoryId))
            ? new OperationInvalidInput(
                "invalid-category-rule-category",
                "Every category rule must name an existing local deck category.")
            : null;
    }

    /// <summary>
    /// Collects selectors in the same deterministic order used when their IDs are rebound.
    /// </summary>
    private static List<CategoryTagSelector> CollectSelectors(CategoryRuleSet rules)
    {
        List<CategoryTagSelector> selectors = [];
        foreach (CategoryRule rule in rules.Rules)
        {
            selectors.AddRange(rule.AllOf);
            selectors.AddRange(rule.AnyOf);
            selectors.AddRange(rule.NoneOf);
        }

        return selectors;
    }

    /// <summary>
    /// Replaces every exact source selector identity with the matching installed source ID.
    /// </summary>
    private static CategoryRuleSet BindTagIds(CategoryRuleSet rules, IReadOnlyList<Guid> tagIds)
    {
        int index = 0;
        List<CategoryRule> boundRules = [];
        foreach (CategoryRule rule in rules.Rules)
        {
            boundRules.Add(rule with
            {
                AllOf = BindGroup(rule.AllOf, tagIds, ref index),
                AnyOf = BindGroup(rule.AnyOf, tagIds, ref index),
                NoneOf = BindGroup(rule.NoneOf, tagIds, ref index),
            });
        }

        return new CategoryRuleSet(rules.AssignmentMode, boundRules);
    }

    /// <summary>
    /// Replaces one selector group's identities in source resolution order.
    /// </summary>
    private static IReadOnlyList<CategoryTagSelector> BindGroup(
        IReadOnlyList<CategoryTagSelector> selectors,
        IReadOnlyList<Guid> tagIds,
        ref int index)
    {
        List<CategoryTagSelector> bound = [];
        foreach (CategoryTagSelector selector in selectors)
        {
            bound.Add(selector with { TagId = tagIds[index++], ExactSlug = null });
        }

        return bound;
    }

}
