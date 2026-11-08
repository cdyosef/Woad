using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Randomness;
using Woad.Core.Rules;

namespace Woad.Core.Dungeon;

/// <summary>
/// Crates: some ordinary rooms have one or two wooden crates standing about, to smash open (see
/// <c>GameSession.Smash</c>). They stand on open floor clear of doorways, stairs, starts and whatever's already
/// there, and never cut the room off. Placed on their own dice, so the rest of the dungeon comes out the same.
/// </summary>
public static partial class DungeonGenerator
{
    private const ulong CrateStream = 7;

    /// <summary>The chance (%) an ordinary room has crates.</summary>
    public const int CrateChance = 35;

    private static void PlaceCrates(List<RoomSetup> rooms, IReadOnlyList<BreachSetup> breaches, IRandomSource random)
    {
        for (int i = 0; i < rooms.Count; i++)
        {
            RoomSetup room = rooms[i];
            if (room.Role is not (RoomRole.Combat or RoomRole.Treasure or RoomRole.Stairs) || room.Trap || random.NextInt(0, 100) >= CrateChance)
            {
                continue;
            }

            RoomTemplate template = room.Template;
            HashSet<GridPosition> kept = [.. DoorAreas(template, room.DoorTiles ?? new Dictionary<Direction, GridPosition>()), .. template.SpawnPoints,
                .. FixedTiles(template, room.Features ?? new RoomFeatures()), .. new[] { room.Features?.Merchant?.Position }.OfType<GridPosition>()];

            // Breaches (cracked walls once the run starts) and the tiles inside them stay clear, and count as walls.
            List<GridPosition> breached = [.. breaches.SelectMany(breach => new[] { (breach.RoomA, breach.TileA), (breach.RoomB, breach.TileB) }).Where(end => end.Item1 == room.Id).Select(end => end.Item2)];
            foreach (GridPosition breach in breached)
            {
                kept.UnionWith([breach, .. breach.OrthogonalNeighbors()]);
            }
            int crates = random.NextInt(1, 3);
            for (int placed = 0; placed < crates; placed++)
            {
                List<GridPosition> open = [.. template.TilesOf(TileKind.Floor).Where(tile => !kept.Contains(tile)
                    && tile.OrthogonalNeighbors().All(next => !template.Layout.Contains(next) || template.Layout[next] is not (TileKind.Chest or TileKind.Pot)))];
                random.Shuffle(open);
                // Traps count as blocked (found ones are walked around): with them, the room still mustn't be cut off.
                IReadOnlyCollection<GridPosition> traps = [.. (room.Features ?? new RoomFeatures()).Traps.Keys, .. breached];
                if (open.FirstOrDefault(tile => StaysConnected(template, [tile, .. traps])) is { } spot && open.Contains(spot))
                {
                    template = template.WithTile(spot, TileKind.Crate);
                    kept.Add(spot);
                }
            }

            rooms[i] = room with { Template = template };
        }
    }
}
