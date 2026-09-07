namespace MtgMcp.Spellbook;

/// <summary>
/// Carries the two persisted boundaries that control Commander Spellbook request starts.
/// </summary>
internal sealed record SpellbookPacingState(DateTimeOffset NextStartUtc, DateTimeOffset CooldownUntilUtc);
