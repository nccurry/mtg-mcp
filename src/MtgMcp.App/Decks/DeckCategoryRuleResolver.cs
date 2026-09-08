using System.Security.Cryptography;
using System.Text;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Decks;

/// <summary>
/// Carries category rules after local ownership checks and exact source-tag resolution.
/// </summary>
internal sealed record ResolvedDeckCategoryRules(
    CategoryRuleSet Rules,
    Guid? ScryfallGenerationId,
    int? PresetSchemaVersion,
    string? PresetChecksum);

/// <summary>
/// Expands category rule sources and resolves their tag identities against installed Scryfall data.
/// </summary>
internal sealed class DeckCategoryRuleResolver
{
    /// <summary>
    /// Defines the verified official source tags behind stable common-v1 role keys.
    /// </summary>
    private static readonly (string RoleKey, Guid TagId)[] CommonPresetSourceTags =
    [
        ("ramp", Guid.Parse("2f3e4ad7-5e60-41b4-bdbc-653f16869cf6")),
        ("card-draw", Guid.Parse("b6448c45-ce65-4848-aa98-2151e4e07437")),
        ("removal", Guid.Parse("444f824c-f910-4530-9dbe-ede7a84cd7f9")),
        ("recursion", Guid.Parse("82b824ad-648f-467f-a190-2e0fa9a795d2")),
    ];

    /// <summary>
    /// Identifies the complete checked-in common-v1 source-tag definition.
    /// </summary>
    private static readonly string CommonPresetChecksum = ComputeCommonPresetChecksum();

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

        OperationResult<CategoryRuleSet> expanded = Expand(source);
        if (expanded is not OperationSuccess<CategoryRuleSet> expandedRules)
        {
            return ForwardFailure<CategoryRuleSet, ResolvedDeckCategoryRules>(expanded);
        }

        OperationResult<CategoryRuleSet> grammar = CategoryRuleSetValidator.Validate(expandedRules.Data);
        if (grammar is not OperationSuccess<CategoryRuleSet> validRules)
        {
            return ForwardFailure<CategoryRuleSet, ResolvedDeckCategoryRules>(grammar);
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
                null,
                PresetSchemaVersion(source),
                PresetChecksum(source)));
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
            return ForwardFailure<ScryfallTagResolution, ResolvedDeckCategoryRules>(sourceTags);
        }

        if (resolvedTags.Data.TagIds.Count != selectors.Count)
        {
            return new OperationUnavailable(
                "scryfall-tag-resolution-mismatch",
                "Scryfall returned an incomplete category-rule tag resolution.");
        }

        return new OperationSuccess<ResolvedDeckCategoryRules>(new ResolvedDeckCategoryRules(
            BindTagIds(validRules.Data, resolvedTags.Data.TagIds),
            resolvedTags.Data.GenerationId,
            PresetSchemaVersion(source),
            PresetChecksum(source)));
    }

    /// <summary>
    /// Expands an inline rule set or the supported common-v1 preset.
    /// </summary>
    private static OperationResult<CategoryRuleSet> Expand(CategoryRuleSource source)
    {
        if (source is InlineCategoryRuleSource inline)
        {
            return new OperationSuccess<CategoryRuleSet>(inline.RuleSet);
        }

        if (source is not CommonPresetCategoryRuleSource preset ||
            !string.Equals(preset.PresetId, "common-v1", StringComparison.Ordinal) ||
            preset.Bindings is null ||
            preset.Bindings.Count == 0 ||
            preset.Bindings.Select(binding => binding?.RoleKey).Distinct(StringComparer.Ordinal).Count() != preset.Bindings.Count)
        {
            return new OperationInvalidInput("invalid-category-preset", "Only common-v1 with unique category bindings is supported.");
        }

        List<CategoryRule> rules = [];
        foreach (CategoryRoleBinding? binding in preset.Bindings)
        {
            if (binding is null || binding.CategoryId == Guid.Empty || rules.Any(rule => rule.CategoryId == binding.CategoryId))
            {
                return new OperationInvalidInput(
                    "invalid-category-binding",
                    "Preset bindings require unique non-empty category IDs.");
            }

            CategoryTagSelector? selector = CommonPresetSelector(binding.RoleKey);
            if (selector is null)
            {
                return new OperationInvalidInput("invalid-category-role", "The preset role key is not supported.");
            }

            rules.Add(new CategoryRule(
                binding.CategoryId,
                [selector],
                [],
                [],
                binding.PrimaryPriority));
        }

        return new OperationSuccess<CategoryRuleSet>(new CategoryRuleSet(preset.AssignmentMode, rules));
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

    /// <summary>
    /// Gets one verified source selector for a stable common-v1 role key.
    /// </summary>
    private static CategoryTagSelector? CommonPresetSelector(string roleKey)
    {
        foreach ((string key, Guid tagId) in CommonPresetSourceTags)
        {
            if (string.Equals(roleKey, key, StringComparison.Ordinal))
            {
                return new CategoryTagSelector("oracle", tagId, IncludeDescendants: true, MinimumWeight: "weak");
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the common-preset metadata only when the source selects that preset.
    /// </summary>
    private static int? PresetSchemaVersion(CategoryRuleSource source)
    {
        return source is CommonPresetCategoryRuleSource ? 1 : null;
    }

    /// <summary>
    /// Gets the checked-in preset checksum only when the source selects that preset.
    /// </summary>
    private static string? PresetChecksum(CategoryRuleSource source)
    {
        return source is CommonPresetCategoryRuleSource ? CommonPresetChecksum : null;
    }

    /// <summary>
    /// Computes a stable checksum over every source ID and descendant setting in common-v1.
    /// </summary>
    private static string ComputeCommonPresetChecksum()
    {
        string definition = string.Join(
            '|',
            CommonPresetSourceTags.Select(value => $"{value.RoleKey}:{value.TagId:D}:include-descendants:weak"));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(definition)));
    }

    /// <summary>
    /// Maps a non-success result to a category-rule resolution result.
    /// </summary>
    private static OperationResult<TTarget> ForwardFailure<TSource, TTarget>(OperationResult<TSource> result)
    {
        return result switch
        {
            OperationNotFound value => value,
            OperationNotCached value => value,
            OperationUnsupported value => value,
            OperationUnavailable value => value,
            OperationConflict value => value,
            OperationInvalidInput value => value,
            OperationSuccess<TSource> => new OperationUnavailable("unexpected-result", "The operation returned an unexpected result."),
            _ => new OperationUnavailable("unexpected-result", "The operation returned an unexpected result."),
        };
    }
}
