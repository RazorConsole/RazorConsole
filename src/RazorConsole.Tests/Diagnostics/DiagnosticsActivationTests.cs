// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using RazorConsole.Core;
using RazorConsole.Core.Diagnostics;

namespace RazorConsole.Tests.Diagnostics;

public sealed class DiagnosticsActivationTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    public void Auto_IsEnabledInDevelopmentOrDebugBuilds(bool isDevelopment, bool isDebugBuild, bool expected)
    {
        DiagnosticsActivation.IsEnabled(RazorConsoleDiagnostics.Auto, isDevelopment, environmentOverride: null, isDebugBuild, isBrowser: false)
            .ShouldBe(expected);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Disabled_WinsOverDevelopmentAndDebugBuilds(bool isDevelopment, bool isDebugBuild)
    {
        DiagnosticsActivation.IsEnabled(RazorConsoleDiagnostics.Disabled, isDevelopment, environmentOverride: null, isDebugBuild, isBrowser: false)
            .ShouldBeFalse();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("FALSE")]
    [InlineData("off")]
    [InlineData(" disabled ")]
    public void EnvironmentOptOut_WinsOverDevelopmentAndDebugBuilds(string value)
    {
        DiagnosticsActivation.IsEnabled(RazorConsoleDiagnostics.Auto, isDevelopment: true, value, entryAssemblyIsDebugBuild: true, isBrowser: false)
            .ShouldBeFalse();
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("enabled")]
    [InlineData("")]
    public void OtherEnvironmentValues_DoNotOptInReleaseApps(string value)
    {
        DiagnosticsActivation.IsEnabled(RazorConsoleDiagnostics.Auto, isDevelopment: false, value, entryAssemblyIsDebugBuild: false, isBrowser: false)
            .ShouldBeFalse();
        DiagnosticsActivation.IsEnabled(RazorConsoleDiagnostics.Auto, isDevelopment: true, value, entryAssemblyIsDebugBuild: false, isBrowser: false)
            .ShouldBeTrue();
    }

    [Fact]
    public void Browser_IsNeverEnabled()
    {
        DiagnosticsActivation.IsEnabled(RazorConsoleDiagnostics.Auto, isDevelopment: true, environmentOverride: null, entryAssemblyIsDebugBuild: true, isBrowser: true)
            .ShouldBeFalse();
    }

    [Fact]
    public void IsDebugBuild_MatchesBuildConfiguration()
    {
#if DEBUG
        const bool expected = true;
#else
        const bool expected = false;
#endif
        DiagnosticsActivation.IsDebugBuild(typeof(DiagnosticsActivationTests).Assembly).ShouldBe(expected);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OverrideEntryAssemblyIsDebugBuild_AppliesUntilDisposed(bool value)
    {
        var production = new TestHostEnvironment(Environments.Production);
        var real = DiagnosticsActivation.IsEnabled(production, RazorConsoleDiagnostics.Auto);

        using (DiagnosticsActivation.OverrideEntryAssemblyIsDebugBuild(value))
        {
            DiagnosticsActivation.IsEnabled(production, RazorConsoleDiagnostics.Auto).ShouldBe(
                value && !DiagnosticsActivation.IsOptOut(Environment.GetEnvironmentVariable(DiagnosticsActivation.EnvironmentVariableName)));
        }

        DiagnosticsActivation.IsEnabled(production, RazorConsoleDiagnostics.Auto).ShouldBe(real);
    }

    [Fact]
    public void IsDebugBuild_IsFalseWithoutAnAssembly()
    {
        DiagnosticsActivation.IsDebugBuild(null).ShouldBeFalse();
    }

    [Fact]
    public void IsEnabled_ReadsTheHostEnvironment()
    {
        // Disabled must win even for a Development host, whatever the test assembly's configuration is.
        DiagnosticsActivation.IsEnabled(new TestHostEnvironment(Environments.Development), RazorConsoleDiagnostics.Disabled)
            .ShouldBeFalse();
    }

    [Fact]
    public void ConsoleAppOptions_DefaultsToAuto()
    {
        new ConsoleAppOptions().Diagnostics.ShouldBe(RazorConsoleDiagnostics.Auto);
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Test";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
