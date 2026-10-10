using Woad.Core.Content;
using Woad.Core.Grid;

namespace Woad.Core.Dungeon;

public sealed record MonsterSpawn(GridPosition Position, MonsterDefinition Monster);

/// <summary>
/// A handcrafted 9x9 room written as text, one row per line:
/// <c>.</c> floor, <c>#</c> wall, <c>~</c> pit, <c>C</c> chest, <c>1</c>-<c>4</c> hero start
/// for that seat, and any monster's glyph (e.g. <c>s</c> for a slime) to place that monster.
/// </summary>
public sealed class RoomTemplate
{
    private RoomTemplate(RoomLayout layout, IReadOnlyList<MonsterSpawn> monsters, IReadOnlyDictionary<int, GridPosition> heroStarts)
    {
        Layout = layout;
        Monsters = monsters;
        HeroStarts = heroStarts;
    }

    public RoomLayout Layout { get; }

    public IReadOnlyList<MonsterSpawn> Monsters { get; }

    /// <summary>Starting tile by seat index (0-based; the template's <c>1</c> is seat 0).</summary>
    public IReadOnlyDictionary<int, GridPosition> HeroStarts { get; }

    /// <exception cref="FormatException">The text isn't a 9x9 grid of known characters.</exception>
    public static RoomTemplate Parse(string text, ContentCatalog content)
    {
        string[] rows = text
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();

        if (rows.Length != RoomGrid.Size || rows.Any(row => row.Length != RoomGrid.Size))
        {
            throw new FormatException($"A room template must be {RoomGrid.Size} rows of {RoomGrid.Size} characters.");
        }

        var tiles = new TileKind[RoomGrid.Size * RoomGrid.Size];
        var monsters = new List<MonsterSpawn>();
        var heroStarts = new Dictionary<int, GridPosition>();

        for (int y = 0; y < RoomGrid.Size; y++)
        {
            for (int x = 0; x < RoomGrid.Size; x++)
            {
                char glyph = rows[y][x];
                var position = new GridPosition(x, y);
                tiles[y * RoomGrid.Size + x] = glyph switch
                {
                    '#' => TileKind.Wall,
                    '~' => TileKind.Pit,
                    'C' => TileKind.Chest,
                    _ => TileKind.Floor,
                };

                if (glyph is >= '1' and <= '4')
                {
                    heroStarts[glyph - '1'] = position;
                }
                else if (glyph is not ('.' or '#' or '~' or 'C'))
                {
                    MonsterDefinition monster = content.FindMonsterByGlyph(glyph)
                        ?? throw new FormatException($"Unknown room template character '{glyph}' at {position}.");
                    monsters.Add(new MonsterSpawn(position, monster));
                }
            }
        }

        return new RoomTemplate(new RoomLayout(tiles), monsters, heroStarts);
    }
}
