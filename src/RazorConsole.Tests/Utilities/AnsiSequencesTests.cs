// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Utilities;

namespace RazorConsole.Tests.Utilities;

public sealed class AnsiSequencesTests
{
    private const string IndexEscapeSequence = AnsiSequences.ESC + "D";
    private const string NextLineEscapeSequence = AnsiSequences.ESC + "E";
    private const string ReverseIndexEscapeSequence = AnsiSequences.ESC + "M";

    [Fact]
    public void IDN_ReturnsIndexEscapeSequence()
    {
        AnsiSequences.IDN().ShouldBe(IndexEscapeSequence);
    }

    [Fact]
    public void NEL_ReturnsNextLineEscapeSequence()
    {
        AnsiSequences.NEL().ShouldBe(NextLineEscapeSequence);
    }

    [Fact]
    public void RI_ReturnsReverseIndexEscapeSequence()
    {
        AnsiSequences.RI().ShouldBe(ReverseIndexEscapeSequence);
    }
}
