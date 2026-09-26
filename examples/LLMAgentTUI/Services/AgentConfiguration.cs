// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace LLMAgentTUI.Services;

public sealed record AgentConfiguration(string Provider, Uri Endpoint, string Model, string ApiKey)
{
    // Resolve configuration beside the binary, independently of the coding workspace.
    // Local settings override JSON and Development user secrets. Re-add environment
    // and command-line providers so deployment/runtime overrides retain precedence.
    public static IHostBuilder CreateHostBuilder(string[] args)
        => Host.CreateDefaultBuilder(args).UseContentRoot(AppContext.BaseDirectory)
            .ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false)
                    .AddEnvironmentVariables()
                    .AddCommandLine(args);
            });

    public static AgentConfiguration Load(IConfiguration configuration)
    {
        string Required(string name)
        {
            var value = configuration[$"Agent:{name}"];
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Configure Agent:{name} using appsettings, User Secrets, or Agent__{name}.");
            }
            return value;
        }

        var endpointText = Required("Endpoint");
        if (!Uri.TryCreate(endpointText, UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttps && !(endpoint.Scheme == Uri.UriSchemeHttp && endpoint.IsLoopback))
            || !string.IsNullOrEmpty(endpoint.UserInfo))
        {
            throw new InvalidOperationException("Agent:Endpoint must be HTTPS (or HTTP on loopback for local servers), without embedded credentials.");
        }
        return new(Required("Provider"), endpoint, Required("Model"), Required("ApiKey"));
    }

    // Avoid inadvertently disclosing the credential when logging configuration.
    public override string ToString() => $"{Provider}: {Model} at {Endpoint} (API key redacted)";
}
