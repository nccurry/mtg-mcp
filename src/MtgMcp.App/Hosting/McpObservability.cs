using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MtgMcp.App;

/// <summary>
/// Adds structured diagnostics around MCP request handling.
/// </summary>
public static class McpObservability
{
    /// <summary>
    /// Names the OpenTelemetry activity source emitted by the MCP host boundary.
    /// </summary>
    public const string ActivitySourceName = "MtgMcp.McpServer";

    /// <summary>
    /// Names the OpenTelemetry meter emitted by the MCP host boundary.
    /// </summary>
    public const string MeterName = "MtgMcp.McpServer";

    /// <summary>
    /// Records MCP tool call spans for host-level diagnostics.
    /// </summary>
    private static readonly ActivitySource ToolActivitySource = new(ActivitySourceName);

    /// <summary>
    /// Records MCP tool call counters and latency histograms.
    /// </summary>
    private static readonly Meter ToolMeter = new(MeterName);

    /// <summary>
    /// Counts completed MCP tool calls by tool and outcome.
    /// </summary>
    private static readonly Counter<long> ToolCallCount = ToolMeter.CreateCounter<long>(
        "mtg_mcp.tool.call.count");

    /// <summary>
    /// Measures completed MCP tool call latency in milliseconds.
    /// </summary>
    private static readonly Histogram<double> ToolCallDuration = ToolMeter.CreateHistogram<double>(
        "mtg_mcp.tool.call.duration",
        unit: "ms");

