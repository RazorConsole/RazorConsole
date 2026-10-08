// Copyright (c) RazorConsole. All rights reserved.

using Spectre.Console;
using Spectre.Console.Rendering;

namespace FpsBenchmark;

/// <summary>
/// Delegating console that counts every renderable written by Spectre's live display,
/// i.e. what actually gets drawn to the terminal, independent of the rendering pipeline.
/// </summary>
public sealed class CountingConsole(IAnsiConsole inner, FrameCounter counter) : IAnsiConsole
{
    public Profile Profile => inner.Profile;

    public IAnsiConsoleCursor Cursor => inner.Cursor;

    public IAnsiConsoleInput Input => inner.Input;

    public IExclusivityMode ExclusivityMode => inner.ExclusivityMode;

    public RenderPipeline Pipeline => inner.Pipeline;

    public void Clear(bool home) => inner.Clear(home);

    public void Write(IRenderable renderable)
    {
        counter.RecordWrite();
        inner.Write(renderable);
    }
}
