namespace Woad.Core.Grid;

/// <summary>Every room in a dungeon is a square grid of this size.</summary>
public static class RoomGrid
{
    public const int Size = 9;

    /// <summary>Index of the middle row/column, where doors sit on each edge.</summary>
    public const int Center = Size / 2;

    /// <summary>Every tile in a room, row by row from the top-left.</summary>
    public static IEnumerable<GridPosition> AllPositions()
    {
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                yield return new GridPosition(x, y);
            }
        }
    }
}
