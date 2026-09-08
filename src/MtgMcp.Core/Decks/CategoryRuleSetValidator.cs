using MtgMcp.Core.Results;

namespace MtgMcp.Core.Decks;

/// <summary>Checks the closed category-rule grammar before source data is read.</summary>
public static class CategoryRuleSetValidator
{
    /// <summary>Validates a caller-supplied category rule set without consulting a provider.</summary>
    public static OperationResult<CategoryRuleSet> Validate(CategoryRuleSet? rules)
    {
        if (rules is null ||
            rules.AssignmentMode is not ("add-only" or "synchronize-listed-categories") ||
            rules.Rules is null ||
            rules.Rules.Count == 0)
        {
            return Invalid("Rules require a supported assignment mode and at least one category rule.");
        }

        HashSet<Guid> categoryIds = [];
        HashSet<int> primaryPriorities = [];
        foreach (CategoryRule? rule in rules.Rules)
        {
            if (rule is null ||
                rule.CategoryId == Guid.Empty ||
                !categoryIds.Add(rule.CategoryId) ||
                !HasValidSelectors(rule.AllOf) ||
                !HasValidSelectors(rule.AnyOf) ||
                !HasValidSelectors(rule.NoneOf) ||
                (rule.PrimaryPriority is int priority && !primaryPriorities.Add(priority)))
            {
                return Invalid("Every category rule must have a unique category, valid selector groups, and a unique primary priority when supplied.");
            }
        }

        return new OperationSuccess<CategoryRuleSet>(rules);
    }

    /// <summary>Checks one selector group and every selector it contains.</summary>
    private static bool HasValidSelectors(IReadOnlyList<CategoryTagSelector>? selectors)
    {
        if (selectors is null)
        {
            return false;
        }

        foreach (CategoryTagSelector? selector in selectors)
        {
            if (!HasValidSelector(selector))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Checks one exact tag identity and its closed source vocabulary.</summary>
    private static bool HasValidSelector(CategoryTagSelector? selector)
    {
        if (selector is null ||
            selector.TagType is not ("oracle" or "art") ||
            selector.MinimumWeight is not ("weak" or "median" or "strong" or "very-strong"))
        {
            return false;
        }

        bool hasEmptyId = selector.TagId == Guid.Empty;
        bool hasTagId = selector.TagId is Guid tagId && tagId != Guid.Empty;
        bool hasBlankSlug = selector.ExactSlug is not null && string.IsNullOrWhiteSpace(selector.ExactSlug);
        bool hasSlug = !string.IsNullOrWhiteSpace(selector.ExactSlug);
        return !hasEmptyId && !hasBlankSlug && hasTagId != hasSlug;
    }

    /// <summary>Creates one stable invalid-input result for malformed rule grammar.</summary>
    private static OperationInvalidInput Invalid(string message)
    {
        return new OperationInvalidInput("invalid-category-rules", message);
    }
}
