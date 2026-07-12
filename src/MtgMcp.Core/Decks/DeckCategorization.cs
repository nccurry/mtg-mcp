using System.Text.Json.Serialization;

namespace MtgMcp.Core.Decks;

/// <summary>Identifies how a category rule source was supplied.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(InlineCategoryRuleSource), "inline")]
[JsonDerivedType(typeof(CommonPresetCategoryRuleSource), "preset")]
public abstract record CategoryRuleSource;

/// <summary>Supplies the complete caller-authored category rules.</summary>
public sealed record InlineCategoryRuleSource(CategoryRuleSet RuleSet) : CategoryRuleSource;

/// <summary>Selects the immutable common preset and caller-owned category bindings.</summary>
public sealed record CommonPresetCategoryRuleSource(
    string PresetId,
    string AssignmentMode,
    IReadOnlyList<CategoryRoleBinding> Bindings) : CategoryRuleSource;

/// <summary>Binds one preset role to an existing local category.</summary>
public sealed record CategoryRoleBinding(string RoleKey, Guid CategoryId, int? PrimaryPriority = null);

/// <summary>Defines deterministic category ownership and tag selectors.</summary>
public sealed record CategoryRuleSet(string AssignmentMode, IReadOnlyList<CategoryRule> Rules);

/// <summary>Defines one category and its exact tag selector groups.</summary>
public sealed record CategoryRule(
    Guid CategoryId,
    IReadOnlyList<CategoryTagSelector> AllOf,
    IReadOnlyList<CategoryTagSelector> AnyOf,
    IReadOnlyList<CategoryTagSelector> NoneOf,
    int? PrimaryPriority = null);

/// <summary>Identifies one exact Oracle or art tag selector.</summary>
public sealed record CategoryTagSelector(
    string TagType,
    Guid? TagId = null,
    string? ExactSlug = null,
    bool IncludeDescendants = false,
    string MinimumWeight = "weak");

/// <summary>Provides provider-neutral tag evidence for one deck entry.</summary>
public sealed record CategoryEntryEvidence(
    Guid EntryId,
    IReadOnlyList<CategoryTagEvidence> Tags,
    bool IsComplete = true);

/// <summary>Provides one normalized tag assignment and hierarchy path.</summary>
public sealed record CategoryTagEvidence(
    Guid TagId,
    string TagType,
    string Slug,
    string Weight,
    IReadOnlyList<Guid> HierarchyPath);

/// <summary>Reports one category decision for one entry.</summary>
public sealed record CategoryDecision(
    Guid EntryId,
    Guid CategoryId,
    string Status,
    IReadOnlyList<Guid> MatchedTagIds,
    string Message);

/// <summary>Reports deterministic category evaluation results.</summary>
public sealed record CategoryEvaluation(
    IReadOnlyList<CategoryDecision> Decisions,
    IReadOnlyList<DeckCategoryAssignment> ProposedAssignments,
    IReadOnlyList<string> Warnings);

/// <summary>Evaluates exact category rules without provider or deck-store dependencies.</summary>
public static class DeckCategorizationEvaluator
{
    /// <summary>Evaluates all rules over supplied entry evidence and current assignments.</summary>
    public static CategoryEvaluation Evaluate(
        CategoryRuleSet rules,
        IReadOnlyList<CategoryEntryEvidence> evidence,
        IReadOnlyList<DeckCategoryAssignment> currentAssignments)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(currentAssignments);
        List<CategoryDecision> decisions = [];
        Dictionary<Guid, List<DeckCategoryAssignment>> current = currentAssignments
            .GroupBy(value => value.EntryId)
            .ToDictionary(value => value.Key, value => value.ToList());
        foreach (CategoryEntryEvidence entry in evidence.OrderBy(value => value.EntryId))
        {
            bool entryUnknown = !entry.IsComplete;
            foreach (CategoryRule rule in rules.Rules.OrderBy(value => value.CategoryId))
            {
                List<Guid> matched = [];
                bool unknown = entryUnknown;
                bool all = GroupMatches(rule.AllOf, entry.Tags, matched, ref unknown, true);
                bool any = GroupMatches(rule.AnyOf, entry.Tags, matched, ref unknown, false);
                bool none = GroupMatches(rule.NoneOf, entry.Tags, matched, ref unknown, false, negate: true);
                string status = unknown ? "unknown" : all && any && none ? "matched" : "unmatched";
                decisions.Add(new CategoryDecision(
                    entry.EntryId,
                    rule.CategoryId,
                    status,
                    matched.Distinct().OrderBy(value => value).ToArray(),
                    unknown ? "Tag evidence is incomplete; no destructive removal is authorized." :
                        status == "matched" ? "All category selectors evaluated true." : "Category selectors did not match."));
            }
        }

