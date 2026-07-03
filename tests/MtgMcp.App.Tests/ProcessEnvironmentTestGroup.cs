namespace MtgMcp.App.Tests;

/// <summary>
/// Prevents tests that temporarily change process environment variables from running in parallel.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessEnvironmentTestGroup
{
    /// <summary>
    /// Names the serialized process-environment test collection.
    /// </summary>
    public const string Name = "Process environment";
}

/// <summary>
/// Restores one process environment variable after a test changes it.
/// </summary>
internal sealed class EnvironmentVariableScope : IDisposable
{
    /// <summary>
    /// Stores the environment variable name to restore.
    /// </summary>
    private readonly string name;

    /// <summary>
    /// Stores the value observed before the test changed it.
    /// </summary>
    private readonly string? previousValue;

    /// <summary>
    /// Changes one process environment variable for the lifetime of the scope.
    /// </summary>
    internal EnvironmentVariableScope(string name, string? value)
    {
        this.name = name;
        previousValue = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    /// <summary>
    /// Restores the environment variable to its original value.
    /// </summary>
    public void Dispose()
    {
        Environment.SetEnvironmentVariable(name, previousValue);
    }
}
