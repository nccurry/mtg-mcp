using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;

namespace MtgMcp.Core.Tests;

/// <summary>Verifies deterministic category evaluation over supplied tag evidence.</summary>
public sealed class DeckCategorizationTests
{
    /// <summary>Matches an exact weighted tag and adds the requested category.</summary>
    [Fact]
    public void Evaluate_ExactTag_AddsCategory()
    {
        Guid entryId = Guid.NewGuid();
        Guid categoryId = Guid.NewGuid();
        Guid tagId = Guid.NewGuid();
        CategoryEvaluation result = DeckCategorizationEvaluator.Evaluate(
            new CategoryRuleSet("add-only", [new CategoryRule(
                categoryId,
                [new CategoryTagSelector("oracle", tagId, MinimumWeight: "median")],
                [], [])]),
            [new CategoryEntryEvidence(entryId, [new CategoryTagEvidence(
                tagId, "oracle", "ramp", "strong", [])])],
            []);

        Assert.Equal("matched", Assert.Single(result.Decisions).Status);
        Assert.Contains(result.ProposedAssignments, value => value.EntryId == entryId && value.CategoryId == categoryId);
    }

    /// <summary>Reports unknown evidence without creating a category assignment.</summary>
    [Fact]
    public void Evaluate_MissingEvidence_RemainsUnknown()
    {
        CategoryEvaluation result = DeckCategorizationEvaluator.Evaluate(
            new CategoryRuleSet("add-only", [new CategoryRule(
                Guid.NewGuid(),
                [new CategoryTagSelector("oracle", Guid.NewGuid())],
                [], [])]),
            [new CategoryEntryEvidence(Guid.NewGuid(), [])],
            []);

        Assert.Equal("unmatched", Assert.Single(result.Decisions).Status);
        Assert.Empty(result.ProposedAssignments);
    }

    /// <summary>Removes only rule-owned unmatched assignments in synchronize mode.</summary>
    [Fact]
    public void Evaluate_Synchronize_RemovesOnlyOwnedCategory()
    {
        Guid entryId = Guid.NewGuid();
        Guid owned = Guid.NewGuid();
        Guid unrelated = Guid.NewGuid();
        Guid tagId = Guid.NewGuid();
        CategoryEvaluation result = DeckCategorizationEvaluator.Evaluate(
            new CategoryRuleSet("synchronize-listed-categories", [new CategoryRule(owned, [], [new CategoryTagSelector("oracle", tagId)], [])]),
            [new CategoryEntryEvidence(entryId, [])],
            [new DeckCategoryAssignment(entryId, owned, false), new DeckCategoryAssignment(entryId, unrelated, false)]);

        Assert.DoesNotContain(result.ProposedAssignments, value => value.CategoryId == owned);
        Assert.Contains(result.ProposedAssignments, value => value.CategoryId == unrelated);
    }

    /// <summary>Produces stable decisions regardless of input evidence order.</summary>
    [Fact]
    public void Evaluate_ReorderedEvidence_IsStable()
    {
        Guid categoryId = Guid.NewGuid();
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        Guid drawTagId = Guid.NewGuid();
        CategoryRuleSet rules = new("add-only", [new CategoryRule(categoryId, [], [new CategoryTagSelector("oracle", drawTagId)], [])]);
        CategoryEvaluation left = DeckCategorizationEvaluator.Evaluate(
            rules,
            [new CategoryEntryEvidence(second, [new CategoryTagEvidence(drawTagId, "oracle", "draw", "weak", [])]), new CategoryEntryEvidence(first, [])],
            []);
        CategoryEvaluation right = DeckCategorizationEvaluator.Evaluate(
            rules,
            [new CategoryEntryEvidence(first, []), new CategoryEntryEvidence(second, [new CategoryTagEvidence(drawTagId, "oracle", "draw", "weak", [])])],
            []);

        Assert.Equal(left.Decisions.Select(value => value.Status), right.Decisions.Select(value => value.Status));
    }

    /// <summary>Uses the lowest primary priority for matched categories.</summary>
    [Fact]
    public void Evaluate_PrimaryPriority_SelectsLowest()
    {
        Guid entryId = Guid.NewGuid();
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        Guid tagId = Guid.NewGuid();
        CategoryEvaluation result = DeckCategorizationEvaluator.Evaluate(
            new CategoryRuleSet("add-only", [
                new CategoryRule(first, [], [new CategoryTagSelector("oracle", tagId)], [], 20),
                new CategoryRule(second, [], [new CategoryTagSelector("oracle", tagId)], [], 10),
            ]),
            [new CategoryEntryEvidence(entryId, [new CategoryTagEvidence(tagId, "oracle", "ramp", "strong", [])])],
            []);

        Assert.Contains(result.ProposedAssignments, value => value.CategoryId == second && value.IsPrimary);
        Assert.DoesNotContain(result.ProposedAssignments, value => value.CategoryId == first && value.IsPrimary);
    }

    /// <summary>Matches a source ancestor only when the selector explicitly allows descendants.</summary>
    [Fact]
    public void Evaluate_DescendantSelector_UsesAncestorTagIds()
    {
        Guid parent = Guid.NewGuid();
        Guid child = Guid.NewGuid();
        Guid category = Guid.NewGuid();
        CategoryEvaluation result = DeckCategorizationEvaluator.Evaluate(
            new CategoryRuleSet("add-only", [new CategoryRule(category, [], [new CategoryTagSelector(
                "oracle", parent, IncludeDescendants: true)], [])]),
            [new CategoryEntryEvidence(Guid.NewGuid(), [new CategoryTagEvidence(child, "oracle", "child", "strong", [parent])])],
            []);

        Assert.Equal("matched", Assert.Single(result.Decisions).Status);
    }

