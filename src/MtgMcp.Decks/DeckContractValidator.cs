using MtgMcp.Core.Decks;

namespace MtgMcp.Decks;

/// <summary>
/// Normalizes extensible local vocabulary and rejects malformed caller-owned deck state.
/// </summary>
internal static class DeckContractValidator
{
    /// <summary>
    /// Validates and normalizes one new entry while preserving all explicit identities and the categories the draft
    /// asks for.
    /// </summary>
    internal static DeckNewEntry Normalize(DeckEntryDraft value, Func<Guid> createId)
    {
        ArgumentNullException.ThrowIfNull(value);
        DeckEntry entry = NormalizeEntry(value, createId);
        if (DeckEntryDraftRules.CategoryProblem(value) is string problem)
        {
            throw new DeckInputException(problem);
        }

        return new DeckNewEntry(
            entry,
            [.. (value.CategoryIds ?? []).Distinct()],
            value.PrimaryCategoryId,
            value.ZoneCategories);
    }

    /// <summary>
    /// Validates and normalizes the entry fields of a draft, leaving its categories aside.
    /// </summary>
    private static DeckEntry NormalizeEntry(DeckEntryDraft value, Func<Guid> createId)
    {
        if (DeckEntryDraftRules.FirstEntryProblem(value) is string problem)
        {
            throw new DeckInputException(problem);
        }

        return new DeckEntry(
            NormalizeId(value.EntryId ?? createId(), "entry"),
            value.Quantity,
            Required(value.CardName, "Card name"),
            NormalizeOptionalId(value.OracleId, "Oracle"),
            NormalizeOptionalId(value.PrintingId, "printing"),
            Optional(value.SetCode)?.ToLowerInvariant(),
            Optional(value.CollectorNumber),
            Required(value.Language, "Language").ToLowerInvariant(),
            Required(value.Finish, "Finish").ToLowerInvariant(),
            Required(value.Zone, "Zone").ToLowerInvariant(),
            value.SortOrder);
    }

    /// <summary>
    /// Validates and normalizes a complete replacement entry.
    /// </summary>
    internal static DeckEntry Normalize(DeckEntry value)
    {
        ArgumentNullException.ThrowIfNull(value);
        DeckEntry normalized = NormalizeEntry(
            new DeckEntryDraft(
                value.Quantity,
                value.CardName,
                value.OracleId,
                value.PrintingId,
                value.SetCode,
                value.CollectorNumber,
                value.Language,
                value.Finish,
                value.Zone,
                value.SortOrder,
                value.EntryId),
            static () => throw new InvalidOperationException("An existing entry requires an ID."));
        return normalized;
    }

    /// <summary>
    /// Validates and normalizes one new category.
    /// </summary>
    internal static DeckCategory Normalize(DeckCategoryDraft value, Func<Guid> createId)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new DeckCategory(
            NormalizeId(value.CategoryId ?? createId(), "category"),
            Required(value.Name, "Category name"),
            Optional(value.Color),
            value.SortOrder);
    }

    /// <summary>
    /// Validates and normalizes a complete replacement category.
    /// </summary>
    internal static DeckCategory Normalize(DeckCategory value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Normalize(
            new DeckCategoryDraft(value.Name, value.Color, value.SortOrder, value.CategoryId),
            static () => throw new InvalidOperationException("An existing category requires an ID."));
    }

    /// <summary>
    /// Validates and normalizes one provider-neutral synchronization binding.
    /// </summary>
    internal static DeckProviderBinding Normalize(DeckProviderBinding value, Func<Guid> createId)
    {
        ArgumentNullException.ThrowIfNull(value);
        Guid bindingId = value.BindingId == Guid.Empty ? createId() : value.BindingId;
        return new DeckProviderBinding(
            NormalizeId(bindingId, "binding"),
            Required(value.Provider, "Provider").ToLowerInvariant(),
            Required(value.RemoteId, "Remote ID"),
            Optional(value.RemoteUri),
            Optional(value.RemoteVersion),
            Optional(value.BaselineFingerprint),
            value.LastPulledAtUtc?.ToUniversalTime(),
            value.LastPushedAtUtc?.ToUniversalTime());
    }

    /// <summary>
    /// Returns nonblank trimmed text for a required local field.
    /// </summary>
    internal static string Required(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DeckInputException($"{fieldName} is required.");
        }

        return value.Trim();
    }

    /// <summary>
    /// Returns null or nonblank trimmed optional text.
    /// </summary>
    internal static string? Optional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// Rejects an empty stable identifier.
    /// </summary>
    internal static Guid NormalizeId(Guid value, string fieldName)
    {
        if (value == Guid.Empty)
        {
            throw new DeckInputException($"The {fieldName} ID is invalid.");
        }

        return value;
    }

    /// <summary>
    /// Preserves an absent identifier while rejecting an explicitly empty one.
    /// </summary>
    private static Guid? NormalizeOptionalId(Guid? value, string fieldName)
    {
        return value is null ? null : NormalizeId(value.Value, fieldName);
    }
}

