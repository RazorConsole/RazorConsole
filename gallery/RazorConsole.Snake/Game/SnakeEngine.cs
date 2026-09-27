// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Snake.Game;

internal enum Direction
{
    Up,
    Right,
    Down,
    Left,
}

internal enum GamePhase
{
    Ready,
    Running,
    Paused,
    GameOver,
    Won,
}

internal readonly record struct Cell(int X, int Y);

internal sealed class SnakeEngine
{
    private const int InitialLength = 5;
    private readonly Random _random;
    private readonly List<Cell> _snake = [];
    private Direction _direction;
    private Direction _queuedDirection;

    public SnakeEngine(int width, int height, Random? random = null)
    {
        if (width < 12)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "The board must be at least 12 cells wide.");
        }

        if (height < 8)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "The board must be at least 8 cells high.");
        }

        Width = width;
        Height = height;
        _random = random ?? Random.Shared;
        Restart();
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public IReadOnlyList<Cell> Snake => _snake;

    public Cell Food { get; private set; }

    public GamePhase Phase { get; private set; }

    public int Score { get; private set; }

    public int Level => 1 + Math.Min(9, Score / 50);

    public int BaseTickDelayMilliseconds { get; private set; } = 155;

    public int TickDelayMilliseconds => Math.Clamp(
        BaseTickDelayMilliseconds - ((Level - 1) * 11),
        55,
        220);

    public void SetSpeedDelay(int milliseconds)
    {
        BaseTickDelayMilliseconds = Math.Clamp(milliseconds, 80, 220);
    }

    public void Restart()
    {
        _snake.Clear();
        var centerX = Width / 2;
        var centerY = Height / 2;
        for (var offset = 0; offset < InitialLength; offset++)
        {
            _snake.Add(new Cell(centerX - offset, centerY));
        }

        _direction = Direction.Right;
        _queuedDirection = Direction.Right;
        Score = 0;
        Phase = GamePhase.Ready;
        PlaceFood();
    }

    public void Resize(int width, int height)
    {
        if (width < 12)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "The board must be at least 12 cells wide.");
        }

        if (height < 8)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "The board must be at least 8 cells high.");
        }

        if (width == Width && height == Height)
        {
            return;
        }

        Width = width;
        Height = height;
        if (_snake.Any(IsOutside))
        {
            Restart();
            return;
        }

        if (IsOutside(Food) || OccupiesSnake(Food, includeTail: true))
        {
            PlaceFood();
        }
    }

    public void TogglePause()
    {
        Phase = Phase switch
        {
            GamePhase.Ready => GamePhase.Running,
            GamePhase.Running => GamePhase.Paused,
            GamePhase.Paused => GamePhase.Running,
            _ => Phase,
        };
    }

    public void QueueDirection(Direction direction)
    {
        if (Phase is GamePhase.GameOver or GamePhase.Won || IsOpposite(direction, _direction))
        {
            return;
        }

        _queuedDirection = direction;
        if (Phase == GamePhase.Ready)
        {
            Phase = GamePhase.Running;
        }
    }

    public void Advance()
    {
        if (Phase != GamePhase.Running)
        {
            return;
        }

        _direction = _queuedDirection;
        var head = _snake[0];
        var next = _direction switch
        {
            Direction.Up => head with { Y = head.Y - 1 },
            Direction.Right => head with { X = head.X + 1 },
            Direction.Down => head with { Y = head.Y + 1 },
            Direction.Left => head with { X = head.X - 1 },
            _ => head,
        };

        var ateFood = next == Food;
        if (IsOutside(next) || OccupiesSnake(next, includeTail: ateFood))
        {
            Phase = GamePhase.GameOver;
            return;
        }

        _snake.Insert(0, next);
        if (!ateFood)
        {
            _snake.RemoveAt(_snake.Count - 1);
            return;
        }

        Score += 10;
        if (_snake.Count == Width * Height)
        {
            Phase = GamePhase.Won;
            return;
        }

        PlaceFood();
    }

    private bool IsOutside(Cell cell)
        => cell.X < 0 || cell.X >= Width || cell.Y < 0 || cell.Y >= Height;

    private bool OccupiesSnake(Cell cell, bool includeTail)
    {
        var count = includeTail ? _snake.Count : _snake.Count - 1;
        for (var index = 0; index < count; index++)
        {
            if (_snake[index] == cell)
            {
                return true;
            }
        }

        return false;
    }

    private void PlaceFood()
    {
        var area = Width * Height;
        var start = _random.Next(area);
        for (var offset = 0; offset < area; offset++)
        {
            var candidateIndex = (start + offset) % area;
            var candidate = new Cell(candidateIndex % Width, candidateIndex / Width);
            if (!OccupiesSnake(candidate, includeTail: true))
            {
                Food = candidate;
                return;
            }
        }
    }

    private static bool IsOpposite(Direction first, Direction second)
        => (first, second) is (Direction.Up, Direction.Down)
            or (Direction.Down, Direction.Up)
            or (Direction.Left, Direction.Right)
            or (Direction.Right, Direction.Left);
}
