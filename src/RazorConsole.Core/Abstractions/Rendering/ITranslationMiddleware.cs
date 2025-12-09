// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Rendering.Translation.Contexts;
using RazorConsole.Core.Vdom;

using Spectre.Console.Rendering;

namespace RazorConsole.Core.Abstractions.Rendering;

public delegate IRenderable Next(TranslationContext context, VNode node);

public interface ITranslationMiddleware
{

    IRenderable Translate(TranslationContext context, Next next, VNode node);

}
