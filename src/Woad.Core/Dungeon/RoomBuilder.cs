using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Randomness;

namespace Woad.Core.Dungeon;

/// <summary>The outline of a built room; outside it is solid rock.</summary>
internal enum RoomShape
{
    /// <summary>The whole rectangle.</summary>
    Plain,

    /// <summary>An oval chamber touching the middle of each edge.</summary>
    Round,

    /// <summary>Two broad bands crossing in the middle.</summary>
    Cross,

    /// <summary>The rectangle with one quarter missing.</summary>
    Ell,

    /// <summary>A long hall with a row of pillars down the middle.</summary>
    Gallery,
}

/// <summary>
/// Builds room layouts of any size and shape (the theme's hand-made layouts are all 9x9): carves the
/// shape out of rock, then adds what the room's role needs, as the hand-made layouts have it: monster
/// spawn points, chests, pots, pillars, the stairs, and for a boss arena the boss and the exit. Edges are
/// left clear for doors, and every open tile stays reachable from every other.
/// </summary>
internal static class RoomBuilder
{
    public static RoomTemplate Build(int width, int height, RoomShape shape, RoomRole role, IRandomSource random)
    {
        char[,] grid = Carve(width, height, shape);
        bool Inside(int x, int y) => x >= 1 && y >= 1 && x < width - 1 && y < height - 1;
        List<GridPosition> Interior() =>
            RoomGrid.AllPositions(width, height).Where(tile => Inside(tile.X, tile.Y) && grid[tile.X, tile.Y] == '.').ToList();

        GridPosition center = new(width / 2, height / 2);
        int area = RoomGrid.AllPositions(width, height).Count(tile => grid[tile.X, tile.Y] != '_');

        // What the role must have first, so nothing else takes its place.
        if (role == RoomRole.Boss)
        {
            GridPosition boss = Interior().MinBy(tile => tile.ManhattanDistanceTo(center));
            grid[boss.X, boss.Y] = '&';
            GridPosition exit = Interior().Where(tile => tile.ChebyshevDistanceTo(boss) >= 3).MaxBy(tile => (tile.Y, -Math.Abs(tile.X - center.X)));
            grid[exit.X, exit.Y] = '*';
        }

        if (role == RoomRole.Stairs)
        {
            List<GridPosition> far = Interior().OrderByDescending(tile => tile.ManhattanDistanceTo(center)).Take(6).ToList();
            GridPosition stairs = random.Pick(far);
            grid[stairs.X, stairs.Y] = '>';
        }

        // Pillars and furniture, each kept only if the room stays in one piece.
        int pillars = shape == RoomShape.Gallery ? 0 : area >= 100 ? random.NextInt(2, 6) : area >= 60 ? random.NextInt(0, 3) : 0;
        Scatter(grid, Interior, '#', pillars, random, awayFrom: center);
        int chests = role switch
        {
            RoomRole.Treasure or RoomRole.Secret => random.NextInt(1, 3),
            RoomRole.Combat or RoomRole.Stairs => random.NextInt(0, 100) < 25 ? 1 : 0,
            _ => 0,
        };
        Scatter(grid, Interior, 'C', chests, random);
        Scatter(grid, Interior, 'P', role == RoomRole.Boss ? 0 : random.NextInt(0, area >= 60 ? 3 : 2), random);

        int spawns = role switch
        {
            RoomRole.Combat or RoomRole.Stairs => Math.Max(2, area / 14),
            RoomRole.Treasure or RoomRole.Secret => 1,
            RoomRole.Boss => Math.Max(2, area / 30),
            _ => 0,
        };
        List<GridPosition> open = Interior();
        random.Shuffle(open);
        foreach (GridPosition tile in open.Take(spawns))
        {
            grid[tile.X, tile.Y] = 'M';
        }

        string[] rows = Enumerable.Range(0, height)
            .Select(y => new string(Enumerable.Range(0, width).Select(x => grid[x, y]).ToArray()))
            .ToArray();
        return RoomTemplate.Parse(rows, _ => null);
    }

    /// <summary>The room's floor ('.') inside its outline, rock ('_') outside.</summary>
    private static char[,] Carve(int width, int height, RoomShape shape)
    {
        var grid = new char[width, height];
        double cx = (width - 1) / 2.0, cy = (height - 1) / 2.0;
        int bandX = Math.Max(3, (width / 3) | 1), bandY = Math.Max(3, (height / 3) | 1);
        foreach (GridPosition tile in RoomGrid.AllPositions(width, height))
        {
            double dx = (tile.X - cx) / (cx + 0.5), dy = (tile.Y - cy) / (cy + 0.5);
            bool open = shape switch
            {
                RoomShape.Round => dx * dx + dy * dy <= 1.0,
                RoomShape.Cross => Math.Abs(tile.X - cx) <= bandX / 2 || Math.Abs(tile.Y - cy) <= bandY / 2,
                RoomShape.Ell => !(tile.X > cx && tile.Y < cy),
                _ => true,
            };
            grid[tile.X, tile.Y] = open ? '.' : '_';
        }

        // A gallery's pillars stand every other tile down its long middle line, clear of the ends.
        if (shape == RoomShape.Gallery && Math.Min(width, height) >= 5)
        {
            bool wide = width >= height;
            int length = wide ? width : height;
            for (int i = 2; i < length - 2; i += 2)
            {
                (int x, int y) = wide ? (i, height / 2) : (width / 2, i);
                grid[x, y] = '#';
            }
        }

        return grid;
    }

    /// <summary>Puts up to <paramref name="count"/> of <paramref name="glyph"/> on interior floor, skipping any spot that would cut the room up.</summary>
    private static void Scatter(char[,] grid, Func<List<GridPosition>> interior, char glyph, int count, IRandomSource random, GridPosition? awayFrom = null)
    {
        for (int placed = 0, tries = 0; placed < count && tries < 40; tries++)
        {
            List<GridPosition> spots = interior()
                .Where(tile => awayFrom is not { } avoid || tile.ChebyshevDistanceTo(avoid) > 1)
                .ToList();
            if (spots.Count == 0)
            {
                return;
            }

            GridPosition tile = random.Pick(spots);
            grid[tile.X, tile.Y] = glyph;
            if (StaysWhole(grid))
            {
                placed++;
            }
            else
            {
                grid[tile.X, tile.Y] = '.';
            }
        }
    }

    /// <summary>Every tile a figure could stand on reaches every other, and every chest and pot has one beside it.</summary>
    private static bool StaysWhole(char[,] grid)
    {
        int width = grid.GetLength(0), height = grid.GetLength(1);
        bool Open(GridPosition tile) => tile.X >= 0 && tile.Y >= 0 && tile.X < width && tile.Y < height && grid[tile.X, tile.Y] is '.' or 'M' or '>' or '*' or '&';
        List<GridPosition> open = RoomGrid.AllPositions(width, height).Where(Open).ToList();
        if (open.Count == 0)
        {
            return false;
        }

        IReadOnlyDictionary<GridPosition, int> reached = PathSearch.Run(open[0], Open).Distances;
        return reached.Count == open.Count
               && RoomGrid.AllPositions(width, height)
                   .Where(tile => grid[tile.X, tile.Y] is 'C' or 'P')
                   .All(item => item.OrthogonalNeighbors().Any(reached.ContainsKey));
    }
}
