// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Snake.Game;
using Spectre.Console;

namespace RazorConsole.Tests.Integration;

public sealed class SnakeEngineTests
{
    [Fact]
    public void Meals_AddLengthScoreAndPersistentDistinctTraps()
    {
        var game = CreateGame();
        game.QueueDirection(Direction.Right);
        game.Advance();
        game.Snake.Count.ShouldBe(6);
        game.Score.ShouldBe(10);
        game.WideningItems.ShouldBe([new Cell(14, 10)]);

        game.Advance();
        game.Snake.Count.ShouldBe(7);
        game.Score.ShouldBe(20);
        game.SnakeWidth.ShouldBe(1);
        game.WideningItems.Count.ShouldBe(2);
        game.Advance();
        game.WideningItems.Count.ShouldBe(2);
        game.WideningItems.ShouldNotContain(game.Food);
        game.WideningItems.ShouldAllBe(item => !game.Snake.Contains(item));
    }

    [Fact]
    public void Trap_IsConsumedAndPermanentlyWidensWithoutReward()
    {
        var game = CreateWidenedGame();
        game.SnakeWidth.ShouldBe(3);
        game.GetSegmentCells(game.Snake[0]).Count().ShouldBe(9);
        game.Snake.Count.ShouldBe(7);
        game.Score.ShouldBe(20);
        game.WideningItems.ShouldBe([new Cell(5, 5)]);
        game.Phase.ShouldBe(GamePhase.Running);

        game.Advance();
        game.SnakeWidth.ShouldBe(3);
        game.Phase.ShouldBe(GamePhase.Running);
    }

    [Fact]
    public void WidenedHead_CollidesWithWallBeforeCenterLeavesBoard()
    {
        var game = CreateWidenedGame();
        for (var step = 0; step < 4; step++)
        {
            game.Advance();
            game.Phase.ShouldBe(GamePhase.Running);
        }

        game.Snake[0].ShouldBe(new Cell(18, 10));
        game.Advance();
        game.Phase.ShouldBe(GamePhase.GameOver);
    }

    [Fact]
    public void WidenedBody_AllowsBendButCollidesWithDistantBodyFootprint()
    {
        var game = CreateWidenedGame();
        game.QueueDirection(Direction.Up);
        game.Advance();
        game.Phase.ShouldBe(GamePhase.Running);
        game.QueueDirection(Direction.Left);
        game.Advance();
        game.Phase.ShouldBe(GamePhase.GameOver);
    }

    [Fact]
    public void Expansion_CollectsAllReachedTrapsAndCanReachFood()
    {
        var game = CreateWidthFiveGame();

        game.SnakeWidth.ShouldBe(5);
        game.Score.ShouldBe(40);
        game.Snake.Count.ShouldBe(9);
        game.WideningItems.ShouldNotContain(new Cell(15, 10));
        game.WideningItems.ShouldNotContain(new Cell(14, 9));
        game.WideningItems.ShouldContain(new Cell(5, 5));
        game.Phase.ShouldBe(GamePhase.Running);
    }

    [Fact]
    public void WideSnake_CannotOverlapNearbyBodyWithoutCrossingCenterline()
    {
        var game = CreateWidthFiveGame();
        game.QueueDirection(Direction.Up);
        game.Advance();
        game.Phase.ShouldBe(GamePhase.Running);

        var nextCenter = game.Snake[0] with { X = game.Snake[0].X - 1 };
        game.Snake.ShouldNotContain(nextCenter);
        game.QueueDirection(Direction.Left);
        game.Advance();
        game.Phase.ShouldBe(GamePhase.GameOver);
    }

    [Theory]
    [InlineData((int)Direction.Up)]
    [InlineData((int)Direction.Right)]
    [InlineData((int)Direction.Down)]
    public void WideHead_IsOnlyTheLeadingEdge(int directionValue)
    {
        var direction = (Direction)directionValue;
        var game = CreateWidenedGame();
        game.QueueDirection(direction);
        game.Advance();
        game.Phase.ShouldBe(GamePhase.Running);
        var head = game.Snake[0];
        var headCells = game.GetHeadCells().ToArray();
        headCells.Length.ShouldBe(game.SnakeWidth);
        headCells.ShouldAllBe(cell => game.OccupiedCells.Contains(cell));
        if (direction == Direction.Right)
        {
            headCells.ShouldAllBe(cell => cell.X == head.X + 1);
            headCells.Select(cell => cell.Y).ShouldBe([head.Y - 1, head.Y, head.Y + 1]);
        }
        else
        {
            var leadingY = head.Y + (direction == Direction.Up ? -1 : 1);
            headCells.ShouldAllBe(cell => cell.Y == leadingY);
            headCells.Select(cell => cell.X).ShouldBe([head.X - 1, head.X, head.X + 1]);
        }
    }

