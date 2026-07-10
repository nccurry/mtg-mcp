using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;

namespace MtgMcp.Statistics;

/// <summary>
/// Carries one reduced exact rational and its stable fixed-point display.
/// </summary>
public sealed record ExactRationalValue(
    string Numerator,
    string Denominator,
    [property: JsonPropertyName("decimal")] string Display);

/// <summary>
/// Carries one reduced probability and stable decimal and percentage displays.
/// </summary>
public sealed record ExactProbability(
    string Numerator,
    string Denominator,
    [property: JsonPropertyName("decimal")] string Display,
    string Percent);

/// <summary>
/// Implements normalized rational arithmetic without floating-point conversion.
/// </summary>
internal readonly record struct ExactFraction
{
    /// <summary>
    /// Creates and reduces one rational with a positive denominator.
    /// </summary>
    internal ExactFraction(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero)
        {
            throw new DivideByZeroException("An exact rational denominator cannot be zero.");
        }

        if (denominator.Sign < 0)
        {
            numerator = BigInteger.Negate(numerator);
            denominator = BigInteger.Negate(denominator);
        }

        BigInteger divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        Numerator = numerator / divisor;
        Denominator = denominator / divisor;
    }

    /// <summary>
    /// Gets the normalized signed numerator.
    /// </summary>
    internal BigInteger Numerator { get; }

    /// <summary>
    /// Gets the normalized positive denominator.
    /// </summary>
    internal BigInteger Denominator { get; }

    /// <summary>
    /// Gets exact zero.
    /// </summary>
    internal static ExactFraction Zero { get; } = new(BigInteger.Zero, BigInteger.One);

    /// <summary>
    /// Gets exact one.
    /// </summary>
    internal static ExactFraction One { get; } = new(BigInteger.One, BigInteger.One);

    /// <summary>
    /// Creates one integral rational.
    /// </summary>
    internal static ExactFraction FromInteger(BigInteger value)
    {
        return new ExactFraction(value, BigInteger.One);
    }

    /// <summary>
    /// Adds another exact rational.
    /// </summary>
    internal ExactFraction Add(ExactFraction other)
    {
        return new ExactFraction(
            (Numerator * other.Denominator) + (other.Numerator * Denominator),
            Denominator * other.Denominator);
    }

    /// <summary>
    /// Subtracts another exact rational.
    /// </summary>
    internal ExactFraction Subtract(ExactFraction other)
    {
        return new ExactFraction(
            (Numerator * other.Denominator) - (other.Numerator * Denominator),
            Denominator * other.Denominator);
    }

    /// <summary>
    /// Multiplies by another exact rational.
    /// </summary>
    internal ExactFraction Multiply(ExactFraction other)
    {
        return new ExactFraction(Numerator * other.Numerator, Denominator * other.Denominator);
    }

    /// <summary>
    /// Divides by another nonzero exact rational.
    /// </summary>
    internal ExactFraction Divide(ExactFraction other)
    {
        if (other.Numerator.IsZero)
        {
            throw new DivideByZeroException("An exact rational cannot be divided by zero.");
        }

        return new ExactFraction(Numerator * other.Denominator, Denominator * other.Numerator);
    }

    /// <summary>
    /// Raises this rational to one nonnegative integral power.
    /// </summary>
    internal ExactFraction Pow(int exponent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(exponent);
        return new ExactFraction(
            BigInteger.Pow(Numerator, exponent),
            BigInteger.Pow(Denominator, exponent));
    }

    /// <summary>
    /// Compares two exact rationals without rounding.
    /// </summary>
    internal int CompareTo(ExactFraction other)
    {
        return (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);
    }

    /// <summary>
    /// Projects this value into the stable rational display contract.
    /// </summary>
    internal ExactRationalValue ToValue()
    {
        return new ExactRationalValue(
            Numerator.ToString(CultureInfo.InvariantCulture),
            Denominator.ToString(CultureInfo.InvariantCulture),
            ExactNumberFormatter.FormatFixed(Numerator, Denominator));
    }

    /// <summary>
    /// Projects this nonnegative value into the stable probability contract.
    /// </summary>
    internal ExactProbability ToProbability()
    {
        if (Numerator.Sign < 0 || CompareTo(One) > 0)
        {
            throw new InvalidOperationException("A probability must be between zero and one.");
        }

        return new ExactProbability(
            Numerator.ToString(CultureInfo.InvariantCulture),
            Denominator.ToString(CultureInfo.InvariantCulture),
            ExactNumberFormatter.FormatFixed(Numerator, Denominator),
            ExactNumberFormatter.FormatFixed(Numerator * 100, Denominator));
    }
}

/// <summary>
/// Formats exact rationals as invariant fixed-point strings using integer rounding.
/// </summary>
internal static class ExactNumberFormatter
{
    /// <summary>
    /// Defines the public fixed precision.
    /// </summary>
    private const int FractionalDigits = 12;

    /// <summary>
    /// Stores ten raised to the public fixed precision.
    /// </summary>
    private static readonly BigInteger Scale = BigInteger.Pow(10, FractionalDigits);

    /// <summary>
    /// Formats one rational with midpoint-to-even rounding and no scientific notation.
    /// </summary>
    internal static string FormatFixed(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.Sign <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(denominator),
                denominator,
                "The display denominator must be positive.");
        }

        bool negative = numerator.Sign < 0;
        BigInteger scaled = BigInteger.Abs(numerator) * Scale;
        BigInteger quotient = BigInteger.DivRem(scaled, denominator, out BigInteger remainder);
        int midpoint = (remainder * 2).CompareTo(denominator);
        if (midpoint > 0 || (midpoint == 0 && !quotient.IsEven))
        {
            quotient += BigInteger.One;
        }

        BigInteger integerPart = BigInteger.DivRem(quotient, Scale, out BigInteger fractionalPart);
        string sign = negative && !quotient.IsZero ? "-" : string.Empty;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{sign}{integerPart}.{fractionalPart.ToString($"D{FractionalDigits}", CultureInfo.InvariantCulture)}");
    }
}

/// <summary>
/// Computes exact binomial coefficients with symmetric process-local memoization.
/// </summary>
internal sealed class CombinationCache
{
    /// <summary>
    /// Stores previously computed coefficients by normalized `(n, k)` pair.
    /// </summary>
    private readonly Dictionary<(int Population, int Selected), BigInteger> values = [];

    /// <summary>
    /// Returns `n` choose `k`, using zero for an impossible selection.
    /// </summary>
    internal BigInteger Choose(int population, int selected)
    {
        if (population < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(population),
                population,
                "The population cannot be negative.");
        }

        if (selected < 0 || selected > population)
        {
            return BigInteger.Zero;
        }

        int symmetric = Math.Min(selected, population - selected);
        if (symmetric == 0)
        {
            return BigInteger.One;
        }

        (int Population, int Selected) key = (population, symmetric);
        if (values.TryGetValue(key, out BigInteger cached))
        {
            return cached;
        }

        BigInteger result = BigInteger.One;
        for (int index = 1; index <= symmetric; index++)
        {
            result = (result * (population - symmetric + index)) / index;
        }

        values.Add(key, result);
        return result;
    }
}
