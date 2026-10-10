using Woad.Core.Content;
using Woad.Core.Grid;

namespace Woad.Core.Dungeon;

public sealed record MonsterSpawn(GridPosition Position, MonsterDefinition Monster);

/// <summary>
/// A handcrafted 9x9 room written as text, one row per line:
/// <c>.</c> floor, <c>#</c> wall, <c>~</c> pit, <c>C</c> chest, <c>&gt;</c> stairs down,
/// <c>*</c> exit, <c>1</c>-<c>4</c> hero start for that seat, <c>M</c> a spot the dungeon
/// generator may put a monster, <c>&amp;</c> where the boss stands, and any monster's glyph
/// (e.g. <c>s</c> for a slime) to place that exact monster.
/// </summary>
public sealed class RoomTemplate
{
    /// <summary>Characters with a fixed meaning; monster glyphs can't use them.</summary>
    public const string ReservedCharacters = ".#~C>*M&0123456789";

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

    /// <exception cref="FormatException">The text isn't a 9x9 grid of known characters.</exception>
    public static RoomTemplate Parse(string text, ContentCatalog content) =>
        Parse(text.Split('\n'), content.FindMonsterByGlyph);

    /// <exception cref="FormatException">The rows aren't a 9x9 grid of known characters.</exception>
    public static RoomTemplate Parse(IEnumerable<string> lines, Func<char, MonsterDefinition?> findMonster)
    {
        string[] rows = lines.Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();
        if (rows.Length != RoomGrid.Size || rows.Any(row => row.Length != RoomGrid.Size))
        {
            throw new FormatException($"A room template must be {RoomGrid.Size} rows of {RoomGrid.Size} characters.");
        }

        var tiles = new TileKind[RoomGrid.Size * RoomGrid.Size];
        var monsters = new List<MonsterSpawn>();
        var heroStarts = new Dictionary<int, GridPosition>();
        var spawnPoints = new List<GridPosition>();
        GridPosition? bossSpawn = null;

        foreach (GridPosition position in RoomGrid.AllPositions())
        {
            char glyph = rows[position.Y][position.X];
            tiles[position.Y * RoomGrid.Size + position.X] = glyph switch
            {
                '#' => TileKind.Wall,
                '~' => TileKind.Pit,
                'C' => TileKind.Chest,
                '>' => TileKind.StairsDown,
                '*' => TileKind.Exit,
                _ => TileKind.Floor,
            };

            switch (glyph)
            {
                case '.' or '#' or '~' or 'C' or '>' or '*':
                    break;
                case >= '1' and <= '4':
                    heroStarts[glyph - '1'] = position;
                    break;
                case 'M':
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

        return new RoomTemplate(new RoomLayout(tiles), monsters, heroStarts, spawnPoints, bossSpawn);
    }

    /// <summary>The same room turned clockwise by quarter turns, after an optional left-right mirror.</summary>
    public RoomTemplate Transformed(int quarterTurns, bool mirrored)
    {
        GridPosition Map(GridPosition position)
        {
            int x = mirrored ? RoomGrid.Size - 1 - position.X : position.X;
            int y = position.Y;
            for (int turn = 0; turn < ((quarterTurns % 4) + 4) % 4; turn++)
            {
                (x, y) = (RoomGrid.Size - 1 - y, x);
            }

            return new GridPosition(x, y);
        }

        var tiles = new TileKind[RoomGrid.Size * RoomGrid.Size];
        foreach (GridPosition position in RoomGrid.AllPositions())
        {
            GridPosition target = Map(position);
            tiles[target.Y * RoomGrid.Size + target.X] = Layout[position];
        }

        return new RoomTemplate(
            new RoomLayout(tiles),
            Monsters.Select(spawn => spawn with { Position = Map(spawn.Position) }).ToList(),
            HeroStarts.ToDictionary(start => start.Key, start => Map(start.Value)),
            SpawnPoints.Select(Map).ToList(),
            BossSpawn is { } boss ? Map(boss) : null);
    }

    /// <summary>The same room with a different set of monsters.</summary>
    public RoomTemplate WithMonsters(IEnumerable<MonsterSpawn> monsters) =>
        new(Layout, [.. monsters], HeroStarts, SpawnPoints, BossSpawn);

    public IEnumerable<GridPosition> TilesOf(TileKind kind) =>
        RoomGrid.AllPositions().Where(position => Layout[position] == kind);
}
