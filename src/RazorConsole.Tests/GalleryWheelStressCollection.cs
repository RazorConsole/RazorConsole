// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Tests;

/// <summary>
/// The Gallery wheel-storm tests (headless and in a real terminal) each keep the CPU busy for their whole run;
/// in one collection they never overlap within a test process. Other collections still run in parallel with them.
/// </summary>
[CollectionDefinition(Name)]
public sealed class GalleryWheelStressCollection
{
    public const string Name = "Gallery wheel stress";
}
