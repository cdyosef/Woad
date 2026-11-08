using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Randomness;
using Woad.Core.Rules;

namespace Woad.Core.Dungeon;

/// <summary>
/// Wall sconces: some rooms have one or two on their walls, over edge tiles clear of the doorways. In a lit room
/// most hold a burning torch; in a dark one they're empty, waiting for one. Placed on their own dice.
/// </summary>
public static partial class DungeonGenerator
{
    private const ulong SconceStream = 9;

    /// <summary>The chance (%) a room has sconces, and that one in a lit room holds a torch.</summary>
    public const int SconceChance = 30;

    public const int SconceLitChance = 70;

    private static void PlaceSconces(List<RoomSetup> rooms, IRandomSource random)
    {
        for (int i = 0; i < rooms.Count; i++)
        {
            RoomSetup room = rooms[i];
            if (room.Id == KeepCourtyard.Id || room.Id == RoofId || room.Role is RoomRole.Den || random.NextInt(0, 100) >= SconceChance)
            {
                continue;
            }

            RoomLayout layout = room.Template.Layout;
            HashSet<GridPosition> kept = [.. DoorAreas(room.Template, room.DoorTiles ?? new Dictionary<Direction, GridPosition>())];
            List<GridPosition> edge = [.. layout.Positions.Where(tile => (tile.X == 0 || tile.Y == 0 || tile.X == layout.Width - 1 || tile.Y == layout.Height - 1)
                && layout[tile] == TileKind.Floor && !kept.Contains(tile))];
            random.Shuffle(edge);
            var sconces = new Dictionary<GridPosition, bool>();
            foreach (GridPosition tile in edge.Where(tile => !kept.Contains(tile)).Take(random.NextInt(1, 3)))
            {
                sconces[tile] = !room.Dark && random.NextInt(0, 100) < SconceLitChance;
                kept.UnionWith(tile.OrthogonalNeighbors());
            }

            if (sconces.Count > 0)
            {
                rooms[i] = room with { Features = (room.Features ?? new RoomFeatures()) with { Sconces = sconces } };
            }
        }
    }
}
