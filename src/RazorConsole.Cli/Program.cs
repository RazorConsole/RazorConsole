// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Cli;
using RazorConsole.Cli.Host;
using RazorConsole.Cli.Operations;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    // The first Ctrl+C cancels the command so it can still report; a second one ends the process.
    if (!cancellation.IsCancellationRequested)
    {
        e.Cancel = true;
        cancellation.Cancel();
    }
};

return await CliApplication.RunAsync(
    args,
    new HostOperations(new HostConnector(HostFiles.Resolve(Environment.GetEnvironmentVariable), ProcessHostLauncher.Instance)),
    Console.Out,
    Console.Error,
    Environment.GetEnvironmentVariable,
    Environment.CurrentDirectory,
    cancellation.Token).ConfigureAwait(false);
