using Woad.Core.Content;
using Woad.Core.Grid;

namespace Woad.Core.Dungeon;

public sealed record MonsterSpawn(GridPosition Position, MonsterDefinition Monster);

/// <summary>
/// A handcrafted 9x9 room written as text, one row per line:
/// <c>.</c> floor, <c>#</c> wall, <c>X</c> cracked wall, <c>~</c> pit, <c>C</c> chest, <c>P</c> pot, <c>&gt;</c> stairs down,
/// <c>*</c> exit, <c>1</c>-<c>4</c> hero start for that seat, <c>M</c> a spot the dungeon
/// generator may put a monster (<c>%</c>: in deep water, for swimmers), <c>&amp;</c> where the boss stands, and any monster's glyph
/// (e.g. <c>s</c> for a slime) to place that exact monster.
/// </summary>
public sealed class RoomTemplate
{
    /// <summary>Characters with a fixed meaning; monster glyphs can't use them.</summary>
    public const string ReservedCharacters = ".#X~CP>*M%&_0123456789";

    private RoomTemplate(
        RoomLayout layout,
        IReadOnlyList<MonsterSpawn> monsters,
        IReadOnlyDictionary<int, GridPosition> heroStarts,
        IReadOnlyList<GridPosition> spawnPoints,
        GridPosition? bossSpawn)
    {
        Layout = layout;
        Monsters = monsters;
        HeroStarts = heroStarts;
        SpawnPoints = spawnPoints;
        BossSpawn = bossSpawn;
    }

    public RoomLayout Layout { get; }

    /// <summary>Monsters placed in the room when a run starts.</summary>
    public IReadOnlyList<MonsterSpawn> Monsters { get; }

    /// <summary>Starting tile by seat index (0-based; the template's <c>1</c> is seat 0).</summary>
    public IReadOnlyDictionary<int, GridPosition> HeroStarts { get; }

    /// <summary>Tiles marked <c>M</c>, where the generator may place monsters.</summary>
    public IReadOnlyList<GridPosition> SpawnPoints { get; }

    public GridPosition? BossSpawn { get; }

    /// <exception cref="FormatException">The text isn't a rectangle of known characters.</exception>
    public static RoomTemplate Parse(string text, ContentCatalog content) =>
        Parse(text.Split('\n'), content.FindMonsterByGlyph);

    /// <exception cref="FormatException">The rows aren't a rectangle (up to <see cref="RoomGrid.MaxSize"/> a side) of known characters.</exception>
    public static RoomTemplate Parse(IEnumerable<string> lines, Func<char, MonsterDefinition?> findMonster)
    {
        string[] rows = lines.Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();
        int width = rows.Length > 0 ? rows[0].Length : 0;
        if (rows.Length is < 1 or > RoomGrid.MaxSize || width is < 1 or > RoomGrid.MaxSize || rows.Any(row => row.Length != width))
        {
            throw new FormatException($"A room template must be a rectangle of rows the same length, up to {RoomGrid.MaxSize} each way.");
        }

        int height = rows.Length;
        var tiles = new TileKind[width * height];
        var monsters = new List<MonsterSpawn>();
        var heroStarts = new Dictionary<int, GridPosition>();
        var spawnPoints = new List<GridPosition>();
        GridPosition? bossSpawn = null;

        foreach (GridPosition position in RoomGrid.AllPositions(width, height))
        {
            char glyph = rows[position.Y][position.X];
            tiles[position.Y * width + position.X] = glyph switch
            {
                '#' => TileKind.Wall,
                '_' => TileKind.Rock,
                'X' => TileKind.CrackedWall,
                '~' => TileKind.Pit,
                'C' => TileKind.Chest,
                'P' => TileKind.Pot,
                '>' => TileKind.StairsDown,
                '<' => TileKind.StairsUp,
                ':' => TileKind.Rubble,
                '^' => TileKind.Brazier,
                ',' => TileKind.ShallowWater,
                '=' or '%' => TileKind.DeepWater,
                '*' => TileKind.Exit,
                _ => TileKind.Floor,
            };

            switch (glyph)
            {
                case '_' or '.' or '#' or 'X' or '~' or 'C' or 'P' or '>' or '<' or '*' or ':' or ',' or '=' or '^':
                    break;
                case >= '1' and <= '4':
                    heroStarts[glyph - '1'] = position;
                    break;
                case 'M' or '%':
                    spawnPoints.Add(position);
                    break;
                case '&':
                    bossSpawn = position;
                    break;
                default:
                    MonsterDefinition monster = findMonster(glyph)
                        ?? throw new FormatException($"Unknown room template character '{glyph}' at {position}.");
                    monsters.Add(new MonsterSpawn(position, monster));
                    break;
            }
        }

        return new RoomTemplate(new RoomLayout(width, height, tiles), monsters, heroStarts, spawnPoints, bossSpawn);
    }

    /// <summary>The same room turned clockwise by quarter turns, after an optional left-right mirror (an odd number of turns swaps width and height).</summary>
    public RoomTemplate Transformed(int quarterTurns, bool mirrored)
    {
        int turns = ((quarterTurns % 4) + 4) % 4;
        int width = turns % 2 == 0 ? Layout.Width : Layout.Height;
        int height = turns % 2 == 0 ? Layout.Height : Layout.Width;
        GridPosition Map(GridPosition position)
        {
            int x = mirrored ? Layout.Width - 1 - position.X : position.X;
            int y = position.Y;
            int w = Layout.Width, h = Layout.Height;
            for (int turn = 0; turn < turns; turn++)
            {
                (x, y) = (h - 1 - y, x);
                (w, h) = (h, w);
            }

            return new GridPosition(x, y);
        }

        var tiles = new TileKind[width * height];
        foreach (GridPosition position in Layout.Positions)
        {
            GridPosition target = Map(position);
            tiles[target.Y * width + target.X] = Layout[position];
        }

        return new RoomTemplate(
            new RoomLayout(width, height, tiles),
            Monsters.Select(spawn => spawn with { Position = Map(spawn.Position) }).ToList(),
            HeroStarts.ToDictionary(start => start.Key, start => Map(start.Value)),
            SpawnPoints.Select(Map).ToList(),
            BossSpawn is { } boss ? Map(boss) : null);
    }

    /// <summary>The same room with one tile changed (e.g. a wall turned into a cracked wall).</summary>
    public RoomTemplate WithTile(GridPosition position, TileKind tile)
    {
        TileKind[] tiles = Layout.Positions.Select(at => at == position ? tile : Layout[at]).ToArray();
        return new RoomTemplate(new RoomLayout(Layout.Width, Layout.Height, tiles), Monsters, HeroStarts, SpawnPoints, BossSpawn);
    }

    /// <summary>The same room with a different set of spots the generator may put monsters.</summary>
    public RoomTemplate WithSpawnPoints(IEnumerable<GridPosition> spawnPoints) =>
        new(Layout, Monsters, HeroStarts, [.. spawnPoints], BossSpawn);

    /// <summary>The same room with a different set of monsters.</summary>
    public RoomTemplate WithMonsters(IEnumerable<MonsterSpawn> monsters) =>
        new(Layout, [.. monsters], HeroStarts, SpawnPoints, BossSpawn);

    public IEnumerable<GridPosition> TilesOf(TileKind kind) =>
        Layout.Positions.Where(position => Layout[position] == kind);
}
