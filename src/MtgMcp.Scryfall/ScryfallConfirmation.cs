using MtgMcp.Core.Results;

namespace MtgMcp.Scryfall;

/// <summary>
/// Checks the exact phrase a caller must send to confirm an operation that destroys or replaces installed data, so
/// the confirmation names the action and the target and cannot be reused for another one.
/// </summary>
internal static class ScryfallConfirmation
{
    /// <summary>
    /// Builds the phrase for an action on one target, such as <c>delete card-data</c> and the generation id.
    /// </summary>
    internal static string Phrase(string action, Guid target)
    {
        return $"{action} {target:D}";
    }

    /// <summary>
    /// Returns a refusal that shows the exact phrase, or null when the caller sent it.
    /// </summary>
    internal static OperationInvalidInput? Require(string? confirmation, string phrase)
    {
        return string.Equals(confirmation, phrase, StringComparison.Ordinal)
            ? null
            : new OperationInvalidInput(
                SharedReasonCodes.ConfirmationRequired,
                $"confirmation must be exactly \"{phrase}\".",
                "confirmation");
    }
}
