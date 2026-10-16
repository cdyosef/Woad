using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Randomness;
using Woad.Core.Rules;

namespace Woad.Core.Dungeon;

/// <summary>
/// Obstacles that change a room's shape: a wall dividing it (with a locked gate, a cracked wall or a
/// secret way through), a chasm or stream crossing it (with a bridge or a ford), and breaches in the
/// edge walls between neighboring rooms. Every one is checked so that a run can always be finished:
/// whatever a party needs (doorways, stairs, the exit, monsters in a sealed room) stays reachable on
/// foot, or behind a locked gate, which Od Hand can always pick. Cracked and secret ways only ever
/// close off extras.
/// </summary>
public static partial class DungeonGenerator
{
    /// <summary>Percent chance an ordinary room is divided by a wall.</summary>
    private const int DivideChance = 15;

    /// <summary>Percent chance an ordinary room (not divided) has a chasm or a stream across it.</summary>
    private const int ChannelChance = 20;

    /// <summary>Percent chance a pair of neighboring rooms gets a breach in their shared edge.</summary>
    private const int BreachChance = 15;

    /// <summary>Rows and columns a dividing line may run along: clear of the doorways in the middle of each edge.</summary>
    private static readonly int[] LineIndexes = [2, 3, 5, 6];

    /// <summary>The rows or columns one off each edge that a breach may open, skipping the doorway in the middle.</summary>
    private static readonly int[] BreachOffsets = [1, 2, 3, 5, 6, 7];

    /// <summary>A full row (or, if vertical, column) across the room.</summary>
    private sealed record Line(bool Vertical, int Index)
    {
        public IEnumerable<GridPosition> Tiles =>
            Enumerable.Range(0, RoomGrid.Size).Select(i => Vertical ? new GridPosition(Index, i) : new GridPosition(i, Index));

        /// <summary>-1 or +1 for the two sides of the line, 0 on it.</summary>
        public int SideOf(GridPosition tile) => Math.Sign((Vertical ? tile.X : tile.Y) - Index);

        /// <summary>The tile beside <paramref name="onLine"/> on <paramref name="side"/>.</summary>
        public GridPosition Beside(GridPosition onLine, int side) =>
            Vertical ? new GridPosition(onLine.X + side, onLine.Y) : new GridPosition(onLine.X, onLine.Y + side);
    }

    private static List<Line> ShuffledLines(IRandomSource random)
    {
        List<Line> lines = [.. LineIndexes.SelectMany(index => new[] { new Line(true, index), new Line(false, index) })];
        random.Shuffle(lines);
        return lines;
    }

    /// <summary>
    /// Tiles an obstacle mustn't cover: stairs, the exit, chests, pots, starts, monsters, and anything the
    /// room's secrets put somewhere (traps, stashes, keys, the Boss Key).
    /// </summary>
    private static HashSet<GridPosition> FixedTiles(RoomTemplate template, RoomFeatures features) =>
    [
        .. template.TilesOf(TileKind.StairsDown), .. template.TilesOf(TileKind.StairsUp), .. template.TilesOf(TileKind.Exit),
        .. template.TilesOf(TileKind.Chest), .. template.TilesOf(TileKind.Pot), .. template.TilesOf(TileKind.CrackedWall),
        .. template.HeroStarts.Values, .. template.Monsters.Select(spawn => spawn.Position),
        .. features.Traps.Keys, .. features.Caches.Keys, .. features.Keys.Keys,
        .. new[] { features.BossKeyOnFloor }.OfType<GridPosition>(),
    ];