    /// <summary>Does not treat a source ancestor as a direct tag when descendants are disabled.</summary>
    [Fact]
    public void Evaluate_AncestorSelectorWithoutDescendants_DoesNotMatch()
    {
        Guid parent = Guid.NewGuid();
        Guid child = Guid.NewGuid();
        CategoryEvaluation result = DeckCategorizationEvaluator.Evaluate(
            new CategoryRuleSet("add-only", [new CategoryRule(
                Guid.NewGuid(), [], [new CategoryTagSelector("oracle", parent)], [])]),
            [new CategoryEntryEvidence(Guid.NewGuid(), [new CategoryTagEvidence(
                child, "oracle", "child", "strong", [parent])])],
            []);

        Assert.Equal("unmatched", Assert.Single(result.Decisions).Status);
    }

    /// <summary>Matches each reachable source parent instead of retaining only one path.</summary>
    [Fact]
    public void Evaluate_MultipleSourceParents_MatchesEachEnabledParent()
    {
        Guid firstParent = Guid.NewGuid();
        Guid secondParent = Guid.NewGuid();
        Guid child = Guid.NewGuid();
        Guid firstCategory = Guid.NewGuid();
        Guid secondCategory = Guid.NewGuid();
        CategoryEvaluation result = DeckCategorizationEvaluator.Evaluate(
            new CategoryRuleSet("add-only", [
                new CategoryRule(firstCategory, [], [new CategoryTagSelector("oracle", firstParent, IncludeDescendants: true)], []),
                new CategoryRule(secondCategory, [], [new CategoryTagSelector("oracle", secondParent, IncludeDescendants: true)], []),
            ]),
            [new CategoryEntryEvidence(Guid.NewGuid(), [new CategoryTagEvidence(
                child, "oracle", "child", "strong", [firstParent, secondParent])])],
            []);

        Assert.All(result.Decisions, value => Assert.Equal("matched", value.Status));
    }

    /// <summary>Rejects closed-rule grammar errors before any source lookup can occur.</summary>
    [Theory]
    [MemberData(nameof(InvalidRuleSets))]
    public void Validate_InvalidRuleSet_ReturnsInvalidInput(CategoryRuleSet? rules)
    {
        OperationResult<CategoryRuleSet> result = CategoryRuleSetValidator.Validate(rules);

        Assert.Equal("invalid-category-rules", Assert.IsType<OperationInvalidInput>(result.Value).ReasonCode);
    }

    /// <summary>Accepts a complete rule set that uses one exact source identity per selector.</summary>
    [Fact]
    public void Validate_ValidRuleSet_ReturnsOriginalRules()
    {
        CategoryRuleSet rules = new("add-only", [new CategoryRule(
            Guid.NewGuid(),
            [new CategoryTagSelector("oracle", Guid.NewGuid())],
            [],
            [new CategoryTagSelector("art", ExactSlug: "sunset", MinimumWeight: "strong")],
            PrimaryPriority: 10)]);

        OperationResult<CategoryRuleSet> result = CategoryRuleSetValidator.Validate(rules);

        Assert.Same(rules, Assert.IsType<OperationSuccess<CategoryRuleSet>>(result.Value).Data);
    }

    /// <summary>Supplies malformed rule sets for the closed-grammar test matrix.</summary>
    public static TheoryData<CategoryRuleSet?> InvalidRuleSets => new()
    {
        (CategoryRuleSet?)null,
        new CategoryRuleSet("replace-all", []),
        new CategoryRuleSet("add-only", []),
        new CategoryRuleSet("add-only", [new CategoryRule(Guid.Empty, [], [], [])]),
        new CategoryRuleSet("add-only", [
            new CategoryRule(Guid.Parse("11111111-1111-4111-8111-111111111111"), [], [], []),
            new CategoryRule(Guid.Parse("11111111-1111-4111-8111-111111111111"), [], [], []),
        ]),
        new CategoryRuleSet("add-only", [
            new CategoryRule(Guid.NewGuid(), [], [], [], 1),
            new CategoryRule(Guid.NewGuid(), [], [], [], 1),
        ]),
        new CategoryRuleSet("add-only", [new CategoryRule(Guid.NewGuid(), null!, [], [])]),
        new CategoryRuleSet("add-only", [new CategoryRule(Guid.NewGuid(), [null!], [], [])]),
        new CategoryRuleSet("add-only", [new CategoryRule(Guid.NewGuid(), [new CategoryTagSelector("oracle")], [], [])]),
        new CategoryRuleSet("add-only", [new CategoryRule(Guid.NewGuid(), [new CategoryTagSelector("oracle", Guid.Empty)], [], [])]),
        new CategoryRuleSet("add-only", [new CategoryRule(Guid.NewGuid(), [new CategoryTagSelector("oracle", Guid.NewGuid(), "ramp")], [], [])]),
        new CategoryRuleSet("add-only", [new CategoryRule(Guid.NewGuid(), [new CategoryTagSelector("oracle", ExactSlug: " ")], [], [])]),
        new CategoryRuleSet("add-only", [new CategoryRule(Guid.NewGuid(), [new CategoryTagSelector("unsupported", Guid.NewGuid())], [], [])]),
        new CategoryRuleSet("add-only", [new CategoryRule(Guid.NewGuid(), [new CategoryTagSelector("oracle", Guid.NewGuid(), MinimumWeight: "rare")], [], [])]),
    };
}
