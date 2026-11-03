using System.Globalization;

namespace MtgMcp.Core.Results;

/// <summary>
/// Builds and checks the exact phrase a caller sends to confirm an operation that destroys or replaces data. The
/// phrase names the action and the target, so a confirmation cannot be reused for another one.
/// </summary>
public static class Confirmation
{
    /// <summary>
    /// Limits how many characters of a caller-supplied id a phrase echoes.
    /// </summary>
    public const int MaximumEchoedIdLength = 64;

    /// <summary>
    /// Builds the phrase for an action on one target, such as <c>delete</c>, <c>deck</c>, and the deck ID. An id past
    /// the echo limit is cut and marked with three dots.
    /// </summary>
    public static string Phrase(string action, string targetKind, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetKind);
        ArgumentNullException.ThrowIfNull(id);
        return $"{action} {targetKind} {Shorten(id)}";
    }

    /// <summary>
    /// Builds the phrase for an action on a target named by a GUID.
    /// </summary>
    public static string Phrase(string action, string targetKind, Guid id)
    {
        return Phrase(action, targetKind, id.ToString("D", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Returns the refusal that shows the exact phrase when the caller did not send it, and null when it did. The
    /// comparison is ordinal.
    /// </summary>
    public static OperationInvalidInput? Check(string? confirmation, string phrase)
    {
        return string.Equals(confirmation, phrase, StringComparison.Ordinal)
            ? null
            : new OperationInvalidInput(
                SharedReasonCodes.ConfirmationRequired,
                $"confirmation must be exactly \"{phrase}\".",
                "confirmation");
    }

    /// <summary>
    /// Cuts an echoed id to the length limit without splitting a surrogate pair.
    /// </summary>
    private static string Shorten(string id)
    {
        if (id.Length <= MaximumEchoedIdLength)
        {
            return id;
        }

        int length = char.IsHighSurrogate(id[MaximumEchoedIdLength - 1])
            ? MaximumEchoedIdLength - 1
            : MaximumEchoedIdLength;
        return id[..length] + "...";
    }
}
