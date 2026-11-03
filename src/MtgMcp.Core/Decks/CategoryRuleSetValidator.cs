using MtgMcp.Core.Results;

namespace MtgMcp.Core.Decks;

/// <summary>Checks the closed category-rule grammar before source data is read.</summary>
public static class CategoryRuleSetValidator
{
    /// <summary>
    /// Validates a caller-supplied category rule set without consulting a provider. A refusal names the argument path
    /// of the rejected value, built from <paramref name="path"/>, the path of the rule set in the tool arguments.
    /// </summary>
    public static OperationResult<CategoryRuleSet> Validate(CategoryRuleSet? rules, string path = "source.ruleSet")
    {
        if (rules is null ||
            rules.AssignmentMode is not ("add-only" or "synchronize-listed-categories") ||
            rules.Rules is null ||
            rules.Rules.Count == 0)
        {
            return Invalid(
                "Rules require a supported assignment mode and at least one category rule.",
                rules?.AssignmentMode is "add-only" or "synchronize-listed-categories"
                    ? path + ".rules"
                    : path + ".assignmentMode");
        }

        HashSet<Guid> categoryIds = [];
        HashSet<int> primaryPriorities = [];
        for (int index = 0; index < rules.Rules.Count; index++)
        {
            CategoryRule? rule = rules.Rules[index];
            if (rule is null ||
                rule.CategoryId == Guid.Empty ||
                !categoryIds.Add(rule.CategoryId) ||
                !HasValidSelectors(rule.AllOf) ||
                !HasValidSelectors(rule.AnyOf) ||
                !HasValidSelectors(rule.NoneOf) ||
                (rule.PrimaryPriority is int priority && !primaryPriorities.Add(priority)))
            {
                return Invalid(
                    "Every category rule must have a unique category, valid selector groups, and a unique primary " +
                    "priority when supplied.",
                    $"{path}.rules[{index}]");
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
    private static OperationInvalidInput Invalid(string message, string path)
    {
        return new OperationInvalidInput("invalid-category-rules", message, path);
    }
}
