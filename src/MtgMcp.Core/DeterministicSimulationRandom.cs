namespace MtgMcp.Core;

/// <summary>
/// Provides a small stable random source for deterministic simulation replay.
/// </summary>
internal sealed class DeterministicSimulationRandom
{
    /// <summary>
    /// Identifies the deterministic random algorithm used in simulation metadata.
    /// </summary>
    public const string Kind = "mtgmcp-splitmix64-v1";

    /// <summary>
    /// Advances through the SplitMix64 sequence.
    /// </summary>
    private ulong state;

    /// <summary>
    /// Creates a deterministic random source from a caller-visible seed.
    /// </summary>
    public DeterministicSimulationRandom(int seed)
    {
        unchecked
        {
            uint unsignedSeed = (uint)seed;
            state = ((ulong)unsignedSeed << 32)
                ^ unsignedSeed
                ^ 0x9E3779B97F4A7C15UL;
        }
    }

    /// <summary>
    /// Returns a deterministic integer in the range [0, exclusiveUpperBound).
    /// </summary>
    public int Next(int exclusiveUpperBound)
    {
        if (exclusiveUpperBound <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exclusiveUpperBound), "Upper bound must be positive.");
        }

        ulong bound = (ulong)exclusiveUpperBound;
        ulong threshold = ulong.MaxValue - (ulong.MaxValue % bound);
        ulong value;
        do
        {
            value = NextUInt64();
        }
        while (value >= threshold);

        return (int)(value % bound);
    }

    /// <summary>
    /// Generates the next deterministic 64-bit value.
    /// </summary>
    private ulong NextUInt64()
    {
        unchecked
        {
            ulong value = state += 0x9E3779B97F4A7C15UL;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }
}
