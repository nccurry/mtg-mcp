namespace MtgMcp.Playgroup.Tests;

/// <summary>
/// Creates zero-wall-clock adapter instances while preserving the production 250 ms pacing rule.
/// </summary>
internal static class PlaygroupTestFactory
{
    /// <summary>Creates an injected service and advances a fake clock for each pacing delay.</summary>
    internal static PlaygroupService CreateService(PlaygroupTestHttpHandler handler, string? apiKey = "test-key")
    {
        DateTimeOffset now = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);
        PlaygroupOptions options = PlaygroupOptions.CreateDefault(apiKey);
        PlaygroupRequestPacer pacer = new(
            Guid.NewGuid().ToString("N"),
            options,
            () => now,
            (duration, _) =>
            {
                now += duration;
                return Task.CompletedTask;
            });
        HttpClient client = new(handler)
        {
            BaseAddress = new Uri("https://playgroup.gg/api/public/v1/"),
        };
        return new PlaygroupService(new PlaygroupTransport(client, ownsHttpClient: true, options, pacer));
    }
}