    /// <summary>
    /// Emits client-requested logging threshold changes.
    /// </summary>
    private static readonly Action<ILogger, string, Exception?> LoggingLevelUpdated =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(1, nameof(LoggingLevelUpdated)),
            "MCP logging level updated to {McpLoggingLevel}");

    /// <summary>
    /// Emits successful tool-call completion logs.
    /// </summary>
    private static readonly Action<ILogger, string, string, double, string, string, Exception?> ToolCallSucceeded =
        LoggerMessage.Define<string, string, double, string, string>(
            LogLevel.Information,
            new EventId(2, nameof(ToolCallSucceeded)),
            "MCP tool call completed: {ToolName} {Status} in {ElapsedMilliseconds:F1} ms detailLevel={DetailLevel} errorType={ErrorType}");

    /// <summary>
    /// Emits failed tool-call completion logs.
    /// </summary>
    private static readonly Action<ILogger, string, string, double, string, string, Exception?> ToolCallFailed =
        LoggerMessage.Define<string, string, double, string, string>(
            LogLevel.Error,
            new EventId(3, nameof(ToolCallFailed)),
            "MCP tool call completed: {ToolName} {Status} in {ElapsedMilliseconds:F1} ms detailLevel={DetailLevel} errorType={ErrorType}");

    /// <summary>
    /// Creates a call-tool filter that emits redacted logs, activities, and metrics.
    /// </summary>
    public static McpRequestFilter<CallToolRequestParams, CallToolResult> CreateCallToolFilter()
    {
        return next => async (request, cancellationToken) =>
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            string toolName = request.Params?.Name ?? "(unknown)";
            string? detailLevel = ReadStringArgument(request.Params, "detailLevel");

            using Activity? activity = ToolActivitySource.StartActivity("mcp.tool.call");
            activity?.SetTag("mcp.tool.name", toolName);
            if (!string.IsNullOrWhiteSpace(detailLevel))
            {
                activity?.SetTag("mcp.tool.detail_level", detailLevel);
            }

            IServiceProvider? services = ResolveServices(request);
            ILogger? logger = ResolveLogger(services);
            McpRuntimeLoggingLevel? loggingLevel = services?.GetService<McpRuntimeLoggingLevel>();

            try
            {
                CallToolResult result = await next(request, cancellationToken).ConfigureAwait(false);
                string status = result.IsError == true ? "error" : "success";
                activity?.SetTag("mcp.tool.status", status);
                RecordToolCall(toolName, detailLevel, status, null, stopwatch.Elapsed.TotalMilliseconds);
                LogCompletion(logger, loggingLevel, toolName, detailLevel, status, null, stopwatch.Elapsed.TotalMilliseconds);
                return result;
            }
            catch (Exception exception)
            {
                string errorType = exception.GetType().Name;
                activity?.SetTag("mcp.tool.status", "exception");
                activity?.SetTag("error.type", errorType);
                RecordToolCall(toolName, detailLevel, "exception", errorType, stopwatch.Elapsed.TotalMilliseconds);
                LogCompletion(logger, loggingLevel, toolName, detailLevel, "exception", errorType, stopwatch.Elapsed.TotalMilliseconds);
                throw;
            }
        };
    }

    /// <summary>
    /// Creates a handler for MCP logging/setLevel requests.
    /// </summary>
    public static McpRequestHandler<SetLevelRequestParams, EmptyResult> CreateSetLoggingLevelHandler()
    {
        return (request, cancellationToken) =>
        {
            IServiceProvider services = request.Services
                ?? request.Server.Services
                ?? throw new InvalidOperationException("MCP request did not include a service provider.");
            McpRuntimeLoggingLevel loggingLevel = services.GetRequiredService<McpRuntimeLoggingLevel>();
            loggingLevel.SetMinimumLevel(ToLogLevel(request.Params?.Level));

            ILogger? logger = ResolveLogger(services);
            if (logger is not null && loggingLevel.IsEnabled(LogLevel.Information))
            {
                LoggingLevelUpdated(logger, loggingLevel.MinimumLevel.ToString(), null);
            }

            return ValueTask.FromResult(new EmptyResult());
        };
    }

    /// <summary>
    /// Converts MCP protocol logging levels to Microsoft.Extensions.Logging levels.
    /// </summary>
    private static LogLevel ToLogLevel(LoggingLevel? level)
    {
        return level switch
        {
            LoggingLevel.Debug => LogLevel.Debug,
            LoggingLevel.Info or LoggingLevel.Notice => LogLevel.Information,
            LoggingLevel.Warning => LogLevel.Warning,
            LoggingLevel.Error => LogLevel.Error,
            LoggingLevel.Critical or LoggingLevel.Alert or LoggingLevel.Emergency => LogLevel.Critical,
            _ => LogLevel.Information,
        };
    }

    /// <summary>
    /// Reads a non-secret string argument suitable for diagnostic dimensions.
    /// </summary>
    private static string? ReadStringArgument(CallToolRequestParams? parameters, string name)
    {
        if (parameters?.Arguments is null || !parameters.Arguments.TryGetValue(name, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    /// <summary>
    /// Resolves the per-request service provider supplied by the MCP SDK.
    /// </summary>
    private static IServiceProvider? ResolveServices(RequestContext<CallToolRequestParams> request)
    {
        return request.Services ?? request.Server.Services;
    }

    /// <summary>
    /// Resolves a named logger without forcing every tool wrapper to take a logger dependency.
    /// </summary>
    private static ILogger? ResolveLogger(IServiceProvider? services)
    {
        return services
            ?.GetService<ILoggerFactory>()
            ?.CreateLogger("MtgMcp.App.McpObservability");
    }

    /// <summary>
    /// Records metric measurements for a completed MCP tool call.
    /// </summary>
    private static void RecordToolCall(
        string toolName,
        string? detailLevel,
        string status,
        string? errorType,
        double elapsedMilliseconds)
    {
        TagList tags = CreateTags(toolName, detailLevel, status, errorType);
        ToolCallCount.Add(1, tags);
        ToolCallDuration.Record(elapsedMilliseconds, tags);
    }

    /// <summary>
    /// Emits one structured completion log per tool call when enabled by the runtime level.
    /// </summary>
    private static void LogCompletion(
        ILogger? logger,
        McpRuntimeLoggingLevel? loggingLevel,
        string toolName,
        string? detailLevel,
        string status,
        string? errorType,
        double elapsedMilliseconds)
    {
        if (logger is null)
        {
            return;
        }

        LogLevel messageLevel = status == "success" ? LogLevel.Information : LogLevel.Error;
        if (loggingLevel is not null && !loggingLevel.IsEnabled(messageLevel))
        {
            return;
        }

        if (messageLevel == LogLevel.Information)
        {
            ToolCallSucceeded(logger, toolName, status, elapsedMilliseconds, detailLevel ?? "", errorType ?? "", null);
            return;
        }

        ToolCallFailed(logger, toolName, status, elapsedMilliseconds, detailLevel ?? "", errorType ?? "", null);
    }

    /// <summary>
    /// Builds low-cardinality metric dimensions for host-level tool telemetry.
    /// </summary>
    private static TagList CreateTags(
        string toolName,
        string? detailLevel,
        string status,
        string? errorType)
    {
        TagList tags = new();
        tags.Add("tool.name", toolName);
        tags.Add("status", status);
        if (!string.IsNullOrWhiteSpace(detailLevel))
        {
            tags.Add("detail.level", detailLevel);
        }

        if (!string.IsNullOrWhiteSpace(errorType))
        {
            tags.Add("error.type", errorType);
        }

        return tags;
    }
}

/// <summary>
/// Stores the MCP client-requested minimum log level for host-boundary diagnostics.
/// </summary>
public sealed class McpRuntimeLoggingLevel
{
    /// <summary>
    /// Stores the current minimum Microsoft.Extensions.Logging level.
    /// </summary>
    public LogLevel MinimumLevel { get; private set; } = LogLevel.Information;

    /// <summary>
    /// Updates the host diagnostic level requested by an MCP client.
    /// </summary>
    public void SetMinimumLevel(LogLevel level)
    {
        MinimumLevel = level;
    }

    /// <summary>
    /// Returns whether a host diagnostic message at the supplied level should be emitted.
    /// </summary>
    public bool IsEnabled(LogLevel level)
    {
        return level >= MinimumLevel;
    }
}
