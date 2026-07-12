using MtgMcp.Core.Decks;

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
        CategoryEvaluation result = DeckCategorizationEvaluator.Evaluate(
            new CategoryRuleSet("add-only", [new CategoryRule(
                categoryId,
                [new CategoryTagSelector("oracle", ExactSlug: "ramp", MinimumWeight: "median")],
                [], [])]),
            [new CategoryEntryEvidence(entryId, [new CategoryTagEvidence(
                Guid.NewGuid(), "oracle", "ramp", "strong", [])])],
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
                [new CategoryTagSelector("oracle", ExactSlug: "ramp")],
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
        CategoryEvaluation result = DeckCategorizationEvaluator.Evaluate(
            new CategoryRuleSet("synchronize-listed-categories", [new CategoryRule(owned, [], [new CategoryTagSelector("oracle", ExactSlug: "ramp")], [])]),
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
        CategoryRuleSet rules = new("add-only", [new CategoryRule(categoryId, [], [new CategoryTagSelector("oracle", ExactSlug: "draw")], [])]);
        CategoryEvaluation left = DeckCategorizationEvaluator.Evaluate(
            rules,
            [new CategoryEntryEvidence(second, [new CategoryTagEvidence(Guid.NewGuid(), "oracle", "draw", "weak", [])]), new CategoryEntryEvidence(first, [])],
            []);
        CategoryEvaluation right = DeckCategorizationEvaluator.Evaluate(
            rules,
            [new CategoryEntryEvidence(first, []), new CategoryEntryEvidence(second, [new CategoryTagEvidence(Guid.NewGuid(), "oracle", "draw", "weak", [])])],
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
        CategoryEvaluation result = DeckCategorizationEvaluator.Evaluate(
            new CategoryRuleSet("add-only", [
                new CategoryRule(first, [], [new CategoryTagSelector("oracle", ExactSlug: "ramp")], [], 20),
                new CategoryRule(second, [], [new CategoryTagSelector("oracle", ExactSlug: "ramp")], [], 10),
            ]),
            [new CategoryEntryEvidence(entryId, [new CategoryTagEvidence(Guid.NewGuid(), "oracle", "ramp", "strong", [])])],
            []);

        Assert.Contains(result.ProposedAssignments, value => value.CategoryId == second && value.IsPrimary);
        Assert.DoesNotContain(result.ProposedAssignments, value => value.CategoryId == first && value.IsPrimary);
    }

    /// <summary>Matches a descendant only when the selector explicitly allows it.</summary>
    [Fact]
    public void Evaluate_DescendantSelector_UsesHierarchyPath()
    {
        Guid parent = Guid.NewGuid();
        Guid child = Guid.NewGuid();
        Guid category = Guid.NewGuid();
        CategoryEvaluation result = DeckCategorizationEvaluator.Evaluate(
            new CategoryRuleSet("add-only", [new CategoryRule(category, [], [new CategoryTagSelector(
                "oracle", parent, IncludeDescendants: true)], [])]),
            [new CategoryEntryEvidence(Guid.NewGuid(), [new CategoryTagEvidence(child, "oracle", "child", "strong", [parent, child])])],
            []);

        Assert.Equal("matched", Assert.Single(result.Decisions).Status);
    }
}