    /// <summary>
    /// Whether every tile a figure could stand on can be reached from every other, so no part of the
    /// room, and no doorway, is cut off. Traps count as blocked, since found ones are walked around.
    /// </summary>
    private static bool Connected(RoomTemplate template, RoomFeatures? features = null)
    {
        bool Passable(GridPosition tile) => template.Layout[tile].IsWalkable() && features?.Traps.ContainsKey(tile) != true;
        List<GridPosition> open = template.Layout.Positions.Where(Passable).ToList();
        if (open.Count == 0)
        {
            return true;
        }

        // Every open tile reachable without crossing a trap, and every trap tile beside one of them
        // (a trap walled into a corner would hide whatever stands on it for good).
        IReadOnlyDictionary<GridPosition, int> reached = PathSearch.Run(open[0], Passable).Distances;
        return reached.Count == open.Count
               && template.Layout.Positions
                   .Where(tile => template.Layout[tile].IsWalkable() && !Passable(tile))
                   .All(trap => trap.OrthogonalNeighbors().Any(reached.ContainsKey));
    }

    /// <summary>
    /// Sometimes a wall across the room with one way through. If one side holds nothing the party needs
    /// (no doorway, stairs, exit or monster), the way may be a locked gate, a cracked wall with a stash
    /// inside, or a secret way with a stash beyond; otherwise it's always a locked gate. So is a room
    /// someone could drop into from above (<paramref name="dropsIn"/>), lest they land in the closed-off part.
    /// </summary>
    private static (RoomTemplate Template, RoomFeatures Features) Divide(
        RoomTemplate template, RoomFeatures features, IReadOnlyCollection<Direction> doors, int floor, bool dropsIn, IRandomSource random)
    {
        HashSet<GridPosition> fixedTiles = FixedTiles(template, features);
        IEnumerable<GridPosition> needed = doors.Select(template.Layout.MiddleOf)
            .Concat(template.TilesOf(TileKind.StairsDown)).Concat(template.TilesOf(TileKind.StairsUp)).Concat(template.TilesOf(TileKind.Exit))
            .Concat(template.Monsters.Select(spawn => spawn.Position));

        foreach (Line line in ShuffledLines(random))
        {
            if (line.Tiles.Any(fixedTiles.Contains))
            {
                continue;
            }

            List<GridPosition> spots = line.Tiles
                .Where(tile => template.Layout[tile] is TileKind.Floor or TileKind.Rubble
                               && template.Layout[line.Beside(tile, -1)].IsWalkable() && template.Layout[line.Beside(tile, 1)].IsWalkable())
                .ToList();
            if (spots.Count == 0)
            {
                continue;
            }

            GridPosition gate = random.Pick(spots);
            RoomTemplate walled = line.Tiles.Aggregate(template, (room, tile) => room.WithTile(tile, tile == gate ? TileKind.Floor : TileKind.Wall));
            if (!Connected(walled, features))
            {
                continue;
            }

            int? closet = new[] { -1, 1 }.Where(side => !needed.Any(tile => line.SideOf(tile) == side)).Select(side => (int?)side).FirstOrDefault();
            int roll = random.NextInt(0, 100);
            if (closet is not { } extrasSide || dropsIn || roll < 40)
            {
                return (walled, features with { Gates = new Dictionary<GridPosition, int> { [gate] = GameRules.DoorLockDifficulty(floor) } });
            }

            bool key = random.CoinFlip();
            var stash = new CacheSetup(0, Keys: key ? 1 : 0, Cards: key ? 0 : 1);
            var caches = new Dictionary<GridPosition, CacheSetup>(features.Caches);
            if (roll < 70)
            {
                caches[gate] = stash with { BehindWall = true };
                return (walled.WithTile(gate, TileKind.CrackedWall), features with { Caches = caches });
            }

            // A secret way; the same search that finds it turns up the stash beyond.
            List<GridPosition> beyond = walled.TilesOf(TileKind.Floor).Where(tile => line.SideOf(tile) == extrasSide && tile != line.Beside(gate, extrasSide) && !fixedTiles.Contains(tile)).ToList();
            if (beyond.Count > 0)
            {
                caches[random.Pick(beyond)] = stash;
            }

            return (walled.WithTile(gate, TileKind.Wall), features with
            {
                Caches = caches,
                HiddenGates = new Dictionary<GridPosition, int> { [gate] = GameRules.SecretDifficulty(floor) },
            });
        }

        return (template, features);
    }

