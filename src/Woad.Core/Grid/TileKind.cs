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

    /// <summary>The way out of the dungeon. Opens once the boss in its room is defeated.</summary>
    Exit,
}

public static class TileKindExtensions
{
    public static bool IsWalkable(this TileKind tile, bool flying = false) =>
        tile is TileKind.Floor or TileKind.StairsDown or TileKind.Exit || (flying && tile == TileKind.Pit);

    public static bool BlocksSight(this TileKind tile) => tile == TileKind.Wall;
}
