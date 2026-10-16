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
    private readonly HashSet<Cell> _wideningItems = [];
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

    public IReadOnlySet<Cell> WideningItems => _wideningItems;

    public int SnakeWidth { get; private set; }

    public IReadOnlySet<Cell> OccupiedCells => _snake.SelectMany(GetSegmentCells).ToHashSet();

    public IEnumerable<Cell> GetHeadCells()
    {
        var head = _snake[0];
        var radius = SnakeWidth / 2;
        for (var offset = -radius; offset <= radius; offset++)
        {
            yield return _direction switch
            {
                Direction.Up => new Cell(head.X + offset, head.Y - radius),
                Direction.Right => new Cell(head.X + radius, head.Y + offset),
                Direction.Down => new Cell(head.X + offset, head.Y + radius),
                Direction.Left => new Cell(head.X - radius, head.Y + offset),
                _ => throw new InvalidOperationException("The snake direction is invalid."),
            };
        }
    }

    public IEnumerable<Cell> GetSegmentCells(Cell segment)
    {
        var radius = SnakeWidth / 2;
        for (var y = segment.Y - radius; y <= segment.Y + radius; y++)
        {
            for (var x = segment.X - radius; x <= segment.X + radius; x++)
            {
                yield return new Cell(x, y);
            }
        }
    }

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
        _wideningItems.Clear();
        SnakeWidth = 1;
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
        if (_snake.Any(segment => GetSegmentCells(segment).Any(IsOutside))
            || _wideningItems.Any(IsOutside))
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

        // Expanding the head can reach more items, so resolve the entire pickup chain.
        int collectedItems;
        while ((collectedItems = _wideningItems.RemoveWhere(item => Covers(next, item))) > 0)
        {
            SnakeWidth += 2 * collectedItems;
        }

        var ateFood = Covers(next, Food);
        var nextSnake = new List<Cell>(_snake.Count + 1) { next };
        nextSnake.AddRange(_snake);
        if (!ateFood)
        {
            nextSnake.RemoveAt(nextSnake.Count - 1);
        }

        if (HasCollision(nextSnake))
        {
            Phase = GamePhase.GameOver;
            return;
        }

        _snake.Clear();
        _snake.AddRange(nextSnake);
        if (!ateFood)
        {
            return;
        }

        Score += 10;
        PlaceFood();
        if (Phase != GamePhase.Won && TryFindEmptyCell(out var item, reserveFood: true))
        {
            _wideningItems.Add(item);
        }
    }

    private bool IsOutside(Cell cell)
        => cell.X < 0 || cell.X >= Width || cell.Y < 0 || cell.Y >= Height;

    private bool OccupiesSnake(Cell cell, bool includeTail)
    {
        var count = includeTail ? _snake.Count : _snake.Count - 1;
        for (var index = 0; index < count; index++)
        {
            if (Covers(_snake[index], cell))
            {
                return true;
            }
        }

        return false;
    }

    private void PlaceFood()
    {
        if (TryFindEmptyCell(out var food))
        {
            Food = food;
        }
        else
        {
            Phase = GamePhase.Won;
        }
    }

    private bool TryFindEmptyCell(out Cell cell, bool reserveFood = false)
    {
        var area = Width * Height;
        var start = _random.Next(area);
        for (var offset = 0; offset < area; offset++)
        {
            var candidateIndex = (start + offset) % area;
            var candidate = new Cell(candidateIndex % Width, candidateIndex / Width);
            if ((!reserveFood || candidate != Food)
                && !_wideningItems.Contains(candidate)
                && !OccupiesSnake(candidate, includeTail: true))
            {
                cell = candidate;
                return true;
            }
        }

        cell = default;
        return false;
    }

    private bool Covers(Cell segment, Cell cell)
        => Math.Abs(segment.X - cell.X) <= SnakeWidth / 2
            && Math.Abs(segment.Y - cell.Y) <= SnakeWidth / 2;

    private bool HasCollision(IReadOnlyList<Cell> snake)
    {
        var occupied = new HashSet<Cell>();
        var centers = new HashSet<Cell>();
        var previousFootprint = new HashSet<Cell>();
        for (var index = snake.Count - 1; index >= 0; index--)
        {
            var footprint = GetSegmentCells(snake[index]).ToHashSet();
            if (!centers.Add(snake[index]) || footprint.Any(IsOutside))
            {
                return true;
            }

            // Sweep from tail to head. Only the immediately preceding footprint
            // is shared motion; entering any previously occupied body cell is a hit.
            foreach (var cell in footprint)
            {
                if (occupied.Contains(cell) && !previousFootprint.Contains(cell))
                {
                    return true;
                }
            }

            occupied.UnionWith(footprint);
            previousFootprint = footprint;
        }

        return false;
    }

    private static bool IsOpposite(Direction first, Direction second)
        => (first, second) is (Direction.Up, Direction.Down)
            or (Direction.Down, Direction.Up)
            or (Direction.Left, Direction.Right)
            or (Direction.Right, Direction.Left);
}
