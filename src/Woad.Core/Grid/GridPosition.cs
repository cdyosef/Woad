namespace Woad.Core.Grid;

/// <summary>A tile coordinate inside a room. (0, 0) is the top-left tile.</summary>
public readonly record struct GridPosition(int X, int Y)
{
    public bool IsInRoom => X is >= 0 and < RoomGrid.Size && Y is >= 0 and < RoomGrid.Size;

    public int ManhattanDistanceTo(GridPosition other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);

    /// <summary>Up, right, down, left. Movement and melee are orthogonal only.</summary>
    public IEnumerable<GridPosition> OrthogonalNeighbors()
    {
        yield return this with { Y = Y - 1 };
        yield return this with { X = X + 1 };
        yield return this with { Y = Y + 1 };
        yield return this with { X = X - 1 };
    }

    public override string ToString() => $"({X}, {Y})";
}
