using MtgMcp.Core.Results;

namespace MtgMcp.App.Decks;

/// <summary>
/// Checks the confirmation phrase of a local operation that destroys or replaces data with no local undo. The phrase
/// names the action and the target, so a caller cannot confirm a different target than the one it passes.
/// </summary>
internal static class DeckConfirmation
{
    /// <summary>
    /// Names the reason code of a missing or wrong confirmation phrase.
    /// </summary>
    internal const string ReasonCode = "confirmation-required";

    /// <summary>
    /// Builds the exact phrase for an action on one target, such as <c>delete deck</c> and the deck ID.
    /// </summary>
    internal static string Phrase(string action, Guid target)
    {
        return $"{action} {target:D}";
    }

    /// <summary>
    /// Refuses a confirmation that is not exactly the phrase, and shows the phrase to pass.
    /// </summary>
    internal static OperationInvalidInput? Check(string? confirmation, string phrase)
    {
        return string.Equals(confirmation, phrase, StringComparison.Ordinal)
            ? null
            : new OperationInvalidInput(ReasonCode, $"Pass confirmation exactly as: {phrase}", "confirmation");
    }
}
