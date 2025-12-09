// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Rendering.Translation.Contexts;
using RazorConsole.Core.Vdom;

using Spectre.Console.Rendering;

namespace RazorConsole.Tests.Rendering.Translation.Contexts;

public sealed class TranslationContextTests
{
    [Fact]
    public void Constructor_WithNullMiddlewares_ThrowsArgumentNullException()
        => Should.Throw<ArgumentNullException>(() => new TranslationContext(null!));

    [Fact]
    public void Constructor_WithEmptyMiddlewares_ThrowsInvalidOperationException()
    {
        var middlewares = Array.Empty<ITranslationMiddleware>();

        Should.Throw<InvalidOperationException>(() => new TranslationContext(middlewares));
    }

    [Fact]
    public void Translate_WithSingleMiddleware_CallsMiddleware()
    {
        // Arrange
        var middleware = Substitute.For<ITranslationMiddleware>();
        var node = VNode.CreateText("test");
        var renderable = Substitute.For<IRenderable>();

        middleware.Translate(Arg.Any<TranslationContext>(), Arg.Any<Next>(), Arg.Any<VNode>())
            .Returns(renderable);

        var context = new TranslationContext([middleware]);

        // Act
        var result = context.Translate(node);

        // Assert
        result.ShouldBe(renderable);
        middleware.Received(1).Translate(
            Arg.Is<TranslationContext>(c => c == context),
            Arg.Any<Next>(),
            Arg.Is<VNode>(n => n == node));
    }

    [Fact]
    public void Translate_WithMultipleMiddlewares_CallsInCorrectOrder()
    {
        // Arrange
        var callOrder = new List<int>();
        var middleware1 = CreateTrackingMiddleware(1, callOrder);
        var middleware2 = CreateTrackingMiddleware(2, callOrder);
        var middleware3 = CreateTrackingMiddleware(3, callOrder, returnsResult: true);

        var node = VNode.CreateText("test");
        var context = new TranslationContext([middleware1, middleware2, middleware3]);

        // Act
        context.Translate(node);

        // Assert
        callOrder.ShouldBe(new[] { 1, 2, 3 });
    }

    [Fact]
    public void Translate_WhenMiddlewareCallsNext_PassesToNextMiddleware()
    {
        // Arrange
        var middleware1 = Substitute.For<ITranslationMiddleware>();
        var middleware2 = Substitute.For<ITranslationMiddleware>();
        var node = VNode.CreateText("test");
        var renderable2 = Substitute.For<IRenderable>();

        middleware1.Translate(Arg.Any<TranslationContext>(), Arg.Any<Next>(), Arg.Any<VNode>())
            .Returns(callInfo =>
            {
                var next = callInfo.Arg<Next>();
                return next(callInfo.Arg<TranslationContext>(), callInfo.Arg<VNode>());
            });

        middleware2.Translate(Arg.Any<TranslationContext>(), Arg.Any<Next>(), Arg.Any<VNode>())
            .Returns(renderable2);

        var context = new TranslationContext(new[] { middleware1, middleware2 });

        // Act
        var result = context.Translate(node);

        // Assert
        result.ShouldBe(renderable2);
        middleware1.Received(1).Translate(Arg.Any<TranslationContext>(), Arg.Any<Next>(), Arg.Any<VNode>());
        middleware2.Received(1).Translate(Arg.Any<TranslationContext>(), Arg.Any<Next>(), Arg.Any<VNode>());
    }

