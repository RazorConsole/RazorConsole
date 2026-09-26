// Copyright (c) RazorConsole. All rights reserved.

using LLMAgentTUI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace RazorConsole.Tests.Integration;

public sealed class AgentConfigurationTests
{
    [Fact]
    public void ShippedConfiguration_UsesDeepSeekWithDummyKey()
    {
        var configuration = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false).Build();
        var options = AgentConfiguration.Load(configuration);
        options.Provider.ShouldBe("DeepSeek");
        options.Endpoint.ShouldBe(new Uri("https://api.deepseek.com"));
        options.Model.ShouldBe("deepseek-flash");
        options.ApiKey.ShouldBe("dummy");
        options.ToString().ShouldNotContain("dummy");
    }

    [Fact]
    public void Host_LoadsConfigurationBesideBinary_AndCommandLineOverridesDefaults()
    {
        using var host = AgentConfiguration.CreateHostBuilder([
                "--Agent:Provider=Local", "--Agent:Endpoint=http://localhost:11434/v1",
                "--Agent:Model=test-model", "--Agent:ApiKey=dummy-override"])
            .UseEnvironment("Production").Build();
        var configuration = AgentConfiguration.Load(host.Services.GetRequiredService<IConfiguration>());
        configuration.Model.ShouldBe("test-model");
        configuration.ApiKey.ShouldBe("dummy-override");
        host.Services.GetRequiredService<IHostEnvironment>().ContentRootPath.TrimEnd(Path.DirectorySeparatorChar)
            .ShouldBe(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
    }

    [Theory]
    [InlineData("http://remote.example.com")]
    [InlineData("file:///tmp/test")]
    [InlineData("https://user:password@example.com")]
    public void Configuration_RejectsUnsafeEndpoints(string endpoint)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Agent:Provider"] = "test",
            ["Agent:Endpoint"] = endpoint,
            ["Agent:Model"] = "test",
            ["Agent:ApiKey"] = "dummy",
        }).Build();
        Should.Throw<InvalidOperationException>(() => AgentConfiguration.Load(configuration));
    }

    [Fact]
    public void LocalConfiguration_IsOptional_OverridesJson_AndYieldsToCommandLine()
    {
        var directory = Directory.CreateTempSubdirectory("llmagent-config-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "appsettings.json"), "{\"LocalConfigurationProbe\":\"base\"}");
            using (var host = AgentConfiguration.CreateHostBuilder([]).UseContentRoot(directory).UseEnvironment("Production").Build())
            {
                host.Services.GetRequiredService<IConfiguration>()["LocalConfigurationProbe"].ShouldBe("base");
            }
            File.WriteAllText(Path.Combine(directory, "appsettings.local.json"), "{\"LocalConfigurationProbe\":\"local\"}");
            using (var host = AgentConfiguration.CreateHostBuilder([]).UseContentRoot(directory).UseEnvironment("Production").Build())
            {
                host.Services.GetRequiredService<IConfiguration>()["LocalConfigurationProbe"].ShouldBe("local");
            }
            using (var host = AgentConfiguration.CreateHostBuilder(["--LocalConfigurationProbe=command"])
                .UseContentRoot(directory).UseEnvironment("Production").Build())
            {
                host.Services.GetRequiredService<IConfiguration>()["LocalConfigurationProbe"].ShouldBe("command");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
