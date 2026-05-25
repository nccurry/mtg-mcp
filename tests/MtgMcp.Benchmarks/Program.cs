using BenchmarkDotNet.Running;

namespace MtgMcp.Benchmarks;

/// <summary>
/// BenchmarkDotNet entry point for mtg-mcp performance checks.
/// </summary>
public static class Program
{
    /// <summary>
    /// Runs benchmarks selected by BenchmarkDotNet command-line arguments.
    /// </summary>
    public static void Main(string[] args)
    {
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
