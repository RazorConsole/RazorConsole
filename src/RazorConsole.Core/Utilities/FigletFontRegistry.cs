// Copyright (c) RazorConsole. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Spectre.Console;

namespace RazorConsole.Core.Utilities;

public static class FigletFontRegistry
{
    private static readonly ConcurrentDictionary<string, FigletFont> _fonts = new();

    public static void Add(string name, FigletFont font) => _fonts.AddOrUpdate(name, font, (_, existing) => existing);

    public static void Add(string name, string fontPath) =>
        _fonts.AddOrUpdate(name, FigletFont.Load(fontPath), (_, existing) => existing);

    public static bool TryGetOrLoad(string nameOrPath, [NotNullWhen(true)] out FigletFont? font)
    {
        if (_fonts.TryGetValue(nameOrPath, out var cachedFont))
        {
            font = cachedFont;
            return true;
        }

        if (File.Exists(nameOrPath))
        {
            try
            {
                var newFont = FigletFont.Load(nameOrPath);
                _fonts.TryAdd(nameOrPath, newFont);
                font = newFont;
                return true;
            }
            catch
            {
                font = null;
                return false;
            }
        }
        font = null;
        return false;
    }
}
