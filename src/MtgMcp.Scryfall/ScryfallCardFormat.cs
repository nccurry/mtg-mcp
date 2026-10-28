using MtgMcp.Core.Results;

namespace MtgMcp.Scryfall;

/// <summary>
/// Describes how a card result is shaped: the response format and, for concise, the chosen fields.
/// </summary>
internal sealed record ScryfallCardFormat
{
    /// <summary>
    /// Creates a format after its values were checked by <see cref="Parse"/>.
    /// </summary>
    private ScryfallCardFormat(ScryfallResponseFormat format, IReadOnlySet<string>? selectedFields)
    {
        Format = format;
        SelectedFields = selectedFields;
        NeedsTags = Needs(ScryfallInstalledDataNeed.Tags);
        NeedsPrice = Needs(ScryfallInstalledDataNeed.CheapestPrice);
    }

    /// <summary>
    /// Gets the response format.
    /// </summary>
    internal ScryfallResponseFormat Format { get; }

    /// <summary>
    /// Gets the concise fields to write, or null for the other formats.
    /// </summary>
    internal IReadOnlySet<string>? SelectedFields { get; }

    /// <summary>
    /// Gets the name callers send and receive for the format.
    /// </summary>
    internal string Name => ScryfallResponseFormatNames.ToName(Format);

    /// <summary>
    /// Gets a value showing whether results use the concise projection.
    /// </summary>
    internal bool IsConcise => Format == ScryfallResponseFormat.Concise;

    /// <summary>
    /// Gets a value showing whether results carry the raw provider objects.
    /// </summary>
    internal bool IncludeRaw => Format == ScryfallResponseFormat.Full;

    /// <summary>
    /// Gets a value showing whether tag slugs are selected.
    /// </summary>
    internal bool NeedsTags { get; }

    /// <summary>
    /// Gets a value showing whether the cheapest USD price is selected.
    /// </summary>
    internal bool NeedsPrice { get; }

    /// <summary>
    /// Gets a value showing whether any selected field needs installed card data.
    /// </summary>
    internal bool NeedsInstalledData => NeedsTags || NeedsPrice;

    /// <summary>
    /// Checks the response format name and field list and builds the format.
    /// </summary>
    internal static OperationResult<ScryfallCardFormat> Parse(string? responseFormat, IReadOnlyList<string>? fields)
    {
        if (!ScryfallResponseFormatNames.TryParse(responseFormat, out ScryfallResponseFormat format))
        {
            return new OperationInvalidInput(
                "invalid-response-format",
                "responseFormat must be concise, standard, or full.",
                "responseFormat");
        }

        if (fields is not null && format != ScryfallResponseFormat.Concise)
        {
            return new OperationInvalidInput(
                "fields-require-concise",
                "fields can only be used with responseFormat concise.",
                "fields");
        }

        if (format != ScryfallResponseFormat.Concise)
        {
            return new OperationSuccess<ScryfallCardFormat>(new ScryfallCardFormat(format, null));
        }

        OperationInvalidInput? unknown = FindUnknownField(fields);
        if (unknown is not null)
        {
            return unknown;
        }

        HashSet<string> selected = new(StringComparer.Ordinal);
        foreach (string field in fields is null ? ScryfallConciseFields.Default : ScryfallConciseFields.Always)
        {
            selected.Add(field);
        }

        foreach (string field in fields ?? [])
        {
            selected.Add(field);
        }

        return new OperationSuccess<ScryfallCardFormat>(new ScryfallCardFormat(format, selected));
    }

    /// <summary>
    /// Finds the first field name that is not offered, and names every offered field in the message.
    /// </summary>
    private static OperationInvalidInput? FindUnknownField(IReadOnlyList<string>? fields)
    {
        if (fields is null)
        {
            return null;
        }

        for (int index = 0; index < fields.Count; index++)
        {
            if (!ScryfallConciseFields.All.Contains(fields[index], StringComparer.Ordinal))
            {
                return new OperationInvalidInput(
                    "invalid-fields",
                    $"Unknown field '{fields[index]}'. Allowed names: {string.Join(", ", ScryfallConciseFields.All)}.",
                    $"fields[{index}]");
            }
        }

        return null;
    }

    /// <summary>
    /// Reports whether any selected concise field needs the given installed data.
    /// </summary>
    private bool Needs(ScryfallInstalledDataNeed need)
    {
        return SelectedFields is not null &&
            ScryfallConciseFieldTable.Definitions.Any(
                field => ScryfallConciseFieldTable.NeedOf(field) == need &&
                    SelectedFields.Contains(ScryfallConciseFieldTable.NameOf(field)));
    }
}
