using Microsoft.Extensions.Options;
using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Enforces the configured safety mode before tools write deck, remote, or planning state.
/// </summary>
public sealed class OperationModeGuard
{
    /// <summary>
    /// Allows all local and remote mutations.
    /// </summary>
    public const string Apply = "apply";

    /// <summary>
    /// Allows local planning writes while blocking deck mutations.
    /// </summary>
    public const string Plan = "plan";

    /// <summary>
    /// Blocks every write-capable tool.
    /// </summary>
    public const string ReadOnly = "read-only";

    /// <summary>
    /// Supplies the raw operation mode from configuration.
    /// </summary>
    private readonly IOptions<MtgMcpOptions> options;

    /// <summary>
    /// Creates a guard backed by the current mtg-mcp options snapshot.
    /// </summary>
    public OperationModeGuard(IOptions<MtgMcpOptions> options)
    {
        this.options = options;
    }

    /// <summary>
    /// Gets the normalized safety mode used by tool wrappers.
    /// </summary>
    public string EffectiveMode => Normalize(options.Value.OperationMode);

    /// <summary>
    /// Throws unless the configured mode permits deck or remote mutations.
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
            throw new OperationModeBlockedException(
                toolName,
                mode,
                Apply,
                $"mtg-mcp is running in plan mode. Tool '{toolName}' would modify deck state. "
                    + "Ask the user to switch MTGMCP__OPERATION_MODE=apply before applying changes."
            );
        }

        throw new OperationModeBlockedException(
            toolName,
            mode,
            Apply,
            $"mtg-mcp is running in read-only mode. Tool '{toolName}' would modify deck state. "
                + "Ask the user to switch MTGMCP__OPERATION_MODE=apply before applying changes."
        );
    }

    /// <summary>
    /// Throws unless the configured mode permits local plan and metadata writes.
    /// </summary>
    public void EnsureCanWritePlanningState(string toolName)
    {
        string mode = EffectiveMode;
        if (mode is Apply or Plan)
        {
            return;
        }

        throw new OperationModeBlockedException(
            toolName,
            mode,
            Plan,
            $"mtg-mcp is running in read-only mode. Tool '{toolName}' would write local planning state. "
                + "Ask the user to switch MTGMCP__OPERATION_MODE=plan or apply before creating plans or refreshing local metadata."
        );
    }

    /// <summary>
    /// Returns a serializable snapshot of the current operation mode.
    /// </summary>
    public object GetStatus()
    {
        return new
        {
            RawMode = options.Value.OperationMode,
            EffectiveMode,
            IsMutationAllowed = EffectiveMode == Apply,
            IsPlanningStateWriteAllowed = EffectiveMode is Apply or Plan,
        };
    }

    /// <summary>
    /// Maps accepted client aliases onto one of the supported safety modes.
    /// </summary>
    public static string Normalize(string? mode)
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

/// <summary>
/// Signals that a tool was blocked by the configured operation mode.
/// </summary>
public sealed class OperationModeBlockedException : InvalidOperationException
{
    /// <summary>
    /// Creates an exception with structured operation-mode details.
    /// </summary>
    public OperationModeBlockedException(
        string toolName,
        string currentMode,
        string requiredMode,
        string message)
        : base(message)
    {
        ToolName = toolName;
        CurrentMode = currentMode;
        RequiredMode = requiredMode;
    }

    /// <summary>
    /// Gets the MCP tool name that was blocked.
    /// </summary>
    public string ToolName { get; }

    /// <summary>
    /// Gets the normalized mode active when the tool was blocked.
    /// </summary>
    public string CurrentMode { get; }

    /// <summary>
    /// Names the least-permissive operation mode that can run the blocked tool.
    /// </summary>
    public string RequiredMode { get; }
}