        List<DeckCategoryAssignment> proposed = currentAssignments.ToList();
        foreach (CategoryRule rule in rules.Rules)
        {
            foreach (CategoryEntryEvidence entry in evidence)
            {
                CategoryDecision decision = decisions.First(value =>
                    value.EntryId == entry.EntryId && value.CategoryId == rule.CategoryId);
                bool exists = proposed.Any(value => value.EntryId == entry.EntryId && value.CategoryId == rule.CategoryId);
                if (decision.Status == "matched" && !exists)
                {
                    proposed.Add(new DeckCategoryAssignment(entry.EntryId, rule.CategoryId, false));
                }
                if (rules.AssignmentMode == "synchronize-listed-categories" &&
                    decision.Status == "unmatched" && exists)
                {
                    proposed.RemoveAll(value => value.EntryId == entry.EntryId && value.CategoryId == rule.CategoryId);
                }
            }
        }

        foreach (CategoryEntryEvidence entry in evidence)
        {
            CategoryRule? primaryRule = rules.Rules
                .Where(rule => rule.PrimaryPriority is not null && decisions.Any(decision =>
                    decision.EntryId == entry.EntryId && decision.CategoryId == rule.CategoryId && decision.Status == "matched"))
                .OrderBy(rule => rule.PrimaryPriority)
                .FirstOrDefault();
            if (primaryRule is null)
            {
                continue;
            }

            for (int index = 0; index < proposed.Count; index++)
            {
                DeckCategoryAssignment assignment = proposed[index];
                if (assignment.EntryId == entry.EntryId &&
                    rules.Rules.Any(rule => rule.CategoryId == assignment.CategoryId))
                {
                    proposed[index] = assignment with
                    {
                        IsPrimary = assignment.CategoryId == primaryRule.CategoryId,
                    };
                }
            }
        }

        return new CategoryEvaluation(decisions, proposed, []);
    }

    /// <summary>Evaluates one selector group while preserving unknown evidence.</summary>
    private static bool GroupMatches(
        IReadOnlyList<CategoryTagSelector> selectors,
        IReadOnlyList<CategoryTagEvidence> tags,
        List<Guid> matched,
        ref bool unknown,
        bool allRequired,
        bool negate = false)
    {
        if (selectors.Count == 0)
        {
            return true;
        }

        int matches = 0;
        foreach (CategoryTagSelector selector in selectors)
        {
            CategoryTagEvidence? tag = tags.FirstOrDefault(value => Matches(selector, value));
            if (tag is null)
            {
                continue;
            }

            matches++;
            matched.Add(tag.TagId);
        }

        bool result = allRequired ? matches == selectors.Count : matches > 0;
        return negate ? !result : result;
    }

    /// <summary>Matches a selector against one exact tag assignment.</summary>
    private static bool Matches(CategoryTagSelector selector, CategoryTagEvidence tag)
    {
        if (!string.Equals(selector.TagType, tag.TagType, StringComparison.Ordinal) ||
            !WeightAtLeast(tag.Weight, selector.MinimumWeight))
        {
            return false;
        }

        bool identity = selector.TagId is Guid id
            ? tag.TagId == id || (selector.IncludeDescendants && tag.HierarchyPath.Contains(id))
            : !string.IsNullOrWhiteSpace(selector.ExactSlug) &&
              string.Equals(tag.Slug, selector.ExactSlug, StringComparison.Ordinal);
        return identity;
    }

    /// <summary>Compares the closed evidence weight vocabulary.</summary>
    private static bool WeightAtLeast(string actual, string required)
    {
        string[] order = ["weak", "median", "strong", "very-strong"];
        int actualIndex = Array.IndexOf(order, actual);
        int requiredIndex = Array.IndexOf(order, required);
        return actualIndex >= 0 && requiredIndex >= 0 && actualIndex >= requiredIndex;
    }
}