    /// <summary>
    /// Sometimes a chasm (pits, only where they lead somewhere or on the last floor) or a stream of deep
    /// water across the room, with one crossing: a bridge of floor, or a shallow ford.
    /// </summary>
    private static RoomTemplate Channel(RoomTemplate template, RoomFeatures features, bool pits, IRandomSource random)
    {
        HashSet<GridPosition> fixedTiles = FixedTiles(template, features);
        TileKind gap = pits ? TileKind.Pit : TileKind.DeepWater;
        TileKind crossing = pits ? TileKind.Floor : TileKind.ShallowWater;
        foreach (Line line in ShuffledLines(random))
        {
            if (line.Tiles.Any(fixedTiles.Contains))
            {
                continue;
            }

            List<GridPosition> spots = line.Tiles
                .Where(tile => template.Layout[tile].IsWalkable()
                               && template.Layout[line.Beside(tile, -1)].IsWalkable() && template.Layout[line.Beside(tile, 1)].IsWalkable())
                .ToList();
            if (spots.Count == 0)
            {
                continue;
            }

            GridPosition way = random.Pick(spots);
            RoomTemplate cut = line.Tiles
                .Where(tile => template.Layout[tile] is TileKind.Floor or TileKind.Rubble or TileKind.ShallowWater)
                .Aggregate(template, (room, tile) => room.WithTile(tile, tile == way ? crossing : gap));
            if (Connected(cut, features))
            {
                return cut;
            }
        }

        return template;
    }

    /// <summary>
    /// Sometimes a breach in the edge two neighboring rooms share (never into the boss room): a cracked
    /// wall on both sides, off the doorway, that a bomb opens into an extra way between them.
    /// </summary>
    private static List<BreachSetup> PlanBreaches(List<RoomSetup> rooms, IRandomSource random)
    {
        var breaches = new List<BreachSetup>();

        // Each room as its breaches so far will leave it: walls where they stand, so two breaches can't
        // seal off a corner between them.
        var walled = rooms.ToDictionary(room => room.Id, room => room.Template);
        foreach (RoomSetup room in rooms.Where(room => room.Role != RoomRole.Boss))
        {
            foreach (Direction side in new[] { Direction.East, Direction.South })
            {
                RoomSetup? neighbor = rooms.FirstOrDefault(other => other.Floor == room.Floor && other.MapPosition == room.MapPosition.Step(side) && other.Role != RoomRole.Boss);
                if (neighbor is null || random.NextInt(0, 100) >= BreachChance)
                {
                    continue;
                }

                List<int> offsets = [.. BreachOffsets];
                random.Shuffle(offsets);
                foreach (int offset in offsets)
                {
                    GridPosition here = side == Direction.East ? new GridPosition(RoomGrid.Size - 1, offset) : new GridPosition(offset, RoomGrid.Size - 1);
                    GridPosition there = side == Direction.East ? new GridPosition(0, offset) : new GridPosition(offset, 0);
                    if (FixedTiles(room.Template, room.Features ?? RoomFeatures.None).Contains(here)
                        || FixedTiles(neighbor.Template, neighbor.Features ?? RoomFeatures.None).Contains(there)
                        || !Connected(walled[room.Id].WithTile(here, TileKind.Wall), room.Features)
                        || !Connected(walled[neighbor.Id].WithTile(there, TileKind.Wall), neighbor.Features))
                    {
                        continue;
                    }

                    walled[room.Id] = walled[room.Id].WithTile(here, TileKind.Wall);
                    walled[neighbor.Id] = walled[neighbor.Id].WithTile(there, TileKind.Wall);
                    breaches.Add(new BreachSetup(room.Id, here, neighbor.Id, there));
                    break;
                }
            }
        }

        return breaches;
    }
}
