using System.Globalization;
using System.Text;

namespace MtgMcp.Spellbook;

/// <summary>
/// Identifies one exact source request without retaining its raw input outside the current call.
/// </summary>
internal sealed record SpellbookRequestIdentity(string Operation, string RequestHash, string ContractChecksum)
{
    /// <summary>
    /// Builds a hash from a fixed-order representation of the full source request.
    /// </summary>
    internal static SpellbookRequestIdentity Create(
        string operation,
        HttpMethod method,
        string path,
        SpellbookRequestDetails request,
        SpellbookDeckRequest? deck)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(request);

        StringBuilder text = new();
        Append(text, "operation", operation);
        Append(text, "method", method.Method);
        Append(text, "path", path);
        Append(text, "sourceQuery", request.SourceQuery);
        Append(text, "variantId", request.VariantId);
        Append(text, "limit", request.Page?.Limit.ToString(CultureInfo.InvariantCulture));
        Append(text, "offset", request.Page?.Offset.ToString(CultureInfo.InvariantCulture));
        Append(text, "groupByCombo", request.Page?.GroupByCombo is { } groupByCombo
            ? groupByCombo ? "true" : "false"
            : null);
        AppendDeck(text, "commanders", deck?.Commanders);
        AppendDeck(text, "main", deck?.Main);
        Append(text, "contractChecksum", SpellbookContract.OpenApiChecksum);
        return new SpellbookRequestIdentity(
            operation,
            SpellbookHash.Compute(text.ToString()),
            SpellbookContract.OpenApiChecksum);
    }

    /// <summary>
    /// Appends one length-prefixed field so arbitrary source text remains unambiguous in memory.
    /// </summary>
    private static void Append(StringBuilder builder, string name, string? value)
    {
        builder.Append(name);
        builder.Append('=');
        if (value is null)
        {
            builder.Append("-1:");
        }
        else
        {
            builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(value);
        }

        builder.Append('\n');
    }

    /// <summary>
    /// Appends the already normalized source deck rows in their fixed zone order.
    /// </summary>
    private static void AppendDeck(
        StringBuilder builder,
        string name,
        IReadOnlyList<SpellbookDeckEntry>? entries)
    {
        Append(builder, $"{name}.count", entries?.Count.ToString(CultureInfo.InvariantCulture));
        if (entries is null)
        {
            return;
        }

        foreach (SpellbookDeckEntry entry in entries)
        {
            Append(builder, $"{name}.card", entry.Card);
            Append(builder, $"{name}.quantity", entry.Quantity.ToString(CultureInfo.InvariantCulture));
        }
    }
}
