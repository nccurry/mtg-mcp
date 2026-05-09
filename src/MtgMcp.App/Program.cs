using Microsoft.Extensions.Hosting;
using MtgMcp.App;

bool smoke = args.Any(arg => arg.Equals("--smoke", StringComparison.OrdinalIgnoreCase));
using IHost host = MtgMcpHost.Build(args);

if (smoke)
{
    MtgMcpHost.ValidateServices(host.Services);
    Console.Error.WriteLine("mtg-mcp host build ok");
    return;
}

await host.RunAsync().ConfigureAwait(false);
