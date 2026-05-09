using Microsoft.Extensions.Options;
using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Provides operation mode guard behavior.
/// </summary>
public sealed class OperationModeGuard
{
    /// <summary>
    /// Stores the apply.
    /// </summary>
    public const string Apply = "apply";

    /// <summary>
    /// Stores the plan.
    /// </summary>
    public const string Plan = "plan";

    /// <summary>
    /// Stores the read only.
    /// </summary>
    public const string ReadOnly = "read-only";

    /// <summary>
    /// Stores the options.
    /// </summary>
    private readonly IOptions<MtgMcpOptions> options;

    /// <summary>
    /// Handles operation mode guard.
    /// </summary>
    public OperationModeGuard(IOptions<MtgMcpOptions> options)
    {
        this.options = options;
    }

    /// <summary>
    /// Handles effective mode.
    /// </summary>
    public string EffectiveMode => Normalize(options.Value.OperationMode);

    /// <summary>
    /// Ensures the can mutate.
    /// </summary>
    public void EnsureCanMutate(string toolName)
    {
        string mode = EffectiveMode;
        if (mode == Apply)
        {
            return;
        }

        if (mode == Plan)
        {
            throw new InvalidOperationException(
                $"mtg-mcp is running in plan mode. Tool '{toolName}' would modify deck state. "
                    + "Ask the user to switch MTGMCP__OPERATION_MODE=apply before applying changes."
            );
        }

        throw new InvalidOperationException(
            $"mtg-mcp is running in read-only mode. Tool '{toolName}' would modify deck state. "
                + "Ask the user to switch MTGMCP__OPERATION_MODE=apply before applying changes."
        );
    }

    /// <summary>
    /// Gets the status.
    /// </summary>
    public object GetStatus()
    {
        return new
        {
            RawMode = options.Value.OperationMode,
            EffectiveMode,
            IsMutationAllowed = EffectiveMode == Apply,
        };
    }

    /// <summary>
    /// Normalizes the mode.
    /// </summary>
    private static string Normalize(string? mode)
    {
        string value = mode?.Trim().ToLowerInvariant() ?? "";
        return value switch
        {
            "" => Apply,
            "act" or "apply" or "write" or "writeable" or "writable" => Apply,
            "plan" or "planning" or "dry-run" or "dryrun" => Plan,
            "ask" or "read" or "readonly" or "read-only" or "read_only" => ReadOnly,
            _ => throw new InvalidOperationException(
                $"Unsupported MTGMCP operation mode '{mode}'. Use apply, plan, read-only, ask, or act."
            ),
        };
    }
}
