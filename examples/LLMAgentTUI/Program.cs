// Copyright (c) RazorConsole. All rights reserved.

using LLMAgentTUI.Components;
using LLMAgentTUI.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenAI;
using RazorConsole.Core;

// Get API key from environment variable or use Ollama as default
var useOllama = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENAI_API_KEY"));

var hostBuilder = Host.CreateDefaultBuilder(args)
    .UseRazorConsole<App>();

hostBuilder.ConfigureServices(services =>
{

    if (useOllama)
    {
        // Use Ollama with local model
        services.AddChatClient(client =>
            new OllamaChatClient(new Uri("http://localhost:11434"), "llama3.2"));
    }
    else
    {
        // Use OpenAI
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")!;
        services.AddChatClient(client =>
            new OpenAIClient(apiKey).AsChatClient("gpt-4o-mini"));
    }

    services.AddSingleton<IChatService, ChatService>();

    services.Configure<ConsoleAppOptions>(options =>
    {
        options.AutoClearConsole = false;
        options.EnableTerminalResizing = true;
    });
});

var host = hostBuilder.Build();

await host.RunAsync();
