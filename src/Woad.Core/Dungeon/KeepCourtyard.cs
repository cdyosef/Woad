using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Rules;

namespace Woad.Core.Dungeon;

/// <summary>What a tile of the keep's courtyard looks like (the rules only see floor, water, trees and the way out).</summary>
public enum CourtyardGround
{
    /// <summary>The stone apron at the foot of the keep's wall.</summary>
    Flagstone,
    Grass,

    /// <summary>The dirt road up to the drawbridge.</summary>
    Road,

    /// <summary>The drawbridge's planks over the moat.</summary>
    Bridge,
    Moat,

    /// <summary>A tree or bush: solid.</summary>
    Tree,

    /// <summary>The road on out, back to the Woarld Map.</summary>
    WayOut,
}

/// <summary>
/// The Emerald Keep's way in, the same every time: outside the keep, at the foot of its front wall (the top
/// edge of the room, the wall drawn rising above it), its great door in the middle. A stone apron before the
/// wall, where the Emerald Knight and his guards stand; the moat right across, a drawbridge four wide over
/// it; then grass and trees, a dirt road down to the way out, four wide, at the bottom edge. The great door
/// leads into the entrance hall, the first room inside the keep.
/// </summary>
public static class KeepCourtyard
{
    /// <summary>The courtyard's room id.</summary>
    public const string Id = "courtyard";

    /// <summary>
    /// The courtyard, row by row from the foot of the keep's wall: <c>.</c> flagstones, <c>,</c> grass,
    /// <c>:</c> road, <c>b</c> bridge, <c>=</c> moat, <c>T</c> a tree, <c>*</c> the way out; <c>D</c> the
    /// threshold of the great door (flagstone).
    /// </summary>
    private static readonly string[] Rows =
    [
        "..........D..........",
        ".....................",
        ",...................,",
        "=========bbbb========",
        "=========bbbb========",
        ",,,,,,,,,::::,,,,,,,,",
        ",T,,,,,,,::::,,,,,T,,",
        ",,,,T,,,,::::,,,,,,,,",
        ",,,,,,,,,::::,,,T,,,,",
        "T,,,,,,,,::::,,,,,,,T",
        ",,,T,,,,,::::,,,,,,,,",
        ",,,,,,,,,::::,,,,T,,,",
        ",,,,,,,,,****,,,,,,,,",
    ];

    public static int Width => Rows[0].Length;

    public static int Height => Rows.Length;

    /// <summary>The great door's threshold, in the middle of the top edge.</summary>
    public static GridPosition Door => new(Rows[0].IndexOf('D'), 0);

    /// <summary>What the tile looks like (see <see cref="CourtyardGround"/>).</summary>
    public static CourtyardGround GroundAt(GridPosition tile) => Rows[tile.Y][tile.X] switch
    {
        '.' or 'D' => CourtyardGround.Flagstone,
        ':' => CourtyardGround.Road,
        'b' => CourtyardGround.Bridge,
        '=' => CourtyardGround.Moat,
        'T' => CourtyardGround.Tree,
        '*' => CourtyardGround.WayOut,
        _ => CourtyardGround.Grass,
    };

    /// <summary>
    /// The courtyard's layout for the rules: floor, the moat's deep water, trees as solid rock, and the way
    /// out; the party starting on the road just inside it, in rows of four, and the gate's guard on the apron:
    /// the knight before the great door, and two guards for every three heroes (rounded up) around him.
    /// </summary>
    public static RoomTemplate Template(GateGuard gate, int heroes, ContentCatalog content)
    {
        // In the room templates' own letters: floor, deep water, solid rock, the way out.
        string text = string.Join('\n', Enumerable.Range(0, Height).Select(y => new string([.. Enumerable.Range(0, Width).Select(x => GroundAt(new GridPosition(x, y)) switch
        {
            CourtyardGround.Moat => '=',
            CourtyardGround.Tree => '_',
            CourtyardGround.WayOut => '*',
            _ => '.',
        })])));

        // The party on the road: four to a row, back from the way out.
        List<GridPosition> wayOut = [.. RoomGrid.AllPositions(Width, Height).Where(tile => GroundAt(tile) == CourtyardGround.WayOut).OrderBy(tile => tile.X)];
        var starts = new Dictionary<int, GridPosition>();
        for (int i = 0; i < GameRules.MaxHeroes; i++)
        {
            GridPosition exit = wayOut[i % wayOut.Count];
            starts[i] = exit with { Y = exit.Y - 1 - i / wayOut.Count };
        }

        // The knight right before the great door; his guards in a line either side of him, then a second line.
        GridPosition knight = Door with { Y = 1 };
        int guards = 2 * ((heroes + 2) / 3);
        (int Dx, int Dy)[] posts = [(-2, 1), (2, 1), (-4, 0), (4, 0), (-6, 1), (6, 1), (-3, 2), (3, 2), (-8, 0), (8, 0)];
        var monsters = new List<MonsterSpawn> { new(knight, content.GetMonster(gate.Knight)) };
        monsters.AddRange(posts.Take(guards).Select(post => new MonsterSpawn(new GridPosition(knight.X + post.Dx, knight.Y + post.Dy), content.GetMonster(gate.Guard))));
        return RoomTemplate.Parse(text, content).WithHeroStarts(starts).WithMonsters(monsters);
    }
}
