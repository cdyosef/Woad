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
}

public static class TileKindExtensions
{
    public static bool IsWalkable(this TileKind tile, bool flying = false) =>
        tile == TileKind.Floor || (flying && tile == TileKind.Pit);

    public static bool BlocksSight(this TileKind tile) => tile == TileKind.Wall;
}
