namespace Woad.Core.Grid;

/// <summary>A tile coordinate inside a room. (0, 0) is the top-left tile.</summary>
public readonly record struct GridPosition(int X, int Y)
{
    private static readonly Direction[] Directions = [Direction.North, Direction.East, Direction.South, Direction.West];

    public bool IsInRoom => X is >= 0 and < RoomGrid.Size && Y is >= 0 and < RoomGrid.Size;

    public int ManhattanDistanceTo(GridPosition other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);

    /// <summary>Distance counting diagonal steps as 1; used for area effects such as bomb blasts.</summary>
    public int ChebyshevDistanceTo(GridPosition other) => Math.Max(Math.Abs(X - other.X), Math.Abs(Y - other.Y));

    public GridPosition Step(Direction direction) => direction switch
    {
        Direction.North => this with { Y = Y - 1 },
        Direction.East => this with { X = X + 1 },
        Direction.South => this with { Y = Y + 1 },
        Direction.West => this with { X = X - 1 },
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };

    /// <summary>Up, right, down, left. Movement is orthogonal only; heroes can also attack diagonally.</summary>
    public IEnumerable<GridPosition> OrthogonalNeighbors()
    {
        foreach (Direction direction in Directions)
        {
            yield return Step(direction);
        }
    }

    /// <summary>The eight surrounding tiles: orthogonal neighbors first, then diagonals.</summary>
    public IEnumerable<GridPosition> SurroundingTiles()
    {
        foreach (GridPosition neighbor in OrthogonalNeighbors())
        {
            yield return neighbor;
        }

        yield return new GridPosition(X + 1, Y - 1);
        yield return new GridPosition(X + 1, Y + 1);
        yield return new GridPosition(X - 1, Y + 1);
        yield return new GridPosition(X - 1, Y - 1);
    }

    public override string ToString() => $"({X}, {Y})";
}
