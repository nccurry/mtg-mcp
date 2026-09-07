namespace MtgMcp.Spellbook.Tests;

/// <summary>
/// Verifies SQLite-backed pacing stays shared across independently constructed adapter owners.
/// </summary>
public sealed class SpellbookRequestPacerTests
{
    /// <summary>
    /// Verifies two database owners reserve source starts one second apart from the same local root.
    /// </summary>
    [Fact]
    public async Task IndependentDatabaseOwners_ReserveOneSecondApart()
    {
        using TemporarySpellbookDirectory directory = new();
        using SpellbookDatabase firstDatabase = new(directory.Path);
        using SpellbookDatabase secondDatabase = new(directory.Path);
        SpellbookRequestPacer first = new(firstDatabase);
        SpellbookRequestPacer second = new(secondDatabase);
        DateTimeOffset now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

        TimeSpan[] reservations = await Task.WhenAll(
            first.ReserveStartAsync(now, TestContext.Current.CancellationToken),
            second.ReserveStartAsync(now, TestContext.Current.CancellationToken));
        List<TimeSpan> ordered = [.. reservations];
        ordered.Sort();

        Assert.Equal([TimeSpan.Zero, TimeSpan.FromSeconds(1)], ordered);
    }

    /// <summary>
    /// Verifies a source cooldown is capped locally and prevents a later source start without waiting.
    /// </summary>
    [Fact]
    public async Task Cooldown_CapsRetryAfterAndStopsLaterReservation()
    {
        using TemporarySpellbookDirectory directory = new();
        using SpellbookDatabase database = new(directory.Path);
        SpellbookRequestPacer pacer = new(database);
        DateTimeOffset now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

        await pacer.ReserveStartAsync(now, TestContext.Current.CancellationToken);
        await pacer.RecordCooldownAsync(
            now,
            TimeSpan.FromMinutes(2),
            TestContext.Current.CancellationToken);
        SpellbookProviderException blocked = await Assert.ThrowsAsync<SpellbookProviderException>(() =>
            pacer.ReserveStartAsync(now.AddSeconds(59), TestContext.Current.CancellationToken));
        TimeSpan resumed = await pacer.ReserveStartAsync(now.AddSeconds(60), TestContext.Current.CancellationToken);

        Assert.Equal(SpellbookFailureKind.RateLimited, blocked.Kind);
        Assert.Equal(TimeSpan.Zero, resumed);
    }

    /// <summary>
    /// Verifies a cooldown recorded after reservation blocks the mandatory immediate pre-HTTP recheck.
    /// </summary>
    [Fact]
    public async Task CoolingDownAfterReservation_StopsRequestBeforeHttp()
    {
        using TemporarySpellbookDirectory directory = new();
        using SpellbookDatabase database = new(directory.Path);
        SpellbookRequestPacer pacer = new(database);
        DateTimeOffset now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(TimeSpan.Zero, await pacer.ReserveStartAsync(now, TestContext.Current.CancellationToken));
        await pacer.RecordCooldownAsync(now, TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        SpellbookProviderException blocked = await Assert.ThrowsAsync<SpellbookProviderException>(() =>
            pacer.ThrowIfCoolingDownAsync(now, TestContext.Current.CancellationToken));

        Assert.Equal("source-rate-limited", blocked.ReasonCode);
    }
}
