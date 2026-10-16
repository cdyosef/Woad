namespace Woad.Core.Grid;

public enum TileKind
{
    Floor,

    /// <summary>A solid block or pillar. Blocks movement and line of sight.</summary>
    Wall,

    /// <summary>A hole in the floor. Only flying monsters can cross it; it doesn't block sight.</summary>
    Pit,

    /// <summary>A treasure chest. Blocks movement; opened from an adjacent tile.</summary>
    Chest,

    /// <summary>Stairs to the next floor. Stand on them to descend.</summary>
    StairsDown,

    /// <summary>Stairs back up to the floor above, in each lower floor's arrival room. Stand on them to climb.</summary>
    StairsUp,

    /// <summary>The way out of the dungeon. Opens once the boss in its room is defeated.</summary>
    Exit,

    /// <summary>A pot. Blocks movement until a hero beside it smashes it, finding coins, a heart or nothing.</summary>
    Pot,

    /// <summary>A cracked block: like a wall until a bomb's blast breaks it into floor.</summary>
    CrackedWall,

    /// <summary>Rough ground (rubble): walkable, but each step onto it costs 2.</summary>
    Rubble,

    /// <summary>Shallow water: wade through it at 2 steps a tile.</summary>
    ShallowWater,

    /// <summary>
    /// Deep water: heroes swim it at 2 steps a tile, and must spend a Rumble die to stay afloat if their
    /// turn ends in it, or drown back to dry footing. Only flyers (passing over) and swimmers enter it.
    /// </summary>
    DeepWater,

    /// <summary>
    /// A locked iron gate in a wall that divides a room: blocks movement (not sight) until it's opened
    /// with a key or a picked lock.
    /// </summary>
    Gate,

    /// <summary>
    /// Solid rock outside a room's shape (round chambers, crosses, L-shaped rooms): like a wall, but
    /// not part of the room. Blocks movement and line of sight.
    /// </summary>
    Rock,
}

public static class TileKindExtensions
{
    /// <summary>Whether a figure can stand here. Deep water only counts for flyers; heroes swim it on purpose (see HeroPathSearch).</summary>
    public static bool IsWalkable(this TileKind tile, bool flying = false) =>
        tile is TileKind.Floor or TileKind.StairsDown or TileKind.StairsUp or TileKind.Exit or TileKind.Rubble or TileKind.ShallowWater
        || (flying && tile is TileKind.Pit or TileKind.DeepWater);

    /// <summary>Steps it costs to move onto this tile: 2 for rough ground and water (flyers pay 1 everywhere).</summary>
    public static int MoveCost(this TileKind tile, bool flying = false) =>
        !flying && tile is TileKind.Rubble or TileKind.ShallowWater or TileKind.DeepWater ? 2 : 1;

    public static bool BlocksSight(this TileKind tile) => tile is TileKind.Wall or TileKind.CrackedWall or TileKind.Rock;
}