/// <summary>
/// Holds a normalized new entry together with the categories its draft asked for. The category IDs are distinct, and
/// the zone categories mode is as the caller gave it, or null.
/// </summary>
internal sealed record DeckNewEntry(
    DeckEntry Entry,
    IReadOnlyList<Guid> CategoryIds,
    Guid? PrimaryCategoryId,
    string? ZoneCategories)
{
    /// <summary>
    /// Tells whether the draft asked for any category work.
    /// </summary>
    internal bool HasCategories => CategoryIds.Count > 0 || PrimaryCategoryId is not null ||
        DeckZoneCategories.IsMatch(ZoneCategories);
}

/// <summary>
/// Signals caller-owned deck input that cannot enter the local store.
/// </summary>
internal sealed class DeckInputException : Exception
{
    /// <summary>
    /// Creates a bounded validation failure safe for conversion to structured invalid input.
    /// </summary>
    internal DeckInputException(
        string message,
        string reasonCode = DeckReasonCodes.InvalidDeckInput,
        string? path = null)
        : base(message)
    {
        ReasonCode = reasonCode;
        Path = path;
    }

    /// <summary>
    /// Builds the refusal of a change or draft the deck cannot take. The whole request is rolled back or never
    /// started, so the message says nothing was written.
    /// </summary>
    internal static DeckInputException Refusal(string message)
    {
        return new DeckInputException(message + " Nothing was written.", DeckReasonCodes.InvalidDeckChange);
    }

    /// <summary>
    /// Returns the same failure with an argument path, for a caller that knows which value was rejected.
    /// </summary>
    internal DeckInputException At(string path)
    {
        return new DeckInputException(Message, ReasonCode, path);
    }

    /// <summary>
    /// Gets the lowercase reason code projected to the public result union.
    /// </summary>
    internal string ReasonCode { get; }

    /// <summary>
    /// Gets the argument path of the rejected value, or null when the failing operation names it.
    /// </summary>
    internal string? Path { get; }
}

/// <summary>
/// Signals a stable child entity that is absent from the selected local deck.
/// </summary>
internal sealed class DeckEntityNotFoundException : Exception
{
    /// <summary>
    /// Creates a path-free missing-entity failure with a stable reason code.
    /// </summary>
    internal DeckEntityNotFoundException(string reasonCode, string message)
        : base(message)
    {
        ReasonCode = reasonCode;
    }

    /// <summary>
    /// Returns the same failure with an argument path, for a caller that knows which value was rejected.
    /// </summary>
    internal DeckInputException At(string path)
    {
        return new DeckInputException(Message, ReasonCode, path);
    }

    /// <summary>
    /// Gets the lowercase reason code projected to the public result union.
    /// </summary>
    internal string ReasonCode { get; }
}
