// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.TankBattle.Game;

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
}

internal enum Tile
{
    Empty,
    Brick,
    Steel,
    Base,
}

internal enum SkillType
{
    None,
    RapidFire,
    SpreadShot,
    Homing,
}

internal readonly record struct Cell(int X, int Y);

internal sealed class Tank
{
    public Tank(Cell position, Direction direction, bool isEnemy)
    {
        Position = position;
        Direction = direction;
        IsEnemy = isEnemy;
    }

    public Cell Position { get; set; }

    public Direction Direction { get; set; }

    public bool IsEnemy { get; }

    public int Cooldown { get; set; }

    public int ThinkTicks { get; set; }
}

internal sealed class Bullet
{
    public Bullet(Cell position, int deltaX, int deltaY, bool isEnemy, bool isHoming = false)
    {
        Position = position;
        DeltaX = deltaX;
        DeltaY = deltaY;
        IsEnemy = isEnemy;
        IsHoming = isHoming;
    }

    public Cell Position { get; set; }

    public int DeltaX { get; set; }

    public int DeltaY { get; set; }

    public bool IsEnemy { get; }

    public bool IsHoming { get; }
}

internal readonly record struct PowerUp(Cell Position, SkillType Type);

internal sealed class TankEngine
{
    private readonly Random _random;
    private readonly List<Tank> _enemies = [];
    private readonly List<Bullet> _bullets = [];
    private readonly List<PowerUp> _powerUps = [];
    private Tile[,] _tiles = new Tile[1, 1];
    private int _spawnCooldown;
    private int _spawned;

