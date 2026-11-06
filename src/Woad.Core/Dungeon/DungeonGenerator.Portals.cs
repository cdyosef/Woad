using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Rules;

namespace Woad.Core.Dungeon;

/// <summary>
/// Portals: a standing ring in the entrance, one in floor 1's stairs room and one in the last floor's boss
/// room, all linked once lit (see <c>GameSession.Portals</c>). Placed by rule, with no dice, so the dungeon
/// comes out as it would without them.
/// </summary>
public static partial class DungeonGenerator
{
    private static void PlacePortals(List<RoomSetup> rooms, int floors)
    {
        RoomSetup?[] hosts =
        [
            // The entrance: a gated dungeon's entrance hall, inside its great door.
            rooms.FirstOrDefault(room => room.Floor == 1 && room.Level == 0 && room.Role == RoomRole.Hall)
                ?? rooms.FirstOrDefault(room => room.Floor == 1 && room.Level == 0 && room.Role == RoomRole.Arrival),
            rooms.FirstOrDefault(room => room.Floor == 1 && room.Level == 0 && room.Role == RoomRole.Stairs),
            rooms.FirstOrDefault(room => room.Floor == floors && room.Level == 0 && room.Role == RoomRole.Boss),
        ];
        foreach (RoomSetup host in hosts.OfType<RoomSetup>())
        {
            if (PortalSpot(host) is { } spot)
            {
                Replace(rooms, host with { Template = host.Template.WithTile(spot, TileKind.Portal) });
            }
        }
    }

    /// <summary>
    /// Where a room's portal stands: open floor reachable from its doors, clear of doorways, starts, spawn points and
    /// what's already there, that cuts nothing off. In the boss room, near the door (away from the boss); elsewhere,
    /// near the middle.
    /// </summary>
    private static GridPosition? PortalSpot(RoomSetup room)
    {
        RoomTemplate template = room.Template;
        IReadOnlyDictionary<Direction, GridPosition> doors = room.DoorTiles ?? new Dictionary<Direction, GridPosition>();
        HashSet<GridPosition> reserved = [.. DoorAreas(template, doors), .. template.SpawnPoints,
            .. FixedTiles(template, room.Features ?? new RoomFeatures()), .. new[] { template.BossSpawn }.OfType<GridPosition>()];
        bool Open(GridPosition tile) => template.Layout.Contains(tile) && template.Layout[tile].IsWalkable();
        GridPosition from = doors.Values.Select(door => (GridPosition?)door).FirstOrDefault() ?? template.Layout.Center;
        HashSet<GridPosition> reached = [.. PathSearch.Run(from, Open).Distances.Keys];
        GridPosition near = room.Role == RoomRole.Boss && doors.Count > 0 ? doors.Values.First() : template.Layout.Center;
        int keepOff = room.Role == RoomRole.Boss ? 3 : 0;
        return template.TilesOf(TileKind.Floor)
            .Where(tile => reached.Contains(tile) && !reserved.Contains(tile) && tile.ManhattanDistanceTo(near) >= keepOff
                && template.Layout.Positions.Where(other => other.ChebyshevDistanceTo(tile) == 1).Count(Open) >= 3
                && StaysConnected(template, [tile]))
            .OrderBy(tile => tile.ManhattanDistanceTo(near))
            .ThenBy(tile => tile.Y)
            .ThenBy(tile => tile.X)
            .Select(tile => (GridPosition?)tile)
            .FirstOrDefault();
    }
}
