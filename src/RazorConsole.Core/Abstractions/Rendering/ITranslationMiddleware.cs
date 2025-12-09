// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Rendering.Translation.Contexts;
using RazorConsole.Core.Vdom;

using Spectre.Console.Rendering;

namespace RazorConsole.Core.Abstractions.Rendering;

public delegate IRenderable TranslationDelegate(VNode node);

public interface ITranslationMiddleware
{

    IRenderable Translate(TranslationContext context, TranslationDelegate next, VNode node);

}