    public TankEngine(int width, int height, Random? random = null)
    {
        _random = random ?? Random.Shared;
        Width = width;
        Height = height;
        Player = new Tank(default, Direction.Up, false);
        Restart();
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public Tank Player { get; }

    public IReadOnlyList<Tank> Enemies => _enemies;

    public IReadOnlyList<Bullet> Bullets => _bullets;

    public IReadOnlyList<PowerUp> PowerUps => _powerUps;

    public GamePhase Phase { get; private set; }

    public int Score { get; private set; }

    public int Lives { get; private set; }

    public int Level { get; private set; }

    public SkillType ActiveSkill { get; private set; }

    public int SkillTicksRemaining { get; private set; }

    public int SkillSecondsRemaining => (int)Math.Ceiling(SkillTicksRemaining * TickDelayMilliseconds / 1000d);

    public int TotalEnemies => 8 + (Level * 2);

    public int RemainingEnemies => Math.Max(0, TotalEnemies - _spawned + _enemies.Count);

    public int TickDelayMilliseconds => Math.Max(55, 90 - ((Level - 1) * 4));

    public Tile GetTile(Cell cell) => IsInside(cell) ? _tiles[cell.X, cell.Y] : Tile.Steel;

    public IReadOnlyList<Cell> GetTankCells(Tank tank) => GetTankCells(tank.Position, tank.Direction);

    public void Restart()
    {
        Score = 0;
        Lives = 3;
        Level = 1;
        StartLevel(GamePhase.Ready);
    }

    public void Resize(int width, int height)
    {
        if (width == Width && height == Height)
        {
            return;
        }

        Width = width;
        Height = height;
        StartLevel(Phase == GamePhase.Ready ? GamePhase.Ready : GamePhase.Running);
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

    public void MovePlayer(Direction direction)
    {
        if (Phase == GamePhase.Ready)
        {
            Phase = GamePhase.Running;
        }

        if (Phase != GamePhase.Running)
        {
            return;
        }

        TryMove(Player, direction);
    }

    public void FirePlayer()
    {
        if (Phase == GamePhase.Ready)
        {
            Phase = GamePhase.Running;
        }

        if (Phase == GamePhase.Running)
        {
            Fire(Player);
        }
    }

    public void Advance()
    {
        if (Phase != GamePhase.Running)
        {
            return;
        }

        Player.Cooldown = Math.Max(0, Player.Cooldown - 1);
        if (SkillTicksRemaining > 0)
        {
            SkillTicksRemaining--;
            if (SkillTicksRemaining == 0)
            {
                ActiveSkill = SkillType.None;
            }
        }

        AdvanceEnemies();
        SpawnEnemyIfNeeded();
        AdvanceBullets();

        if (_spawned >= TotalEnemies && _enemies.Count == 0)
        {
            Level++;
            StartLevel(GamePhase.Running);
        }
    }

    private void StartLevel(GamePhase phase)
    {
        _tiles = new Tile[Width, Height];
        _enemies.Clear();
        _bullets.Clear();
        _powerUps.Clear();
        _spawned = 0;
        _spawnCooldown = 1;
        Player.Position = PlayerSpawn;
        Player.Direction = Direction.Up;
        Player.Cooldown = 0;
        BuildMap();
        PlacePowerUps();
        ActiveSkill = SkillType.None;
        SkillTicksRemaining = 0;
        Phase = phase;
    }

    private void BuildMap()
    {
        // Start with solid masonry, then carve a sparse maze on a six-cell
        // lattice. Five-cell-wide corridors leave generous combat space around
        // the 3x3 tanks while preserving horizontal and vertical wall runs.
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                _tiles[x, y] = Tile.Brick;
            }
        }

        var columnCenters = new List<int>();
        var rowCenters = new List<int>();
        for (var x = 3; x <= Width - 4; x += 6)
        {
            columnCenters.Add(x);
        }

        for (var y = 3; y <= Height - 4; y += 6)
        {
            rowCenters.Add(y);
        }

        CarveMaze(columnCenters, rowCenters);
        CarveExtraPassages(columnCenters, rowCenters);

        // Join all three deployment pads to the first maze row.
        foreach (var spawn in EnemySpawnPoints)
        {
            var nearestColumn = columnCenters.MinBy(x => Math.Abs(x - spawn.X));
            CarveHorizontal(rowCenters[0], spawn.X, nearestColumn);
            CarveVertical(spawn.X, spawn.Y, rowCenters[0]);
            ClearArea(spawn, 1);
        }

        // Join the player's deployment pad to the last maze row.
        var playerColumn = columnCenters.MinBy(x => Math.Abs(x - Player.Position.X));
        var lastMazeRow = rowCenters[^1];
        CarveHorizontal(lastMazeRow, playerColumn, Player.Position.X);
        CarveVertical(Player.Position.X, lastMazeRow, Player.Position.Y);
        ClearTankZone(Player.Position, Direction.Up);

        // Sparse steel nodes reinforce the continuous brick wall network without
        // changing its topology.
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (_tiles[x, y] == Tile.Brick && ((x * 17) + (y * 31) + (Level * 7)) % 47 == 0)
                {
                    _tiles[x, y] = Tile.Steel;
                }
            }
        }

        var baseCell = new Cell(Width / 2, Height - 1);
        _tiles[baseCell.X, baseCell.Y] = Tile.Base;
        foreach (var cell in new[]
        {
            new Cell(baseCell.X - 1, baseCell.Y),
            new Cell(baseCell.X + 1, baseCell.Y),
            new Cell(baseCell.X - 1, baseCell.Y - 1),
            new Cell(baseCell.X, baseCell.Y - 1),
            new Cell(baseCell.X + 1, baseCell.Y - 1),
        })
        {
            _tiles[cell.X, cell.Y] = Tile.Brick;
        }
    }

    private void CarveMaze(IReadOnlyList<int> columnCenters, IReadOnlyList<int> rowCenters)
    {
        var visited = new bool[columnCenters.Count, rowCenters.Count];
        var stack = new Stack<(int Column, int Row)>();
        stack.Push((0, 0));
        visited[0, 0] = true;
        CarveRoom(columnCenters[0], rowCenters[0]);

        while (stack.Count > 0)
        {
            var current = stack.Peek();
            var neighbors = new List<(int Column, int Row)>();
            AddUnvisitedNeighbor(neighbors, visited, current.Column - 1, current.Row);
            AddUnvisitedNeighbor(neighbors, visited, current.Column + 1, current.Row);
            AddUnvisitedNeighbor(neighbors, visited, current.Column, current.Row - 1);
            AddUnvisitedNeighbor(neighbors, visited, current.Column, current.Row + 1);

            if (neighbors.Count == 0)
            {
                stack.Pop();
                continue;
            }

            var next = neighbors[_random.Next(neighbors.Count)];
            var currentX = columnCenters[current.Column];
            var currentY = rowCenters[current.Row];
            var nextX = columnCenters[next.Column];
            var nextY = rowCenters[next.Row];
            if (current.Row == next.Row)
            {
                CarveHorizontal(currentY, currentX, nextX);
            }
            else
            {
                CarveVertical(currentX, currentY, nextY);
            }

            CarveRoom(nextX, nextY);
            visited[next.Column, next.Row] = true;
            stack.Push(next);
        }
    }

    private void CarveExtraPassages(IReadOnlyList<int> columnCenters, IReadOnlyList<int> rowCenters)
    {
        // Braid some neighboring rooms together. These extra openings reduce wall
        // density and dead-end frequency without flattening the maze into a field.
        for (var row = 0; row < rowCenters.Count; row++)
        {
            for (var column = 0; column < columnCenters.Count; column++)
            {
                if (column + 1 < columnCenters.Count && _random.NextDouble() < 0.22)
                {
                    CarveHorizontal(rowCenters[row], columnCenters[column], columnCenters[column + 1]);
                }

                if (row + 1 < rowCenters.Count && _random.NextDouble() < 0.22)
                {
                    CarveVertical(columnCenters[column], rowCenters[row], rowCenters[row + 1]);
                }
            }
        }
    }

    private static void AddUnvisitedNeighbor(
        List<(int Column, int Row)> neighbors,
        bool[,] visited,
        int column,
        int row)
    {
        if (column >= 0 && column < visited.GetLength(0) &&
            row >= 0 && row < visited.GetLength(1) &&
            !visited[column, row])
        {
            neighbors.Add((column, row));
        }
    }

    private void CarveRoom(int centerX, int centerY)
    {
        for (var y = centerY - 2; y <= centerY + 2; y++)
        {
            for (var x = centerX - 2; x <= centerX + 2; x++)
            {
                _tiles[x, y] = Tile.Empty;
            }
        }
    }

    private void CarveHorizontal(int y, int fromX, int toX)
    {
        for (var x = Math.Min(fromX, toX); x <= Math.Max(fromX, toX); x++)
        {
            for (var offsetY = -2; offsetY <= 2; offsetY++)
            {
                var cell = new Cell(x, y + offsetY);
                if (IsInside(cell))
                {
                    _tiles[cell.X, cell.Y] = Tile.Empty;
                }
            }
        }
    }

    private void CarveVertical(int x, int fromY, int toY)
    {
        for (var y = Math.Min(fromY, toY); y <= Math.Max(fromY, toY); y++)
        {
            for (var offsetX = -2; offsetX <= 2; offsetX++)
            {
                var cell = new Cell(x + offsetX, y);
                if (IsInside(cell))
                {
                    _tiles[cell.X, cell.Y] = Tile.Empty;
                }
            }
        }
    }

    private void AdvanceEnemies()
    {
        foreach (var enemy in _enemies.ToArray())
        {
            enemy.Cooldown = Math.Max(0, enemy.Cooldown - 1);
            enemy.ThinkTicks--;
            var desiredDirection = enemy.Direction;
            if (enemy.ThinkTicks <= 0)
            {
                desiredDirection = ChooseEnemyDirection(enemy);
                enemy.ThinkTicks = _random.Next(5, 17);
            }

            if (_random.NextDouble() < 0.5 && !TryMove(enemy, desiredDirection))
            {
                enemy.ThinkTicks = 0;
            }

            if (_random.NextDouble() < 0.07)
            {
                Fire(enemy);
            }
        }
    }

    private Direction ChooseEnemyDirection(Tank enemy)
    {
        if (Math.Abs(enemy.Position.X - Player.Position.X) <= 1 && _random.NextDouble() < 0.65)
        {
            return enemy.Position.Y < Player.Position.Y ? Direction.Down : Direction.Up;
        }

        return _random.Next(6) switch
        {
            0 => Direction.Up,
            1 => Direction.Left,
            2 => Direction.Right,
            _ => Direction.Down,
        };
    }

    private void SpawnEnemyIfNeeded()
    {
        _spawnCooldown--;
        if (_spawned >= TotalEnemies || _enemies.Count >= Math.Min(3 + Level, 7) || _spawnCooldown > 0)
        {
            return;
        }

        var spawnPoints = EnemySpawnPoints;
        var start = _random.Next(spawnPoints.Length);
        for (var offset = 0; offset < spawnPoints.Length; offset++)
        {
            var cell = spawnPoints[(start + offset) % spawnPoints.Length];
            if (CanTankOccupy(cell, Direction.Down, null))
            {
                _enemies.Add(new Tank(cell, Direction.Down, true));
                _spawned++;
                _spawnCooldown = Math.Max(10, 28 - (Level * 2));
                return;
            }
        }
    }

    private void AdvanceBullets()
    {
        foreach (var bullet in _bullets.ToArray())
        {
            if (bullet.IsHoming && _enemies.Count > 0)
            {
                var target = _enemies.MinBy(enemy =>
                    Math.Abs(enemy.Position.X - bullet.Position.X) + Math.Abs(enemy.Position.Y - bullet.Position.Y));
                if (target is not null)
                {
                    bullet.DeltaX = Math.Sign(target.Position.X - bullet.Position.X);
                    bullet.DeltaY = Math.Sign(target.Position.Y - bullet.Position.Y);
                }
            }

            var next = new Cell(bullet.Position.X + bullet.DeltaX, bullet.Position.Y + bullet.DeltaY);
            if (!IsInside(next))
            {
                _bullets.Remove(bullet);
                continue;
            }

            var tile = _tiles[next.X, next.Y];
            if (tile == Tile.Brick)
            {
                _tiles[next.X, next.Y] = Tile.Empty;
                _bullets.Remove(bullet);
                continue;
            }

            if (tile == Tile.Steel)
            {
                _bullets.Remove(bullet);
                continue;
            }

            if (tile == Tile.Base)
            {
                _bullets.Remove(bullet);
                Phase = GamePhase.GameOver;
                continue;
            }

            if (bullet.IsEnemy && TankOccupies(Player, next))
            {
                _bullets.Remove(bullet);
                HitPlayer();
                continue;
            }

            var enemy = _enemies.FirstOrDefault(candidate => !bullet.IsEnemy && TankOccupies(candidate, next));
            if (enemy is not null)
            {
                _enemies.Remove(enemy);
                _bullets.Remove(bullet);
                Score += 100;
                TryDropPowerUp(next);
                continue;
            }

            var opposingBullet = _bullets.FirstOrDefault(candidate =>
                candidate != bullet && candidate.IsEnemy != bullet.IsEnemy && candidate.Position == next);
            if (opposingBullet is not null)
            {
                _bullets.Remove(opposingBullet);
                _bullets.Remove(bullet);
                continue;
            }

            bullet.Position = next;
        }
    }

    private void HitPlayer()
    {
        Lives--;
        if (Lives <= 0)
        {
            Phase = GamePhase.GameOver;
            return;
        }

        Player.Position = PlayerSpawn;
        Player.Direction = Direction.Up;
        var playerCells = GetTankCells(Player);
        _enemies.RemoveAll(enemy => GetTankCells(enemy).Any(playerCells.Contains));
        _bullets.RemoveAll(bullet => bullet.IsEnemy && playerCells.Contains(bullet.Position));
    }

    private bool TryMove(Tank tank, Direction direction)
    {
        var next = Step(tank.Position, direction);
        if (CanTankOccupy(next, direction, tank))
        {
            tank.Direction = direction;
            tank.Position = next;
            if (tank == Player)
            {
                CollectPowerUp();
            }

            return true;
        }

        if (CanTankOccupy(tank.Position, direction, tank))
        {
            tank.Direction = direction;
            if (tank == Player)
            {
                CollectPowerUp();
            }
        }

        return false;
    }

    private void Fire(Tank tank)
    {
        if (tank.Cooldown > 0)
        {
            return;
        }

        var position = Step(tank.Position, tank.Direction);
        if (!IsInside(position))
        {
            return;
        }

        var (deltaX, deltaY) = DirectionVector(tank.Direction);
        if (!tank.IsEnemy && ActiveSkill == SkillType.SpreadShot)
        {
            if (deltaX == 0)
            {
                _bullets.Add(new Bullet(position, -1, deltaY, false));
                _bullets.Add(new Bullet(position, 0, deltaY, false));
                _bullets.Add(new Bullet(position, 1, deltaY, false));
            }
            else
            {
                _bullets.Add(new Bullet(position, deltaX, -1, false));
                _bullets.Add(new Bullet(position, deltaX, 0, false));
                _bullets.Add(new Bullet(position, deltaX, 1, false));
            }
        }
        else
        {
            var isHoming = !tank.IsEnemy && ActiveSkill == SkillType.Homing;
            _bullets.Add(new Bullet(position, deltaX, deltaY, tank.IsEnemy, isHoming));
        }

        tank.Cooldown = tank.IsEnemy ? 8 : ActiveSkill == SkillType.RapidFire ? 1 : 4;
    }

    private bool CanTankOccupy(Cell position, Direction direction, Tank? movingTank)
    {
        var candidateCells = GetTankCells(position, direction);
        if (candidateCells.Any(cell => !IsInside(cell) || _tiles[cell.X, cell.Y] != Tile.Empty))
        {
            return false;
        }

        if (movingTank != Player && candidateCells.Any(cell => TankOccupies(Player, cell)))
        {
            return false;
        }

        return !_enemies.Any(enemy =>
            enemy != movingTank && candidateCells.Any(cell => TankOccupies(enemy, cell)));
    }

    private void ClearTankZone(Cell position, Direction direction)
    {
        foreach (var cell in GetTankCells(position, direction))
        {
            if (IsInside(cell))
            {
                _tiles[cell.X, cell.Y] = Tile.Empty;
            }
        }
    }

    private void ClearArea(Cell center, int radius)
    {
        for (var y = center.Y - radius; y <= center.Y + radius; y++)
        {
            for (var x = center.X - radius; x <= center.X + radius; x++)
            {
                var cell = new Cell(x, y);
                if (IsInside(cell))
                {
                    _tiles[x, y] = Tile.Empty;
                }
            }
        }
    }

    private void PlacePowerUps()
    {
        foreach (var skill in new[] { SkillType.RapidFire, SkillType.SpreadShot, SkillType.Homing })
        {
            var area = Math.Max(1, (Width - 4) * (Height - 6));
            var start = _random.Next(area);
            for (var offset = 0; offset < area; offset++)
            {
                var index = (start + offset) % area;
                var cell = new Cell(2 + (index % (Width - 4)), 2 + (index / (Width - 4)));
                if (_tiles[cell.X, cell.Y] != Tile.Empty ||
                    _powerUps.Any(powerUp => ManhattanDistance(powerUp.Position, cell) < 5) ||
                    GetTankCells(Player).Contains(cell))
                {
                    continue;
                }

                _powerUps.Add(new PowerUp(cell, skill));
                break;
            }
        }
    }

    private void CollectPowerUp()
    {
        var playerCells = GetTankCells(Player);
        var collected = _powerUps.FirstOrDefault(powerUp => playerCells.Contains(powerUp.Position));
        if (collected.Type == SkillType.None)
        {
            return;
        }

        _powerUps.Remove(collected);
        ActiveSkill = collected.Type;
        SkillTicksRemaining = collected.Type == SkillType.RapidFire ? 260 : 210;
        Score += 50;
    }

    private void TryDropPowerUp(Cell position)
    {
        if (_random.NextDouble() >= 0.18 ||
            _tiles[position.X, position.Y] != Tile.Empty ||
            _powerUps.Any(powerUp => powerUp.Position == position))
        {
            return;
        }

        var skill = _random.Next(3) switch
        {
            0 => SkillType.RapidFire,
            1 => SkillType.SpreadShot,
            _ => SkillType.Homing,
        };
        _powerUps.Add(new PowerUp(position, skill));
    }

    private static int ManhattanDistance(Cell first, Cell second)
        => Math.Abs(first.X - second.X) + Math.Abs(first.Y - second.Y);

    private bool TankOccupies(Tank tank, Cell cell) => GetTankCells(tank).Contains(cell);

    private Cell PlayerSpawn => new(Math.Max(2, (Width / 2) - 4), Height - 3);

    private Cell[] EnemySpawnPoints =>
        [new Cell(2, 2), new Cell(Width / 2, 2), new Cell(Width - 3, 2)];

    private static Cell[] GetTankCells(Cell center, Direction direction)
    {
        var offsets = direction switch
        {
            Direction.Up => new[] { new Cell(0, -1), new Cell(-1, 0), new Cell(0, 0), new Cell(1, 0), new Cell(-1, 1), new Cell(1, 1) },
            Direction.Right => new[] { new Cell(-1, -1), new Cell(0, -1), new Cell(0, 0), new Cell(1, 0), new Cell(-1, 1), new Cell(0, 1) },
            Direction.Down => new[] { new Cell(-1, -1), new Cell(1, -1), new Cell(-1, 0), new Cell(0, 0), new Cell(1, 0), new Cell(0, 1) },
            Direction.Left => new[] { new Cell(0, -1), new Cell(1, -1), new Cell(-1, 0), new Cell(0, 0), new Cell(0, 1), new Cell(1, 1) },
            _ => [],
        };

        return offsets.Select(offset => new Cell(center.X + offset.X, center.Y + offset.Y)).ToArray();
    }

    private bool IsInside(Cell cell)
        => cell.X >= 0 && cell.X < Width && cell.Y >= 0 && cell.Y < Height;

    private static Cell Step(Cell cell, Direction direction)
        => direction switch
        {
            Direction.Up => cell with { Y = cell.Y - 1 },
            Direction.Right => cell with { X = cell.X + 1 },
            Direction.Down => cell with { Y = cell.Y + 1 },
            Direction.Left => cell with { X = cell.X - 1 },
            _ => cell,
        };

    private static (int DeltaX, int DeltaY) DirectionVector(Direction direction)
        => direction switch
        {
            Direction.Up => (0, -1),
            Direction.Right => (1, 0),
            Direction.Down => (0, 1),
            Direction.Left => (-1, 0),
            _ => (0, 0),
        };
}
