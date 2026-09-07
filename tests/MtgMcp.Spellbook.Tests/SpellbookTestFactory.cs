namespace MtgMcp.Spellbook.Tests;

/// <summary>
/// Builds deterministic adapter instances over a temporary cache and fake source transport.
/// </summary>
internal static class SpellbookTestFactory
{
    /// <summary>
    /// Creates one service that owns its injected HTTP client and temporary cache database.
    /// </summary>
    internal static SpellbookService CreateService(
        SpellbookTestHttpHandler handler,
        TemporarySpellbookDirectory directory,
        MutableTimeProvider timeProvider,
        TimeSpan? cacheTtl = null,
        TimeSpan? requestTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        SpellbookOptions options = SpellbookOptions.CreateDefault(directory.Path, cacheTtl);
        SpellbookDatabase database = new(directory.Path);
        HttpClient client = new(handler);
        SpellbookTransport transport = new(client, ownsHttpClient: true, timeProvider, requestTimeout);
        return new SpellbookService(options, database, transport, timeProvider);
    }

    /// <summary>
    /// Creates one direct transport for focused HTTP behavior tests.
    /// </summary>
    internal static SpellbookTransport CreateTransport(
        SpellbookTestHttpHandler handler,
        MutableTimeProvider timeProvider,
        TimeSpan? requestTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(timeProvider);
        return new SpellbookTransport(
            new HttpClient(handler),
            ownsHttpClient: true,
            timeProvider,
            requestTimeout);
    }
}