    [Fact]
    public void WideHorizontalBody_IsOneSolidRectangleWithUniqueCells()
    {
        var game = CreateWidenedGame();
        game.Advance();
        var occupied = game.OccupiedCells;
        var left = game.Snake[^1].X - 1;
        var right = game.Snake[0].X + 1;
        occupied.Count.ShouldBe((game.Snake.Count + 2) * game.SnakeWidth);
        for (var y = game.Snake[0].Y - 1; y <= game.Snake[0].Y + 1; y++)
        {
            for (var x = left; x <= right; x++)
            {
                occupied.ShouldContain(new Cell(x, y));
            }
        }
    }

    [Fact]
    public void HorizontalBoardPixels_HaveIdenticalRowsWithoutColorLayers()
    {
        var game = CreateWidenedGame();
        for (var step = 0; step < 3; step++)
        {
            game.Advance();
            var pixels = RazorConsole.Snake.Components.App.BuildBoard(game);
            var left = game.Snake[^1].X - 1;
            var right = game.Snake[0].X + 1;
            for (var y = game.Snake[0].Y - 1; y <= game.Snake[0].Y + 1; y++)
            {
                for (var x = left; x <= right; x++)
                {
                    var expected = x == right ? new Color(143, 255, 182) : new Color(38, 194, 129);
                    pixels[(y * game.Width) + x].color.ShouldBe(expected);
                }
            }
        }
    }

    [Theory]
    [InlineData((int)Direction.Up)]
    [InlineData((int)Direction.Down)]
    public void TurningBoardPixels_HighlightOnlyNewLeadingFace(int directionValue)
    {
        var game = CreateWidenedGame();
        var direction = (Direction)directionValue;
        game.QueueDirection(direction);
        for (var step = 0; step < 3; step++)
        {
            game.Advance();
            game.Phase.ShouldBe(GamePhase.Running);
            var head = game.Snake[0];
            var leadingY = head.Y + (direction == Direction.Up ? -1 : 1);
            var pixels = RazorConsole.Snake.Components.App.BuildBoard(game);
            var highlighted = pixels.Where(pixel => pixel.color == new Color(143, 255, 182)).ToArray();
            highlighted.Length.ShouldBe(game.SnakeWidth);
            highlighted.ShouldAllBe(pixel => pixel.y == leadingY);
            foreach (var cell in game.OccupiedCells.Where(cell => cell.Y != leadingY))
            {
                pixels[(cell.Y * game.Width) + cell.X].color.ShouldBe(new Color(38, 194, 129));
            }
        }
    }

    [Fact]
    public void Placement_AvoidsEntireBodyFootprintFoodAndExistingTraps()
    {
        var game = new SnakeEngine(20, 20, new CellRandom(
            new(11, 10), new(12, 10), new(14, 10),
            new(15, 10), new(5, 5), new(14, 9), new(5, 5)));
        game.QueueDirection(Direction.Right);
        for (var step = 0; step < 4; step++)
        {
            game.Advance();
        }

        game.SnakeWidth.ShouldBe(3);
        var occupied = game.Snake.SelectMany(game.GetSegmentCells).ToHashSet();
        occupied.ShouldNotContain(game.Food);
        game.WideningItems.Count.ShouldBe(2);
        game.WideningItems.ShouldNotContain(game.Food);
        game.WideningItems.ShouldAllBe(item => !occupied.Contains(item));
    }

    [Fact]
    public void Restart_ClearsWidthTrapsAndScore()
    {
        var game = CreateWidenedGame();
        game.Restart();
        game.SnakeWidth.ShouldBe(1);
        game.WideningItems.ShouldBeEmpty();
        game.Snake.Count.ShouldBe(5);
        game.Score.ShouldBe(0);
        game.Phase.ShouldBe(GamePhase.Ready);
    }

