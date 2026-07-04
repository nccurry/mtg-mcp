using MtgMcp.App.Cli;

return await FoundationCli.RunAsync(
    args,
    Console.Out,
    Console.Error,
    CancellationToken.None).ConfigureAwait(false);
