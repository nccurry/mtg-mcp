using System.Text.Json;
using System.Text.Json.Nodes;

namespace MtgMcp.App.Hosting;

/// <summary>
/// Rewrites a number that is written with a fractional part or an exponent but has a whole value, such as
/// <c>328.0</c> or <c>3.28e2</c>, as the plain integer, so an integer field reads it. JSON Schema "integer" admits
/// these numbers. A number field that is not an integer reads the plain form just as well. A number with a
/// fractional part is left alone and fails to bind to an integer field with the usual path.
/// </summary>
internal static class WholeNumberArguments
{
    /// <summary>
    /// Replaces each argument that contains such a number with the rewritten value.
    /// </summary>
    internal static void Normalize(IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null)
        {
            return;
        }

        foreach (string name in arguments.Keys.ToArray())
        {
            JsonElement value = arguments[name];
            if (NeedsRewrite(value))
            {
                arguments[name] = Rewrite(value);
            }
        }
    }

    /// <summary>
    /// Reports whether a value holds a number written with a fractional part or an exponent.
    /// </summary>
    private static bool NeedsRewrite(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                return IsWholeButNotInteger(value);
            case JsonValueKind.Array:
                return value.EnumerateArray().Any(NeedsRewrite);
            case JsonValueKind.Object:
                return value.EnumerateObject().Any(property => NeedsRewrite(property.Value));
            default:
                return false;
        }
    }

    /// <summary>
    /// Reports whether a number is written in a non-integer form but holds a whole value that a 64-bit integer holds.
    /// </summary>
    private static bool IsWholeButNotInteger(JsonElement number)
    {
        string text = number.GetRawText();
        return text.AsSpan().IndexOfAny('.', 'e', 'E') >= 0 && TryWhole(number, out _);
    }

    /// <summary>
    /// Reads a number whose value is whole and fits in 64 bits.
    /// </summary>
    private static bool TryWhole(JsonElement number, out long whole)
    {
        whole = 0;
        if (!number.TryGetDecimal(out decimal value) || value != decimal.Truncate(value) ||
            value < long.MinValue || value > long.MaxValue)
        {
            return false;
        }

        whole = (long)value;
        return true;
    }

    /// <summary>
    /// Builds a copy of a value with every such number written as an integer.
    /// </summary>
    private static JsonElement Rewrite(JsonElement value)
    {
        return JsonSerializer.SerializeToElement(RewriteNode(value));
    }

    /// <summary>
    /// Copies one value into a node tree, rewriting numbers on the way.
    /// </summary>
    private static JsonNode? RewriteNode(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                return IsWholeButNotInteger(value) && TryWhole(value, out long whole)
                    ? JsonValue.Create(whole)
                    : JsonNode.Parse(value.GetRawText());
            case JsonValueKind.Array:
                JsonArray array = [];
                foreach (JsonElement item in value.EnumerateArray())
                {
                    array.Add(RewriteNode(item));
                }

                return array;
            case JsonValueKind.Object:
                JsonObject node = [];
                foreach (JsonProperty property in value.EnumerateObject())
                {
                    node[property.Name] = RewriteNode(property.Value);
                }

                return node;
            default:
                return JsonNode.Parse(value.GetRawText());
        }
    }
}
