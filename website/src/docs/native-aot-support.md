# Native AOT

This document explains how to use **Native Ahead-of-Time (AOT)** compilation with **RazorConsole** to build standalone, lightning-fast console applications.

> [!WARNING]
> Native AOT support in RazorConsole is currently **experimental**.
> While core features like routing and rendering are tested and working, you may encounter edge cases with third-party
> libraries or complex reflection scenarios. Please report any issues on [GitHub](https://github.com/RazorConsole/RazorConsole/issues/new?template=bug-report.yml).

---

## 1. What is Native AOT?

Native AOT compiles your .NET application directly into _native machine code_ like other compiled languages does, rather than Intermediate Language (IL) that requires a JIT compiler at runtime.

**Benefits for Console Apps:**

- **Instant Startup:** No JIT warm-up time.
- **Smaller Footprint:** No need to install the .NET Runtime on the target machine; the app is self-contained.
- **Single File:** The output is a single binary executable.

---

## 2. Prerequisites

To build Native AOT applications, you need platform-specific build tools installed on your development machine or CI environment.
Look at this article in [msdocs](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/?tabs=windows%2Cnet8).

---

## 3. How to Publish

To publish your application as a native executable, use the standard `dotnet publish` command with the `-p:PublishAot=true` property.

You **must** specify a Runtime Identifier (RID), as native code is platform-specific.

```bash
# Publish for Linux
dotnet publish -c Release -r linux-x64 -p:PublishAot=true

# Publish for Windows
dotnet publish -c Release -r win-x64 -p:PublishAot=true

# Publish for macOS (Apple Silicon)
dotnet publish -c Release -r osx-arm64 -p:PublishAot=true
```

The resulting binary will be located in `bin/Release/net8.0/{rid}/publish/`.

---

## 4. Known Warnings & Limitations

### 4.1. The `IL2104` Warning

During the build, you might see:

> `warning IL2104: Assembly 'Microsoft.AspNetCore.Components' produced trim warnings`

**Why this happens:** Blazor was designed for browser scenarios where the
full .NET runtime is available. Some internal Blazor APIs use reflection
patterns that the AOT analyzer cannot verify.

**Is it safe?** Yes, for RazorConsole use cases. We've tested core features
(routing, rendering, DI) and they work correctly. The warnings are about
unused code paths in Blazor's browser-specific features.

**Suppressing the warning:**

```xml
<PropertyGroup>
  <!-- Safe for RazorConsole console apps -->
  <NoWarn>$(NoWarn);IL2104</NoWarn>
</PropertyGroup>
```

**When to investigate:** If you're using advanced Blazor features beyond
basic component rendering, test thoroughly with AOT.

### 4.2. Routing and Pages

By default, the .NET AOT compiler trims unused code aggressively. Because the Router finds pages via reflection, the trimmer might accidentally remove your page components if they aren't directly referenced.

To ensure routing works correctly, you must prevent your application assembly from being trimmed. Add this to your project file (`.csproj`):

```xml
<ItemGroup>
  <TrimmerRootAssembly Include="$(AssemblyName)" />
</ItemGroup>
```

### 4.3. Reflection & Parameters

Native AOT aggressively trims unused code. Anonymous types rely on reflection
to read properties at runtime, and the trimmer may remove property metadata
if it cannot statically prove the properties are used.

**Avoid Anonymous Types for Parameters:**

```csharp
// Avoid this in AOT
// The trimmer may remove property metadata, causing runtime failures
var parameters = new { Title = "Hello", Count = 5 };
```

**Use Dictionary Instead:**
Explicitly using `Dictionary<string, object>` ensures the AOT compiler preserves the data.

```csharp
// Preferred way for AOT
var parameters = new Dictionary<string, object>
{
    { "Title", "Hello" },
    { "Count", 5 }
};

await renderer.RenderAsync<MyComponent>(parameters);
```

### 4.4. What Works & What Doesn't

**✅ AOT-Compatible:**

- Razor component rendering
- Routing (`@page` directives)
- Dependency injection
- `System.Text.Json` (with source generators)
- LINQ (query syntax)

**⚠️ Requires Care:**

- Third-party libraries (check for `IsAotCompatible`)
- Custom reflection code
- Dynamic assembly loading

**❌ Not Supported:**

- `System.Reflection.Emit`
- C# dynamic keyword

---

## 5. Troubleshooting

### Build fails with `error MSB3073` or `link.exe` not found (Windows)

This usually means the C++ Build Tools are missing.

1. Open **Visual Studio Installer**.
2. Modify your installation.
3. Check **Desktop development with C++**.

### App crashes immediately (Segmentation Fault / MissingMethodException)

If your app uses third-party libraries that rely heavily on reflection (e.g., JSON serializers other than `System.Text.Json` source generator), they might be incompatible with AOT.

- Try enabling the AOT analysis warnings in your project to see potential issues:

```xml
<PropertyGroup>
  <IsAotCompatible>true</IsAotCompatible>
</PropertyGroup>
```

---

## 6. Examples

You can see a working AOT setup in the [RazorConsole.Gallery](https://github.com/RazorConsole/RazorConsole/blob/main/src/RazorConsole.Gallery) project.
