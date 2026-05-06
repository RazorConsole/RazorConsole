# LLM Agent TUI Example

![alt text](../../assets/example/chatbot.png)

This example demonstrates how to build an AI-powered chat application using RazorConsole and Microsoft.Extensions.AI SDK. The interface is inspired by Claude Code's terminal UI.

## Features

- Interactive chat interface with AI agents
- Support for multiple LLM providers (OpenAI, Ollama)
- Clean, responsive console UI using RazorConsole components
- Conversation history tracking
- Real-time streaming responses

## Prerequisites

Choose one of the following options:

### Option 1: Use OpenAI

Set your OpenAI API key as an environment variable:

```bash
export OPENAI_API_KEY="your-api-key-here"
```

### Option 2: Use Ollama (Local)

1. Install Ollama: https://ollama.ai/
2. Pull a model (e.g., llama3.2):
   ```bash
   ollama pull llama3.2
   ```
3. Make sure Ollama is running on `http://localhost:11434`

## Running the Example

```bash
cd examples/LLMAgentTUI
dotnet run
```

The application will automatically detect if you have an OpenAI API key set. If not, it will use Ollama with the llama3.2 model.

## Usage

1. Type your message in the input field
2. Press Enter or click the "Send" button to submit
3. Wait for the AI response
4. Continue the conversation
5. Press Ctrl+C to exit

## Project Structure

- `Program.cs` - Application entry point and service configuration
- `Components/App.razor` - Main UI component with chat interface
- `Services/IChatService.cs` - Chat service interface
- `Services/ChatService.cs` - Chat service implementation using M.E.AI

## Customization

### Changing the Model

Edit `Program.cs` to use a different model:

```csharp
// For Ollama
services.AddChatClient(client =>
    new OllamaChatClient(new Uri("http://localhost:11434"), "your-model-name"));

// For OpenAI
services.AddChatClient(client =>
    new OpenAIClient(apiKey).AsChatClient("gpt-4"));
```

### Customizing the UI

The UI is built using RazorConsole components. You can modify `Components/App.razor` to:
- Change colors and styling
- Add more panels or sections
- Customize the message display format
- Add additional features like message history export

## Technologies Used

- **RazorConsole.Core** - Console UI framework with Razor components
- **Microsoft.Extensions.AI** - Unified AI abstraction layer
- **Microsoft.Extensions.AI.OpenAI** - OpenAI provider
- **Microsoft.Extensions.AI.Ollama** - Ollama provider
- **Spectre.Console** - Rich console rendering

## License

This example is part of the RazorConsole project and follows the same MIT license.
