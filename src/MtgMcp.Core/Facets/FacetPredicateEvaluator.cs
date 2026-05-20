using System.Text.Json;

namespace MtgMcp.Core;

/// <summary>
/// Evaluates caller-supplied JSON predicates against normalized card facets.
/// </summary>
internal static class FacetPredicateEvaluator
{
    /// <summary>
    /// Evaluates a predicate JSON document against one card's facets.
    /// </summary>
    internal static CardFacetMatchResult Evaluate(
        CardFacetSnapshot card,
        string predicateJson)
    {
        using JsonDocument document = JsonDocument.Parse(predicateJson);
        List<FacetMatchEvidence> evidence = [];
        bool matched = EvaluateNode(document.RootElement, card, evidence);

        return new CardFacetMatchResult
        {
            WorkspaceId = card.WorkspaceId,
            CardName = card.CardName,
            Matched = matched,
            PredicateJson = JsonSerializer.Serialize(document.RootElement),
            Evidence = evidence
        };
    }

    /// <summary>
    /// Evaluates a predicate node and appends concrete evidence for leaf checks.
    /// </summary>
    private static bool EvaluateNode(
        JsonElement node,
        CardFacetSnapshot card,
        List<FacetMatchEvidence> evidence)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Facet predicate nodes must be JSON objects.");
        }

        if (node.TryGetProperty("all", out JsonElement all))
        {
            JsonElement[] children = ReadPredicateArray(all, "all");
            return children.All(child => EvaluateNode(child, card, evidence));
        }

        if (node.TryGetProperty("any", out JsonElement any))
        {
            JsonElement[] children = ReadPredicateArray(any, "any");
            return children.Any(child => EvaluateNode(child, card, evidence));
        }

        if (node.TryGetProperty("none", out JsonElement none))
        {
            JsonElement[] children = ReadPredicateArray(none, "none");
            return !children.Any(child => EvaluateNode(child, card, evidence));
        }

        return EvaluateLeaf(node, card, evidence);
    }

    /// <summary>
    /// Evaluates one concrete facet predicate.
    /// </summary>
    private static bool EvaluateLeaf(
        JsonElement node,
        CardFacetSnapshot card,
        List<FacetMatchEvidence> evidence)
    {
        string facetName = ReadRequiredString(node, "facet");
        card.Facets.TryGetValue(facetName, out CardFacet? facet);
        List<string> values = facet?.Values ?? [];
        string source = facet?.Source ?? SourceFromFacetName(facetName);

        if (node.TryGetProperty("exists", out JsonElement existsElement))
        {
            bool expected = existsElement.GetBoolean();
            bool matched = values.Count > 0 == expected;
            evidence.Add(CreateEvidence(facetName, source, "exists", [expected.ToString()], values, matched));
            return matched;
        }

        if (node.TryGetProperty("equals", out JsonElement equalsElement))
        {
            List<string> expected = ReadStringValues(equalsElement);
            bool matched = expected.Any(expectedValue =>
                values.Any(value => value.Equals(expectedValue, StringComparison.OrdinalIgnoreCase)));
            evidence.Add(CreateEvidence(facetName, source, "equals", expected, values, matched));
            return matched;
        }

        if (node.TryGetProperty("contains", out JsonElement containsElement))
        {
            List<string> expected = ReadStringValues(containsElement);
            bool matched = expected.Any(expectedValue =>
                values.Any(value => value.Contains(expectedValue, StringComparison.OrdinalIgnoreCase)));
            evidence.Add(CreateEvidence(facetName, source, "contains", expected, values, matched));
            return matched;
        }

        if (node.TryGetProperty("containsAny", out JsonElement containsAnyElement))
        {
            List<string> expected = ReadStringValues(containsAnyElement);
            bool matched = expected.Any(expectedValue =>
                values.Any(value => value.Contains(expectedValue, StringComparison.OrdinalIgnoreCase)));
            evidence.Add(CreateEvidence(facetName, source, "containsAny", expected, values, matched));
            return matched;
        }

        if (node.TryGetProperty("containsAll", out JsonElement containsAllElement))
        {
            List<string> expected = ReadStringValues(containsAllElement);
            bool matched = expected.All(expectedValue =>
                values.Any(value => value.Contains(expectedValue, StringComparison.OrdinalIgnoreCase)));
            evidence.Add(CreateEvidence(facetName, source, "containsAll", expected, values, matched));
            return matched;
        }

        if (node.TryGetProperty("greaterThanOrEqual", out JsonElement greaterThanOrEqualElement))
        {
            decimal expected = greaterThanOrEqualElement.GetDecimal();
            bool matched = values.Any(value => TryReadDecimal(value, out decimal actual) && actual >= expected);
            evidence.Add(CreateEvidence(facetName, source, "greaterThanOrEqual", [expected.ToString(System.Globalization.CultureInfo.InvariantCulture)], values, matched));
            return matched;
        }

        if (node.TryGetProperty("lessThanOrEqual", out JsonElement lessThanOrEqualElement))
        {
            decimal expected = lessThanOrEqualElement.GetDecimal();
            bool matched = values.Any(value => TryReadDecimal(value, out decimal actual) && actual <= expected);
            evidence.Add(CreateEvidence(facetName, source, "lessThanOrEqual", [expected.ToString(System.Globalization.CultureInfo.InvariantCulture)], values, matched));
            return matched;
        }

        throw new ArgumentException(
            $"Facet predicate for '{facetName}' must include exists, equals, contains, containsAny, containsAll, greaterThanOrEqual, or lessThanOrEqual.");
    }

    /// <summary>
    /// Reads a required string property from a predicate node.
    /// </summary>
    private static string ReadRequiredString(JsonElement node, string propertyName)
    {
        if (!node.TryGetProperty(propertyName, out JsonElement property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new ArgumentException($"Facet predicate nodes must include a non-empty '{propertyName}' string.");
        }

        return property.GetString() ?? "";
    }

    /// <summary>
    /// Reads a predicate array property.
    /// </summary>
    private static JsonElement[] ReadPredicateArray(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException($"Facet predicate '{propertyName}' must be an array.");
        }

        JsonElement[] values = element.EnumerateArray().ToArray();
        if (values.Length == 0)
        {
            throw new ArgumentException($"Facet predicate '{propertyName}' must contain at least one child predicate.");
        }

        return values;
    }

    /// <summary>
    /// Reads either one string value or an array of string values.
    /// </summary>
    private static List<string> ReadStringValues(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            string? value = element.GetString();
            return string.IsNullOrWhiteSpace(value) ? [] : [value];
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("Facet predicate values must be strings or arrays of strings.");
        }

        return element
            .EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value ?? "")
            .ToList();
    }

    /// <summary>
    /// Creates a predicate evidence row.
    /// </summary>
    private static FacetMatchEvidence CreateEvidence(
        string facet,
        string source,
        string operation,
        List<string> expected,
        List<string> actual,
        bool matched)
    {
        return new FacetMatchEvidence
        {
            Facet = facet,
            Source = source,
            Operation = operation,
            Expected = expected,
            Actual = actual,
            Matched = matched
        };
    }

    /// <summary>
    /// Infers a source family from the facet prefix when the facet is absent.
    /// </summary>
    private static string SourceFromFacetName(string facetName)
    {
        if (facetName.StartsWith("scryfall.", StringComparison.OrdinalIgnoreCase))
        {
            return CardFacetSourceNames.Scryfall;
        }

        if (facetName.StartsWith("tagger.", StringComparison.OrdinalIgnoreCase))
        {
            return CardFacetSourceNames.Tagger;
        }

        if (facetName.StartsWith("user.", StringComparison.OrdinalIgnoreCase))
        {
            return CardFacetSourceNames.User;
        }

        if (facetName.StartsWith("metadata.", StringComparison.OrdinalIgnoreCase))
        {
            return CardFacetSourceNames.Metadata;
        }

        return CardFacetSourceNames.Workspace;
    }

    /// <summary>
    /// Parses a decimal in invariant culture.
    /// </summary>
    private static bool TryReadDecimal(string value, out decimal result)
    {
        return decimal.TryParse(
            value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out result);
    }
}
