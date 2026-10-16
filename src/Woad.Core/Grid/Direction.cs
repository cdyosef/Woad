namespace Woad.Core.Grid;

/// <summary>The four sides of a room. A door can sit anywhere along its side (see Room.DoorTile).</summary>
public enum Direction
{
    North,
    East,
    South,
    West,
}

public static class DirectionExtensions
{
    public static Direction Opposite(this Direction direction) => direction switch
    {
        Direction.North => Direction.South,
        Direction.East => Direction.West,
        Direction.South => Direction.North,
        Direction.West => Direction.East,
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };

    /// <summary>The middle of this side in a standard 9x9 room, where its door sits by default (rooms say where theirs are).</summary>
    public static GridPosition DoorTile(this Direction direction) => direction switch
    {
        Direction.North => new GridPosition(RoomGrid.Center, 0),
        Direction.East => new GridPosition(RoomGrid.Size - 1, RoomGrid.Center),
        Direction.South => new GridPosition(RoomGrid.Center, RoomGrid.Size - 1),
        Direction.West => new GridPosition(0, RoomGrid.Center),
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };
}