    [Fact]
    public void Resize_PreservesStateWhenEverythingFits()
    {
        var game = CreateWidenedGame();
        var traps = game.WideningItems.ToArray();
        game.Resize(25, 25);
        game.SnakeWidth.ShouldBe(3);
        game.Score.ShouldBe(20);
        game.WideningItems.ShouldBe(traps);
        game.Phase.ShouldBe(GamePhase.Running);
    }

    [Fact]
    public void Resize_RestartsWhenWideBodyWouldBeClipped()
    {
        var game = CreateWidenedGame();
        game.Resize(15, 20);
        game.SnakeWidth.ShouldBe(1);
        game.WideningItems.ShouldBeEmpty();
        game.Phase.ShouldBe(GamePhase.Ready);
    }

    [Fact]
    public void Resize_RestartsRatherThanSilentlyDiscardingTraps()
    {
        var game = new SnakeEngine(20, 20, new CellRandom(
            new(11, 10), new(12, 10), new(19, 19)));
        game.QueueDirection(Direction.Right);
        game.Advance();
        game.Resize(18, 18);
        game.WideningItems.ShouldBeEmpty();
        game.Score.ShouldBe(0);
        game.Phase.ShouldBe(GamePhase.Ready);
    }

    [Fact]
    public void ExpansionIntoWall_EndsGameEvenWhenHeadHasRoom()
    {
        var game = new SnakeEngine(20, 20, new CellRandom(
            new(11, 10), new(12, 5), new(19, 10)));
        game.QueueDirection(Direction.Right);
        for (var step = 0; step < 9; step++)
        {
            game.Advance();
        }

        game.SnakeWidth.ShouldBe(3);
        game.WideningItems.ShouldBeEmpty();
        game.Phase.ShouldBe(GamePhase.GameOver);
    }

    [Fact]
    public void NarrowSnake_CanMoveIntoVacatingTail()
    {
        var game = CreateGame();
        game.QueueDirection(Direction.Right);
        game.Advance();
        game.QueueDirection(Direction.Up);
        game.Advance();
        game.Advance();
        game.QueueDirection(Direction.Left);
        game.Advance();
        game.QueueDirection(Direction.Down);
        game.Advance();
        var tail = game.Snake[^1];
        game.Advance();
        game.Snake[0].ShouldBe(tail);
        game.Phase.ShouldBe(GamePhase.Running);
    }

    [Fact]
    public void Pause_DoesNotMoveOrConsumeItems()
    {
        var game = CreateGame();
        game.QueueDirection(Direction.Right);
        game.Advance();
        game.TogglePause();
        var head = game.Snake[0];
        game.Advance();
        game.Snake[0].ShouldBe(head);
        game.Score.ShouldBe(10);
        game.WideningItems.Count.ShouldBe(1);
    }

    [Fact]
    public void NarrowSnake_StillCollidesWithItself()
    {
        var game = CreateGame();
        game.QueueDirection(Direction.Up);
        game.Advance();
        game.QueueDirection(Direction.Left);
        game.Advance();
        game.QueueDirection(Direction.Down);
        game.Advance();
        game.Phase.ShouldBe(GamePhase.GameOver);
    }

    private static SnakeEngine CreateGame()
        => new(20, 20, new CellRandom(
            new(11, 10), new(12, 10), new(14, 10), new(12, 5), new(5, 5)));

    private static SnakeEngine CreateWidenedGame()
    {
        var game = CreateGame();
        game.QueueDirection(Direction.Right);
        for (var step = 0; step < 4; step++)
        {
            game.Advance();
        }

        return game;
    }

    private static SnakeEngine CreateWidthFiveGame()
    {
        var game = new SnakeEngine(20, 20, new CellRandom(
            new(11, 10), new(12, 10), new(15, 10),
            new(13, 10), new(14, 9), new(16, 9), new(5, 5)));
        game.QueueDirection(Direction.Right);
        for (var step = 0; step < 5; step++)
        {
            game.Advance();
        }

        return game;
    }

    private sealed class CellRandom(params Cell[] cells) : Random
    {
        private readonly Queue<Cell> _cells = new(cells);

        public override int Next(int maxValue)
        {
            if (!_cells.TryDequeue(out var cell))
            {
                return maxValue - 1;
            }

            return (cell.Y * 20) + cell.X;
        }
    }
}
