// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Cli;
using RazorConsole.Cli.Host;
using RazorConsole.Cli.Mcp;
using RazorConsole.Cli.Operations;
using RazorConsole.Cli.Output;

using var console = CliConsole.Open();
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

var operations = new HostOperations(new HostConnector(HostFiles.Resolve(Environment.GetEnvironmentVariable), ProcessHostLauncher.Instance));
return await CliApplication.RunAsync(
    args,
    operations,
    console.Output,
    console.Error,
    Environment.GetEnvironmentVariable,
    Environment.CurrentDirectory,
    cancellation.Token,
    // The mcp verb speaks the protocol on the raw standard streams; stdout carries nothing else, logs go to stderr.
    (options, token) => McpServerHost.RunAsync(
        operations,
        options,
        Console.OpenStandardInput(),
        Console.OpenStandardOutput(),
        console.Error,
        token)).ConfigureAwait(false);
