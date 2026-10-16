namespace Woad.Core.Grid;

/// <summary>The fixed terrain of a room: <see cref="Width"/> by <see cref="Height"/> tiles.</summary>
public sealed class RoomLayout
{
    private readonly TileKind[] _tiles;

    /// <summary>A standard 9x9 layout.</summary>
    public RoomLayout(IReadOnlyList<TileKind> tiles)
        : this(RoomGrid.Size, RoomGrid.Size, tiles)
    {
    }

    public RoomLayout(int width, int height, IReadOnlyList<TileKind> tiles)
    {
        if (width is < 1 or > RoomGrid.MaxSize || height is < 1 or > RoomGrid.MaxSize)
        {
            throw new ArgumentException($"A room is 1 to {RoomGrid.MaxSize} tiles each way, not {width}x{height}.");
        }

        if (tiles.Count != width * height)
        {
            throw new ArgumentException($"A {width}x{height} room layout needs exactly {width * height} tiles.", nameof(tiles));
        }

        Width = width;
        Height = height;
        _tiles = [.. tiles];
    }

    public static RoomLayout Empty { get; } = new(Enumerable.Repeat(TileKind.Floor, RoomGrid.Size * RoomGrid.Size).ToArray());

    public int Width { get; }

    public int Height { get; }

    /// <summary>The middle tile (rounding down).</summary>
    public GridPosition Center => new(Width / 2, Height / 2);

    /// <summary>Every tile, row by row from the top-left.</summary>
    public IEnumerable<GridPosition> Positions => RoomGrid.AllPositions(Width, Height);

    /// <summary>Tiles outside the room read as walls, so callers never need a bounds check.</summary>
    public TileKind this[GridPosition position] =>
        Contains(position) ? _tiles[position.Y * Width + position.X] : TileKind.Wall;

    public bool Contains(GridPosition position) => position.X >= 0 && position.Y >= 0 && position.X < Width && position.Y < Height;

    /// <summary>The middle of an edge: where a door on that side sits unless the room says otherwise.</summary>
    public GridPosition MiddleOf(Direction side) => side switch
    {
        Direction.North => new GridPosition(Width / 2, 0),
        Direction.East => new GridPosition(Width - 1, Height / 2),
        Direction.South => new GridPosition(Width / 2, Height - 1),
        Direction.West => new GridPosition(0, Height / 2),
        _ => throw new ArgumentOutOfRangeException(nameof(side)),
    };

    /// <summary>The tile on <paramref name="side"/>'s edge at <paramref name="offset"/> along it (from the top or left).</summary>
    public GridPosition EdgeTile(Direction side, int offset) => side switch
    {
        Direction.North => new GridPosition(offset, 0),
        Direction.East => new GridPosition(Width - 1, offset),
        Direction.South => new GridPosition(offset, Height - 1),
        Direction.West => new GridPosition(0, offset),
        _ => throw new ArgumentOutOfRangeException(nameof(side)),
    };

    /// <summary>How many tiles long <paramref name="side"/>'s edge is.</summary>
    public int EdgeLength(Direction side) => side is Direction.North or Direction.South ? Width : Height;

    /// <summary>A separate copy, so a run can change its rooms (e.g. smash pots) without touching the template.</summary>
    internal RoomLayout Copy() => new(Width, Height, _tiles);

    internal void Set(GridPosition position, TileKind tile) => _tiles[position.Y * Width + position.X] = tile;
}
