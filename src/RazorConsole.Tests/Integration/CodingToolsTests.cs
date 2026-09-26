// Copyright (c) RazorConsole. All rights reserved.

using System.Text;
using LLMAgentTUI.Services;
using Microsoft.Extensions.AI;

namespace RazorConsole.Tests.Integration;

public sealed class CodingToolsTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("razorconsole-tools-").FullName;

    [Fact]
    public async Task ReadWriteEdit_PreserveEncodingAndRejectAmbiguousEdits()
    {
        using var tools = new CodingTools(_directory);
        tools.Create().ShouldAllBe(tool => tool is ApprovalRequiredAIFunction);
        await tools.WriteAsync("src/test.txt", "one\r\ntwo\r\none", TestContext.Current.CancellationToken);
        var read = await tools.ReadAsync("src/test.txt", offset: 2, limit: 1, TestContext.Current.CancellationToken);
        read.ShouldContain("2: two");
        read.ShouldContain("offset=3");
        await Should.ThrowAsync<InvalidOperationException>(() => tools.EditAsync("src/test.txt", "one", "changed", TestContext.Current.CancellationToken));
        await tools.EditAsync("src/test.txt", "two", "changed", TestContext.Current.CancellationToken);
        var target = Path.Combine(_directory, "src/test.txt");
        (await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken)).ShouldBe("one\r\nchanged\r\none");
        await File.WriteAllTextAsync(target, "BOM text", new UTF8Encoding(true), TestContext.Current.CancellationToken);
        await tools.EditAsync("src/test.txt", "text", "preserved", TestContext.Current.CancellationToken);
        (await File.ReadAllBytesAsync(target, TestContext.Current.CancellationToken)).Take(3).ShouldBe(new byte[] { 0xef, 0xbb, 0xbf });
    }

    [Fact]
    public async Task FileTools_RejectOutsidePathsAndLinkedPaths()
    {
        using var tools = new CodingTools(_directory);
        await Should.ThrowAsync<UnauthorizedAccessException>(() => tools.ReadAsync("../outside", cancellationToken: TestContext.Current.CancellationToken));
        await Should.ThrowAsync<UnauthorizedAccessException>(() => tools.WriteAsync("../outside", "no", TestContext.Current.CancellationToken));
        if (!OperatingSystem.IsWindows())
        {
            var link = Path.Combine(_directory, "link");
            Directory.CreateSymbolicLink(link, Path.GetDirectoryName(_directory)!);
            await Should.ThrowAsync<UnauthorizedAccessException>(() => tools.WriteAsync("link/outside", "no", TestContext.Current.CancellationToken));
            Directory.Delete(link);
        }
    }

    [Fact]
    public async Task Read_TruncatesAtLineLimitWithContinuation()
    {
        using var tools = new CodingTools(_directory);
        await tools.WriteAsync("large.txt", string.Join('\n', Enumerable.Repeat("line", 2100)), TestContext.Current.CancellationToken);
        (await tools.ReadAsync("large.txt", cancellationToken: TestContext.Current.CancellationToken)).ShouldContain("offset=2001");
    }

    [Fact]
    public async Task CancelledWrite_PreservesOriginal_AndTerminalControlsAreEscaped()
    {
        using var tools = new CodingTools(_directory);
        await tools.WriteAsync("safe.txt", "original", TestContext.Current.CancellationToken);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => tools.WriteAsync("safe.txt", "replacement", cancelled.Token));
        (await File.ReadAllTextAsync(Path.Combine(_directory, "safe.txt"), TestContext.Current.CancellationToken)).ShouldBe("original");
        TerminalText.Safe("\u001b[2J\u0007safe\ntext").ShouldBe("\\u001b[2J\\u0007safe\ntext");
        Directory.EnumerateFiles(_directory, ".llmagent-*").ShouldBeEmpty();
    }

    [Fact]
    public async Task Shell_ReturnsExitAndOutput_AndHonorsTimeoutAndCancellation()
    {
        // Windows requires an installed pwsh; native shell validation runs on Unix here.
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        using var tools = new CodingTools(_directory);
        var result = await tools.ShellAsync("printf 'hello'; printf 'error' >&2; exit 3", cancellationToken: TestContext.Current.CancellationToken);
        result.ShouldContain("Exit code: 3");
        result.ShouldContain("hello");
        result.ShouldContain("error");
        await Should.ThrowAsync<TimeoutException>(() => tools.ShellAsync("sleep 30", 1, TestContext.Current.CancellationToken));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(100);
        await Should.ThrowAsync<OperationCanceledException>(() => tools.ShellAsync("sleep 30", cancellationToken: cancellation.Token));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
