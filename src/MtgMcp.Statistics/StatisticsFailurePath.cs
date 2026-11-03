namespace MtgMcp.Statistics;

/// <summary>
/// Names the request argument path an invalid statistics value belongs to, from the field the failure message starts
/// with.
/// </summary>
internal static class StatisticsFailurePath
{
    /// <summary>
    /// Reads the path at the start of a message, such as <c>request.drawCount</c>, and adds the <c>request.</c> prefix
    /// to a population path. A message that starts with no path names the whole request.
    /// </summary>
    internal static string FromMessage(string message)
    {
        int end = message.IndexOf(' ', StringComparison.Ordinal);
        string token = (end < 0 ? message : message[..end]).TrimEnd('.', ',', ':');
        if (token.StartsWith("population", StringComparison.Ordinal))
        {
            return "request." + token;
        }

        return token.StartsWith("request", StringComparison.Ordinal) ? token : "request";
    }
}
