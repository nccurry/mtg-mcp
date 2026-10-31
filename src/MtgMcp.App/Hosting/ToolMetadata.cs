namespace MtgMcp.App.Hosting;

/// <summary>
/// Names the tool metadata keys the server publishes.
/// </summary>
internal static class ToolMetadata
{
    /// <summary>
    /// Asks Claude Code to confirm with the user before each call of a destructive remote write. Other clients
    /// ignore it.
    /// </summary>
    internal const string RequiresUserInteractionKey = "anthropic/requiresUserInteraction";
}
