namespace Woad.Core.Grid;

/// <summary>The fixed terrain of a 9x9 room.</summary>
public sealed class RoomLayout
{
    private readonly TileKind[] _tiles;

    public RoomLayout(IReadOnlyList<TileKind> tiles)
    {
        if (tiles.Count != RoomGrid.Size * RoomGrid.Size)
        {
            throw new ArgumentException($"A room layout needs exactly {RoomGrid.Size * RoomGrid.Size} tiles.", nameof(tiles));
        }

        _tiles = [.. tiles];
    }

    public static RoomLayout Empty { get; } = new(Enumerable.Repeat(TileKind.Floor, RoomGrid.Size * RoomGrid.Size).ToArray());

    /// <summary>Tiles outside the room read as walls, so callers never need a bounds check.</summary>
    public TileKind this[GridPosition position] =>
        position.IsInRoom ? _tiles[position.Y * RoomGrid.Size + position.X] : TileKind.Wall;

    /// <summary>A separate copy, so a run can change its rooms (e.g. smash pots) without touching the template.</summary>
    internal RoomLayout Copy() => new(_tiles);

    internal void Set(GridPosition position, TileKind tile) => _tiles[position.Y * RoomGrid.Size + position.X] = tile;
}
