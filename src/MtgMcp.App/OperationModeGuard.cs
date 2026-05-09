using Microsoft.Extensions.Options;
using MtgMcp.Core;

namespace MtgMcp.App;

public sealed class OperationModeGuard
{
    public const string Apply = "apply";
    public const string Plan = "plan";
    public const string ReadOnly = "read-only";

    private readonly IOptions<MtgMcpOptions> options;

    public OperationModeGuard(IOptions<MtgMcpOptions> options)
    {
        this.options = options;
    }

    public string EffectiveMode => Normalize(options.Value.OperationMode);

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
                $"mtg-mcp is running in plan mode. Tool '{toolName}' would modify deck state. Ask the user to switch MTGMCP__OPERATION_MODE=apply before applying changes.");
        }

        throw new InvalidOperationException(
            $"mtg-mcp is running in read-only mode. Tool '{toolName}' would modify deck state. Ask the user to switch MTGMCP__OPERATION_MODE=apply before applying changes.");
    }

    public object GetStatus()
    {
        return new
        {
            RawMode = options.Value.OperationMode,
            EffectiveMode,
            IsMutationAllowed = EffectiveMode == Apply
        };
    }

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
                $"Unsupported MTGMCP operation mode '{mode}'. Use apply, plan, read-only, ask, or act.")
        };
    }
}