    [Fact]
    public void Translate_WhenNoMiddlewareHandlesNode_ThrowsInvalidOperationException()
    {
        // Arrange
        var middleware = Substitute.For<ITranslationMiddleware>();
        var node = VNode.CreateText("test");

        middleware.Translate(Arg.Any<TranslationContext>(), Arg.Any<Next>(), Arg.Any<VNode>())
            .Returns(callInfo =>
            {
                var next = callInfo.Arg<Next>();
                return next(callInfo.Arg<TranslationContext>(), callInfo.Arg<VNode>());
            });

        var context = new TranslationContext(new[] { middleware });

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => context.Translate(node))
            .Message.ShouldContain("No translation middleware was able to translate");
    }

    [Fact]
    public void Translate_WithThreeMiddlewares_FirstCanReturnResultWithoutCallingNext()
    {
        // Arrange
        var middleware1 = Substitute.For<ITranslationMiddleware>();
        var middleware2 = Substitute.For<ITranslationMiddleware>();
        var middleware3 = Substitute.For<ITranslationMiddleware>();
        var node = VNode.CreateText("test");
        var renderable1 = Substitute.For<IRenderable>();

        middleware1.Translate(Arg.Any<TranslationContext>(), Arg.Any<Next>(), Arg.Any<VNode>())
            .Returns(renderable1);

        var context = new TranslationContext(new[] { middleware1, middleware2, middleware3 });

        // Act
        var result = context.Translate(node);

        // Assert
        result.ShouldBe(renderable1);
        middleware1.Received(1).Translate(Arg.Any<TranslationContext>(), Arg.Any<Next>(), Arg.Any<VNode>());
        middleware2.DidNotReceive().Translate(Arg.Any<TranslationContext>(), Arg.Any<Next>(), Arg.Any<VNode>());
        middleware3.DidNotReceive().Translate(Arg.Any<TranslationContext>(), Arg.Any<Next>(), Arg.Any<VNode>());
    }

    [Fact]
    public void Translate_WithThreeMiddlewares_SecondCanReturnResultWithoutCallingNext()
    {
        // Arrange
        var middleware1 = Substitute.For<ITranslationMiddleware>();
        var middleware2 = Substitute.For<ITranslationMiddleware>();
        var middleware3 = Substitute.For<ITranslationMiddleware>();
        var node = VNode.CreateText("test");
        var renderable2 = Substitute.For<IRenderable>();

        middleware1.Translate(Arg.Any<TranslationContext>(), Arg.Any<Next>(), Arg.Any<VNode>())
            .Returns(callInfo =>
            {
                var next = callInfo.Arg<Next>();
                return next(callInfo.Arg<TranslationContext>(), callInfo.Arg<VNode>());
            });

        middleware2.Translate(Arg.Any<TranslationContext>(), Arg.Any<Next>(), Arg.Any<VNode>())
            .Returns(renderable2);

        var context = new TranslationContext(new[] { middleware1, middleware2, middleware3 });

        // Act
        var result = context.Translate(node);

        // Assert
        result.ShouldBe(renderable2);
        middleware1.Received(1).Translate(Arg.Any<TranslationContext>(), Arg.Any<Next>(), Arg.Any<VNode>());
        middleware2.Received(1).Translate(Arg.Any<TranslationContext>(), Arg.Any<Next>(), Arg.Any<VNode>());
        middleware3.DidNotReceive().Translate(Arg.Any<TranslationContext>(), Arg.Any<Next>(), Arg.Any<VNode>());
    }

    [Fact]
    public void Translate_PassesCorrectContextToEachMiddleware()
    {
        // Arrange
        var middleware = Substitute.For<ITranslationMiddleware>();
        var node = VNode.CreateText("test");
        var renderable = Substitute.For<IRenderable>();
        TranslationContext? receivedContext = null;

        middleware.Translate(Arg.Any<TranslationContext>(), Arg.Any<Next>(), Arg.Any<VNode>())
            .Returns(callInfo =>
            {
                receivedContext = callInfo.Arg<TranslationContext>();
                return renderable;
            });

        var context = new TranslationContext(new[] { middleware });

        // Act
        context.Translate(node);

        // Assert
        receivedContext.ShouldBe(context);
    }

    [Fact]
    public void Translate_PassesCorrectNodeToEachMiddleware()
    {
        // Arrange
        var middleware = Substitute.For<ITranslationMiddleware>();
        var node = VNode.CreateText("test");
        var renderable = Substitute.For<IRenderable>();
        VNode? receivedNode = null;

        middleware.Translate(Arg.Any<TranslationContext>(), Arg.Any<Next>(), Arg.Any<VNode>())
            .Returns(callInfo =>
            {
                receivedNode = callInfo.Arg<VNode>();
                return renderable;
            });

        var context = new TranslationContext(new[] { middleware });

        // Act
        context.Translate(node);

        // Assert
        receivedNode.ShouldBe(node);
    }

    private static ITranslationMiddleware CreateTrackingMiddleware(int id, List<int> callOrder, bool returnsResult = false)
    {
        var middleware = Substitute.For<ITranslationMiddleware>();
        middleware.Translate(Arg.Any<TranslationContext>(), Arg.Any<Next>(), Arg.Any<VNode>())
            .Returns(callInfo =>
            {
                callOrder.Add(id);
                if (returnsResult)
                {
                    return Substitute.For<IRenderable>();
                }
                var next = callInfo.Arg<Next>();
                return next(callInfo.Arg<TranslationContext>(), callInfo.Arg<VNode>());
            });
        return middleware;
    }

}

