using System.ComponentModel;
using ModelContextProtocol.Server;

namespace MtgMcp.App;

/// <summary>
/// Exposes server identity and runtime diagnostics through MCP tools.
/// </summary>
[McpServerToolType]
public sealed class ServerTools
{
    /// <summary>
    /// Stores the service that builds server identity payloads.
    /// </summary>
    private readonly ServerInfoService serverInfo;

    /// <summary>
    /// Creates server diagnostic tools.
    /// </summary>
    public ServerTools(ServerInfoService serverInfo)
    {
        this.serverInfo = serverInfo;
    }

    /// <summary>
    /// Gets version, git, operation mode, and runtime identity for this MCP server.
    /// </summary>
    [McpServerTool(Name = "server_get_info", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Get mtg-mcp version, git commit, git branch, operation mode, data directory, and runtime details for the running server.")]
    public ServerInfo GetServerInfo()
    {
        return serverInfo.GetInfo();
    }
}
