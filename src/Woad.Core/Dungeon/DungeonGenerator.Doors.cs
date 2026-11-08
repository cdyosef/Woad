using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Randomness;
using Woad.Core.Rules;

namespace Woad.Core.Dungeon;

/// <summary>
/// Sterner doors, past floor 1: some locked doors have a second lock, some trapped doors a second trap; and on any
/// floor, now and then a door between two ordinary rooms is barred on one side, but only where there's another
/// way round. Decided on their own dice, so the rest of the dungeon comes out the same.
/// </summary>
public static partial class DungeonGenerator
{
    private const ulong DoorStream = 8;

    /// <summary>The chances (%) of a second lock, a second trap, and a door barred.</summary>
    public const int SecondLockChance = 30;

    public const int SecondTrapChance = 25;

    public const int BarredChance = 12;

    private static void SternerDoors(List<RoomSetup> rooms, List<PassageSetup> passages, IRandomSource random)
    {
        Dictionary<string, RoomSetup> byId = rooms.ToDictionary(room => room.Id);
        for (int i = 0; i < passages.Count; i++)
        {
            PassageSetup passage = passages[i];
            int floor = byId.TryGetValue(passage.RoomId, out RoomSetup? room) ? room.Floor : 1;
            if (floor < 2)
            {
                continue;
            }

            if (passage.Kind == PassageKind.Locked && random.NextInt(0, 100) < SecondLockChance)
            {
                passage = passage with { Locks = 2 };
            }

            if (passage.Trap is { } trap && random.NextInt(0, 100) < SecondTrapChance)
            {
                passage = passage with { MoreTraps = [trap] };
            }

            passages[i] = passage;
        }

        // Barred doors: plain doors between ordinary rooms that aren't the only way between them.
        RoomRole[] ordinary = [RoomRole.Combat, RoomRole.Treasure, RoomRole.Stairs];
        foreach (RoomSetup room in rooms.Where(room => room.Level == 0 && ordinary.Contains(room.Role)).OrderBy(room => room.Id, StringComparer.Ordinal).ToList())
        {
            foreach ((Direction side, string otherId) in (room.Doors ?? new Dictionary<Direction, string>()).Where(door => door.Key is Direction.East or Direction.South).ToList())
            {
                RoomSetup other = byId[otherId];
                bool plain = !passages.Any(passage => (passage.RoomId == room.Id && passage.Side == side) || (passage.RoomId == otherId && passage.Side == side.Opposite()));
                if (!plain || !ordinary.Contains(other.Role) || other.Floor != room.Floor || other.Level != 0 || random.NextInt(0, 100) >= BarredChance
                    || !StillConnected(rooms, passages, room.Floor, (room.Id, otherId)))
                {
                    continue;
                }

                passages.Add(new PassageSetup(room.Id, side, PassageKind.Open) { BarredFrom = random.CoinFlip() ? room.Id : otherId });
            }
        }
    }

    /// <summary>Whether a floor's rooms (not counting secret ones) still all connect by plain doorways without the door between <paramref name="cut"/>.</summary>
    private static bool StillConnected(List<RoomSetup> rooms, List<PassageSetup> passages, int floor, (string A, string B) cut)
    {
        List<RoomSetup> floorRooms = [.. rooms.Where(room => room.Floor == floor && room.Level == 0 && room.Role != RoomRole.Secret)];
        bool Usable(string from, Direction side, string to) =>
            (from, to) != cut && (to, from) != cut
            && !passages.Any(passage => ((passage.RoomId == from && passage.Side == side) || (passage.RoomId == to && passage.Side == side.Opposite()))
                                        && (passage.Kind is PassageKind.Secret or PassageKind.Cracked || passage.BarredFrom is not null));
        var reached = new HashSet<string> { floorRooms[0].Id };
        var frontier = new Queue<RoomSetup>([floorRooms[0]]);
        while (frontier.Count > 0)
        {
            RoomSetup room = frontier.Dequeue();
            foreach ((Direction side, string to) in room.Doors ?? new Dictionary<Direction, string>())
            {
                if (floorRooms.Any(other => other.Id == to) && Usable(room.Id, side, to) && reached.Add(to))
                {
                    frontier.Enqueue(floorRooms.First(other => other.Id == to));
                }
            }
        }

        return floorRooms.All(room => reached.Contains(room.Id));
    }
}
