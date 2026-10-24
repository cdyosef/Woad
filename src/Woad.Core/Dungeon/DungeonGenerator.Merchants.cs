using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Randomness;
using Woad.Core.Rules;

namespace Woad.Core.Dungeon;

public static partial class DungeonGenerator
{
    /// <summary>Percent chance a run has a traveling merchant chained up somewhere in it.</summary>
    public const int MerchantChance = 33;

    private static readonly string[] MerchantNames =
        ["Bartholomew", "Old Meg", "Silas", "Fenwick", "Marta", "Ignatius", "Tobble", "Rosalind", "Hagan", "Wilhelmina", "Quill", "Dorrit"];

    /// <summary>
    /// Sometimes chains a traveling merchant up in an ordinary room on a random floor: against a wall,
    /// clear of the doorways, the starts and anything else in the room, never cutting it in two.
    /// </summary>
    private static void PlaceMerchant(List<RoomSetup> rooms, int floors, IRandomSource random)
    {
        if (random.NextInt(0, 100) >= MerchantChance)
        {
            return;
        }

        int floor = random.NextInt(1, floors + 1);
        List<RoomSetup> hosts = rooms.Where(room => room.Floor == floor && room.Role is RoomRole.Combat or RoomRole.Treasure or RoomRole.Stairs && !room.Trap).ToList();
        random.Shuffle(hosts);
        foreach (RoomSetup host in hosts)
        {
            RoomTemplate template = host.Template;
            RoomLayout layout = template.Layout;
            RoomFeatures features = host.Features ?? RoomFeatures.None;
            HashSet<GridPosition> taken = [.. DoorAreas(template, host.DoorTiles ?? new Dictionary<Direction, GridPosition>()),
                .. template.HeroStarts.Values, .. template.SpawnPoints, .. template.Monsters.Select(spawn => spawn.Position),
                .. features.Traps.Keys, .. features.Keys.Keys, .. features.Caches.Keys,
                .. layout.Positions.Where(tile => layout[tile] is TileKind.Chest or TileKind.Pot or TileKind.StairsDown or TileKind.StairsUp or TileKind.Exit)
                    .SelectMany(tile => tile.OrthogonalNeighbors().Append(tile))];
            bool AgainstWall(GridPosition tile) =>
                tile.OrthogonalNeighbors().Any(next => !layout.Contains(next) || layout[next] is TileKind.Wall or TileKind.Rock);
            List<GridPosition> spots = layout.Positions
                .Where(tile => layout[tile] == TileKind.Floor && !taken.Contains(tile) && AgainstWall(tile))
                .ToList();
            random.Shuffle(spots);
            foreach (GridPosition spot in spots)
            {
                if (StaysConnected(template, [spot]))
                {
                    int index = rooms.IndexOf(host);
                    rooms[index] = host with
                    {
                        Features = features with { Merchant = new MerchantSetup(spot, GameRules.ChestLockDifficulty(floor), random.Pick(MerchantNames)) },
                    };
                    return;
                }
            }
        }
    }
}
