using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace MtgMcp.App.Hosting;

/// <summary>
/// Pairs a published tool with the method behind it, so the server can record what each tool accepts.
/// </summary>
/// <param name="Tool">The tool the server publishes.</param>
/// <param name="Method">The method that implements the tool.</param>
internal sealed record RegisteredTool(McpServerTool Tool, MethodInfo Method);

/// <summary>
/// Adds registered tools to a server builder together with the argument catalog the server owns.
/// </summary>
internal static class RegisteredToolExtensions
{
    /// <summary>
    /// Publishes the tools and records each one in the catalog of this server builder, creating the catalog on first use.
    /// </summary>
    internal static IMcpServerBuilder WithRegisteredTools(
        this IMcpServerBuilder builder,
        IEnumerable<RegisteredTool> tools)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(tools);
        ToolArgumentCatalog catalog = CatalogOf(builder.Services);
        List<McpServerTool> published = [];
        foreach (RegisteredTool registered in tools)
        {
            catalog.Register(registered.Method);
            published.Add(registered.Tool);
        }

        return builder.WithTools(published);
    }

    /// <summary>
    /// Finds the catalog already added to the services, or adds a new one.
    /// </summary>
    private static ToolArgumentCatalog CatalogOf(IServiceCollection services)
    {
        ServiceDescriptor? existing = services.FirstOrDefault(
            descriptor => descriptor.ServiceType == typeof(ToolArgumentCatalog));
        if (existing?.ImplementationInstance is ToolArgumentCatalog catalog)
        {
            return catalog;
        }

        catalog = new ToolArgumentCatalog();
        services.AddSingleton(catalog);
        return catalog;
    }
}
