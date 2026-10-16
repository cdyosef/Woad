namespace Woad.Core.Grid;

/// <summary>
/// Room sizes. Rooms are rectangles of any size up to <see cref="MaxSize"/> a side; most are the
/// standard <see cref="Size"/> square, whose doors sit in the middle of each edge.
/// </summary>
public static class RoomGrid
{
    /// <summary>The standard room: 9 by 9.</summary>
    public const int Size = 9;

    /// <summary>Index of the middle row/column of a standard room.</summary>
    public const int Center = Size / 2;

    /// <summary>No room is wider or taller than this (a boss arena spanning two map cells each way).</summary>
    public const int MaxSize = 2 * Size;

    /// <summary>Every tile in a standard room, row by row from the top-left.</summary>
    public static IEnumerable<GridPosition> AllPositions() => AllPositions(Size, Size);

    /// <summary>Every tile in a room of this size, row by row from the top-left.</summary>
    public static IEnumerable<GridPosition> AllPositions(int width, int height)
    {
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                yield return new GridPosition(x, y);
            }
        }
    }
}
