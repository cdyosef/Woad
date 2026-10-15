using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Randomness;
using Woad.Core.Rules;

namespace Woad.Core.Dungeon;

/// <summary>
/// Builds a run's dungeon from its seed. For each floor a random walk lays out 6-8 rooms on a 5x5
/// map. The floors share that map, so pits and trap doors drop into the room underneath (rooms with
/// nothing below avoid layouts with pits, except on the last floor). The arrival room is where the
/// walk starts (below floor 1, under the stairs that lead down to it), the farthest room gets the
/// stairs (or the boss on the last floor, behind the Boss Door, its only way in), and a dead end
/// becomes the treasure room (on the last floor, home of the Boss Key). Each room gets a randomly
/// turned layout from the theme and monsters bought with a difficulty budget that grows with floor
/// depth and party size, then its secrets: traps, locked and trapped chests, hidden stashes and cracked walls. Some doors
/// are locked or trapped, and some extra links between rooms are secret doors or cracked walls.
/// The same seed and party size always build the same dungeon.
/// </summary>
public static partial class DungeonGenerator
{
    public const int MapSize = 5;
    public const int MinRooms = 6;
    public const int MaxRooms = 8;

    /// <summary>Percent chance a floor gets a secret room.</summary>
    private const int SecretRoomChance = 50;

    /// <summary>Percent chance a room gets a patch of rubble.</summary>
    private const int RubbleChance = 40;

    private const ulong Stream = 4;

    public static RunSetup CreateRun(ContentCatalog content, string themeId, IReadOnlyList<HeroSetup> heroes, ulong seed)
    {
        ThemeDefinition theme = content.GetTheme(themeId);
        IReadOnlyList<ThemeRoom> layouts = content.RoomsFor(themeId);
        foreach (RoomRole role in Enum.GetValues<RoomRole>())
        {
            // Secret rooms borrow treasure layouts.
            bool needed = role switch
            {
                RoomRole.Stairs => theme.Floors > 1,
                RoomRole.Secret => false,
                _ => true,
            };
            if (needed && !layouts.Any(layout => layout.Roles.Contains(role)))
            {
                throw new InvalidOperationException($"Theme '{themeId}' has no {role} room layouts.");
            }
        }

        if (theme.Boss is null)
        {
            throw new InvalidOperationException($"Theme '{themeId}' has no boss.");
        }

        // Every floor's map first, so each room knows what lies under it: pits and trap doors lead
        // to the room below. A floor's arrival room is under the stairs that lead to it.
        var random = new Pcg32(seed, Stream);
        var rooms = new List<RoomSetup>();
        var passages = new List<PassageSetup>();
        var plans = new List<FloorPlan>();
        for (int floor = 1; floor <= theme.Floors; floor++)
        {
            plans.Add(PlanFloor(floor, floor == theme.Floors, plans.Count > 0 ? plans[^1].Farthest : null, random, passages));
        }

        foreach (FloorPlan plan in plans)
        {
            HashSet<GridPosition> below = plan.Floor < plans.Count
                ? [.. plans[plan.Floor].Cells.Where(cell => plans[plan.Floor].Roles[cell] is not (RoomRole.Boss or RoomRole.Secret))]
                : [];
            BuildFloor(plan, below, theme, content, layouts, GameRules.PartyStrength(heroes), random, rooms);
        }

        return new RunSetup
        {
            Seed = seed,
            ThemeId = themeId,
            Heroes = heroes,
            Rooms = rooms,
            StartRoomId = rooms.First(room => room.Floor == 1 && room.Role == RoomRole.Arrival).Id,
            Passages = passages,
            Breaches = PlanBreaches(rooms, random),
        };
    }

    /// <summary>A room's id: its floor and map cell, e.g. "f2-31".</summary>
    public static string RoomId(int floor, GridPosition cell) => $"f{floor}-{cell.X}{cell.Y}";

    /// <summary>One floor's map: its rooms' cells, the links between them and each room's role.</summary>
    private sealed record FloorPlan(
        int Floor,
        bool LastFloor,
        List<GridPosition> Cells,
        List<(GridPosition A, GridPosition B)> Links,
        Dictionary<GridPosition, RoomRole> Roles,
        GridPosition Farthest);

    /// <summary>Lays out a floor's rooms and plans its doors (added to <paramref name="passages"/>).</summary>
    /// <param name="underStairs">The arrival room's cell (under the stairs above), or null to pick one.</param>
    private static FloorPlan PlanFloor(int floor, bool lastFloor, GridPosition? underStairs, IRandomSource random, List<PassageSetup> passages)
    {
        // Lay out cells with a random walk; every new cell is linked to the one it grew from.
        int roomCount = random.NextInt(MinRooms, MaxRooms + 1);
        GridPosition start = underStairs ?? new GridPosition(random.NextInt(0, MapSize), random.NextInt(0, MapSize));
        var cells = new List<GridPosition> { start };
        var links = new List<(GridPosition A, GridPosition B)>();
        while (cells.Count < roomCount)
        {
            GridPosition from = random.Pick(cells);
            GridPosition to = from.Step((Direction)random.NextInt(0, 4));
            if (InMap(to) && !cells.Contains(to))
            {
                cells.Add(to);
                links.Add((from, to));
            }
        }

        // A few extra doors between neighboring rooms add loops.
        var loops = new HashSet<(GridPosition, GridPosition)>();
        foreach (GridPosition cell in cells.ToList())
        {
            foreach (Direction side in new[] { Direction.East, Direction.South })
            {
                GridPosition neighbor = cell.Step(side);
                if (cells.Contains(neighbor) && !Linked(links, cell, neighbor) && random.NextInt(0, 4) == 0)
                {
                    links.Add((cell, neighbor));
                    loops.Add((cell, neighbor));
                }
            }
        }

        Dictionary<GridPosition, int> distance = Distances(links, start);
        Dictionary<GridPosition, RoomRole> roles = AssignRoles(cells, links, start, distance, lastFloor, random);
        GridPosition farthest = roles.First(role => role.Value is RoomRole.Boss or RoomRole.Stairs).Key;

        // The boss room keeps a single door, the Boss Door: its nearest neighbor's.
        if (lastFloor)
        {
            var bossLinks = links.Where(link => link.A == farthest || link.B == farthest).ToList();
            var keep = bossLinks.OrderBy(link => distance[link.A == farthest ? link.B : link.A]).First();
            links.RemoveAll(link => bossLinks.Contains(link) && link != keep);
            ReconnectAround(cells, links, loops, start, farthest, bossLinks.Where(link => link != keep).ToList());
        }

        // More hidden links between neighbors: secret doors and cracked walls, never into the boss room.
        foreach (GridPosition cell in cells.ToList())
        {
            foreach (Direction side in new[] { Direction.East, Direction.South })
            {
                GridPosition neighbor = cell.Step(side);
                bool bossRoom = lastFloor && (cell == farthest || neighbor == farthest);
                if (cells.Contains(neighbor) && !bossRoom && !Linked(links, cell, neighbor) && random.NextInt(0, 100) < 30)
                {
                    links.Add((cell, neighbor));
                    loops.Add((cell, neighbor));
                }
            }
        }

        // Sometimes a secret room: a spare map cell beside one ordinary room, reached only through a
        // secret door or a cracked wall and with no other doors. It holds only extras (treasure, stashes),
        // never anything a run needs, so a party that never finds it can still finish.
        (GridPosition Host, GridPosition Cell)? secretLink = null;
        if (random.NextInt(0, 100) < SecretRoomChance)
        {
            List<(GridPosition Host, GridPosition Cell)> spots = cells
                .Where(host => roles[host] is RoomRole.Arrival or RoomRole.Combat or RoomRole.Treasure)
                .SelectMany(host => host.OrthogonalNeighbors().Where(next => InMap(next) && !cells.Contains(next)).Select(next => (host, next)))
                .ToList();
            if (spots.Count > 0)
            {
                secretLink = random.Pick(spots);
                cells.Add(secretLink.Value.Cell);
                links.Add(secretLink.Value);
                roles[secretLink.Value.Cell] = RoomRole.Secret;
            }
        }

        foreach ((GridPosition a, GridPosition b) in links)
        {
            bool bossDoor = lastFloor && (a == farthest || b == farthest);
            if (PlanPassage(a, b, floor, bossDoor, loops.Contains((a, b)), (a, b) == secretLink, random) is { } passage)
            {
                passages.Add(passage with { RoomId = RoomId(floor, a) });
            }
        }

        return new FloorPlan(floor, lastFloor, cells, links, roles, farthest);
    }

    /// <summary>
    /// Fills in a planned floor's rooms: layouts, monsters and secrets. <paramref name="below"/> holds
    /// the cells with a room under them on the next floor; only those rooms get trap doors, and above
    /// the last floor, rooms with nothing below avoid layouts with pits (which would be bottomless).
    /// </summary>
    private static void BuildFloor(
        FloorPlan plan,
        HashSet<GridPosition> below,
        ThemeDefinition theme,
        ContentCatalog content,
        IReadOnlyList<ThemeRoom> layouts,
        int partySize,
        IRandomSource random,
        List<RoomSetup> rooms)
    {
        (int floor, bool lastFloor, List<GridPosition> cells, List<(GridPosition A, GridPosition B)> links, Dictionary<GridPosition, RoomRole> roles, _) = plan;
        GridPosition? trap = null;
        List<GridPosition> combat = cells.Where(cell => roles[cell] == RoomRole.Combat).ToList();
        if (floor >= 2 && combat.Count > 0)
        {
            trap = random.Pick(combat);
        }

        foreach (GridPosition cell in cells)
        {
            RoomRole role = roles[cell];
            bool roomBelow = below.Contains(cell);
            RoomRole layoutRole = role == RoomRole.Secret ? RoomRole.Treasure : role;
            List<ThemeRoom> candidates = layouts.Where(layout => layout.Roles.Contains(layoutRole)).ToList();
            List<ThemeRoom> pitless = candidates.Where(layout => !layout.Template.TilesOf(TileKind.Pit).Any()).ToList();
            if (!lastFloor && !roomBelow && pitless.Count > 0)
            {
                candidates = pitless;
            }

            RoomTemplate template = random.Pick(candidates).Template.Transformed(random.NextInt(0, 4), random.CoinFlip());
            if (role == RoomRole.Arrival && floor > 1)
            {
                template = template.WithTile(StairsUpSpot(template), TileKind.StairsUp);
            }

            template = Roughen(template, role, random);

            bool isTrap = cell == trap;
            IEnumerable<MonsterSpawn> monsters = PlaceMonsters(template, role, floor, partySize, isTrap, theme, content, random);
            template = template.WithMonsters(template.Monsters.Concat(monsters));
            (template, RoomFeatures features) = PlaceSecrets(template, role, floor, lastFloor, roomBelow, random);

            var doors = new Dictionary<Direction, string>();
            foreach (Direction side in Enum.GetValues<Direction>())
            {
                GridPosition neighbor = cell.Step(side);
                if (Linked(links, cell, neighbor))
                {
                    doors[side] = RoomId(floor, neighbor);
                }
            }

            // Sometimes a wall dividing the room, or a chasm or stream across it (never in a sealed room).
            if (!isTrap && role is RoomRole.Combat or RoomRole.Treasure or RoomRole.Stairs)
            {
                if (random.NextInt(0, 100) < DivideChance)
                {
                    // Pits and trap doors in the room above drop people in here.
                    bool dropsIn = rooms.Any(above => above.Floor == floor - 1 && above.MapPosition == cell
                        && (above.Template.TilesOf(TileKind.Pit).Any() || above.Features?.Traps.Values.Any(trap => trap.Kind == TrapKind.TrapDoor) == true));
                    (template, features) = Divide(template, features, doors.Keys, floor, dropsIn, random);
                }
                else if (random.NextInt(0, 100) < ChannelChance)
                {
                    bool pits = (roomBelow || lastFloor) && random.CoinFlip();
                    template = Channel(template, features, pits, random);
                }
            }

            rooms.Add(new RoomSetup(RoomId(floor, cell), template, doors, floor, cell, role, isTrap, features));
        }
    }

    /// <summary>
    /// How a door between cells starts: the Boss Door into the boss room; a loop may be secret or a
    /// cracked wall; otherwise sometimes locked, and sometimes trapped. Null for a plain open door.
    /// </summary>
    /// <param name="secretRoom">The only way into a secret room: always a secret door or a cracked wall.</param>
    private static PassageSetup? PlanPassage(GridPosition a, GridPosition b, int floor, bool bossDoor, bool loop, bool secretRoom, IRandomSource random)
    {
        Direction side = Enum.GetValues<Direction>().First(direction => a.Step(direction) == b);
        if (bossDoor)
        {
            return new PassageSetup("", side, PassageKind.BossLocked);
        }

        int roll = random.NextInt(0, 100);
        PassageKind kind = secretRoom
            ? roll < 50 ? PassageKind.Secret : PassageKind.Cracked
            : loop
            ? roll < 35 ? PassageKind.Secret : roll < 65 ? PassageKind.Cracked : PassageKind.Open
            : roll < 20 ? PassageKind.Locked : PassageKind.Open;
        int difficulty = kind switch
        {
            PassageKind.Locked => GameRules.DoorLockDifficulty(floor),
            PassageKind.Secret => GameRules.SecretDifficulty(floor),
            _ => 0,
        };
        TrapSetup? darts = kind is PassageKind.Open or PassageKind.Locked && random.NextInt(0, 100) < 10
            ? new TrapSetup(TrapKind.Darts, GameRules.TrapDifficulty(floor))
            : null;
        return kind == PassageKind.Open && darts is null ? null : new PassageSetup("", side, kind, difficulty, darts);
    }

    /// <summary>
    /// A room's secrets. Traps go on open floor away from doorways, starts, chests and pots; chests may be locked
    /// (better loot) or trapped; a stash may be hidden on the floor or sealed in a cracked wall. The
    /// last floor's treasure room holds the Boss Key, in a chest if it has one.
    /// </summary>
    private static (RoomTemplate Template, RoomFeatures Features) PlaceSecrets(
        RoomTemplate template, RoomRole role, int floor, bool lastFloor, bool roomBelow, IRandomSource random)
    {
        var traps = new Dictionary<GridPosition, TrapSetup>();
        var lockedChests = new Dictionary<GridPosition, int>();
        var chestTraps = new Dictionary<GridPosition, TrapSetup>();
        var caches = new Dictionary<GridPosition, CacheSetup>();
        GridPosition? bossKeyChest = null;
        GridPosition? bossKeyOnFloor = null;

        // Nothing goes beside a chest or pot either: found traps there could wall it off.
        HashSet<GridPosition> reserved = [.. Enum.GetValues<Direction>().Select(side => side.DoorTile()), .. template.HeroStarts.Values,
            .. template.SpawnPoints, .. template.Monsters.Select(spawn => spawn.Position),
            .. template.TilesOf(TileKind.Chest).Concat(template.TilesOf(TileKind.Pot)).SelectMany(tile => tile.OrthogonalNeighbors())];
        List<GridPosition> open = template.TilesOf(TileKind.Floor).Where(tile => !reserved.Contains(tile)).ToList();
        GridPosition? TakeOpenTile()
        {
            if (open.Count == 0)
            {
                return null;
            }

            GridPosition tile = random.Pick(open);
            open.Remove(tile);
            return tile;
        }

        List<GridPosition> chests = template.TilesOf(TileKind.Chest).ToList();
        if (role == RoomRole.Vault)
        {
            if (chests.Count > 0)
            {
                bossKeyChest = random.Pick(chests);
            }
            else
            {
                bossKeyOnFloor = TakeOpenTile();
            }
        }

        foreach (GridPosition chest in chests.Where(chest => chest != bossKeyChest))
        {
            if (random.NextInt(0, 100) < (role is RoomRole.Treasure or RoomRole.Secret ? 50 : 25))
            {
                lockedChests[chest] = GameRules.ChestLockDifficulty(floor);
            }

            if (random.NextInt(0, 100) < 20)
            {
                chestTraps[chest] = new TrapSetup(TrapKind.Needle, GameRules.TrapDifficulty(floor));
            }
        }

        if (role is RoomRole.Arrival or RoomRole.Boss or RoomRole.Vault)
        {
            return (template, new RoomFeatures { LockedChests = lockedChests, ChestTraps = chestTraps, BossKeyChest = bossKeyChest, BossKeyOnFloor = bossKeyOnFloor });
        }

        // Revealed traps are walked around, so a trap never goes where it would cut the room in two.
        for (int count = random.NextInt(0, Math.Min(floor, 3) + 1); count > 0; count--)
        {
            if (TakeOpenTile() is { } tile && StaysConnected(template, [.. traps.Keys, tile]))
            {
                bool trapDoor = roomBelow && random.NextInt(0, 100) < 30;
                traps[tile] = new TrapSetup(trapDoor ? TrapKind.TrapDoor : TrapKind.Spikes, GameRules.TrapDifficulty(floor));
            }
        }

        if (random.NextInt(0, 100) < 30 && TakeOpenTile() is { } stash)
        {
            bool key = random.CoinFlip();
            caches[stash] = new CacheSetup(GameRules.StashDifficulty(floor), Keys: key ? 1 : 0, Cards: key ? 0 : 1);
        }

        List<GridPosition> walls = template.TilesOf(TileKind.Wall).ToList();
        if (walls.Count > 0 && random.NextInt(0, 100) < 30)
        {
            GridPosition crack = random.Pick(walls);
            template = template.WithTile(crack, TileKind.CrackedWall);
            bool key = random.CoinFlip();
            caches[crack] = new CacheSetup(0, Keys: key ? 1 : 0, Cards: key ? 0 : 1, BehindWall: true);
        }

        return (template, new RoomFeatures
        {
            Traps = traps,
            LockedChests = lockedChests,
            ChestTraps = chestTraps,
            BossKeyChest = bossKeyChest,
            BossKeyOnFloor = bossKeyOnFloor,
            Caches = caches,
        });
    }

    /// <summary>Whether every walkable tile not in <paramref name="blocked"/> can still reach every other.</summary>
    private static bool StaysConnected(RoomTemplate template, IReadOnlyCollection<GridPosition> blocked)
    {
        bool Open(GridPosition tile) => template.Layout[tile].IsWalkable() && !blocked.Contains(tile);
        List<GridPosition> walkable = RoomGrid.AllPositions().Where(Open).ToList();
        return walkable.Count == 0 || PathSearch.Run(walkable[0], Open).Distances.Count == walkable.Count;
    }

    private static Dictionary<GridPosition, int> Distances(List<(GridPosition A, GridPosition B)> links, GridPosition start)
    {
        var distance = new Dictionary<GridPosition, int> { [start] = 0 };
        var frontier = new Queue<GridPosition>([start]);
        while (frontier.Count > 0)
        {
            GridPosition cell = frontier.Dequeue();
            foreach (GridPosition next in cell.OrthogonalNeighbors().Where(next => Linked(links, cell, next) && !distance.ContainsKey(next)))
            {
                distance[next] = distance[cell] + 1;
                frontier.Enqueue(next);
            }
        }

        return distance;
    }

    private static Dictionary<GridPosition, RoomRole> AssignRoles(
        List<GridPosition> cells,
        List<(GridPosition A, GridPosition B)> links,
        GridPosition start,
        Dictionary<GridPosition, int> distance,
        bool lastFloor,
        IRandomSource random)
    {
        GridPosition farthest = cells.Where(cell => cell != start).OrderByDescending(cell => distance[cell]).First();
        var roles = cells.ToDictionary(cell => cell, _ => RoomRole.Combat);
        roles[start] = RoomRole.Arrival;
        roles[farthest] = lastFloor ? RoomRole.Boss : RoomRole.Stairs;

        List<GridPosition> others = cells.Where(cell => cell != start && cell != farthest).ToList();
        List<GridPosition> deadEnds = others.Where(cell => cell.OrthogonalNeighbors().Count(next => Linked(links, cell, next)) == 1).ToList();
        GridPosition treasure = random.Pick(deadEnds.Count > 0 ? deadEnds : others);
        roles[treasure] = RoomRole.Treasure;

        // The last floor's vault holds the Boss Key: another dead end if there is one.
        if (lastFloor)
        {
            List<GridPosition> spare = (deadEnds.Count > 1 ? deadEnds : others).Where(cell => cell != treasure).ToList();
            roles[random.Pick(spare)] = RoomRole.Vault;
        }

        return roles;
    }

    /// <summary>
    /// Sometimes a patch of rubble (rough ground, 2 steps a tile) on a room's open floor, away from
    /// doorways, starts and spawn points. It's walkable, so it never cuts anything off.
    /// </summary>
    private static RoomTemplate Roughen(RoomTemplate template, RoomRole role, IRandomSource random)
    {
        if (role is RoomRole.Arrival or RoomRole.Boss or RoomRole.Vault || random.NextInt(0, 100) >= RubbleChance)
        {
            return template;
        }

        HashSet<GridPosition> reserved = [.. Enum.GetValues<Direction>().Select(side => side.DoorTile()), .. template.HeroStarts.Values, .. template.SpawnPoints];
        List<GridPosition> open = template.TilesOf(TileKind.Floor).Where(tile => !reserved.Contains(tile)).ToList();
        if (open.Count == 0)
        {
            return template;
        }

        var patch = new List<GridPosition> { random.Pick(open) };
        for (int size = random.NextInt(2, 6); patch.Count < size;)
        {
            List<GridPosition> edge = patch.SelectMany(tile => tile.OrthogonalNeighbors()).Where(open.Contains).Except(patch).Distinct().ToList();
            if (edge.Count == 0)
            {
                break;
            }

            patch.Add(random.Pick(edge));
        }

        return patch.Aggregate(template, (rough, tile) => rough.WithTile(tile, TileKind.Rubble));
    }

    /// <summary>Where a lower floor's arrival room gets its stairs up: the open floor tile nearest the middle.</summary>
    private static GridPosition StairsUpSpot(RoomTemplate template)
    {
        HashSet<GridPosition> reserved = [.. Enum.GetValues<Direction>().Select(side => side.DoorTile()), .. template.HeroStarts.Values, .. template.SpawnPoints];
        var center = new GridPosition(RoomGrid.Center, RoomGrid.Center);
        return template.TilesOf(TileKind.Floor)
            .Where(tile => !reserved.Contains(tile))
            .OrderBy(tile => tile.ManhattanDistanceTo(center))
            .ThenBy(tile => tile.Y)
            .ThenBy(tile => tile.X)
            .First();
    }

    /// <summary>Buys monsters for a room's spawn points until its difficulty budget runs out.</summary>
    private static List<MonsterSpawn> PlaceMonsters(
        RoomTemplate template,
        RoomRole role,
        int floor,
        int partySize,
        bool trap,
        ThemeDefinition theme,
        ContentCatalog content,
        IRandomSource random)
    {
        var spawns = new List<MonsterSpawn>();
        if (role == RoomRole.Boss && template.BossSpawn is { } bossTile)
        {
            spawns.Add(new MonsterSpawn(bossTile, content.GetMonster(theme.Boss!)));
        }

        if (role == RoomRole.Vault && template.BossSpawn is { } guardTile && theme.Miniboss is { } miniboss)
        {
            spawns.Add(new MonsterSpawn(guardTile, content.GetMonster(miniboss)));
        }

        int budget = role switch
        {
            RoomRole.Arrival or RoomRole.Vault => 0,
            RoomRole.Treasure or RoomRole.Secret => 1,
            RoomRole.Boss => partySize,
            _ => floor + partySize - 1 + (trap ? 1 : 0),
        };

        var roster = theme.Roster
            .Where(entry => entry.MinFloor <= floor)
            .Select(entry => (entry.Weight, Monster: content.GetMonster(entry.Monster)))
            .ToList();
        List<GridPosition> points = [.. template.SpawnPoints];
        random.Shuffle(points);

        foreach (GridPosition point in points)
        {
            var fitting = roster.Where(entry => entry.Monster.Threat <= budget).ToList();
            if (budget <= 0 || fitting.Count == 0)
            {
                break;
            }

            int roll = random.NextInt(0, fitting.Sum(entry => entry.Weight));
            var pick = fitting.First(entry => (roll -= entry.Weight) < 0);
            spawns.Add(new MonsterSpawn(point, pick.Monster));
            budget -= Math.Max(1, pick.Monster.Threat);
        }

        return spawns;
    }

    /// <summary>
    /// Taking the boss room's other doors away can cut off rooms that were only reached through it,
    /// or leave them reachable only by an extra door that may turn out secret. Links each such group
    /// back with a plain door to a reachable neighbor (never the boss room): an extra door already
    /// there becomes a plain one. Failing that, it gives back one of the removed doors.
    /// </summary>
    private static void ReconnectAround(
        List<GridPosition> cells,
        List<(GridPosition A, GridPosition B)> links,
        HashSet<(GridPosition, GridPosition)> loops,
        GridPosition start,
        GridPosition bossRoom,
        List<(GridPosition A, GridPosition B)> removed)
    {
        while (true)
        {
            Dictionary<GridPosition, int> reached = Distances(links.Where(link => !loops.Contains(link)).ToList(), start);
            List<GridPosition> cutOff = cells.Where(cell => !reached.ContainsKey(cell)).OrderBy(cell => cell.Y).ThenBy(cell => cell.X).ToList();
            if (cutOff.Count == 0)
            {
                return;
            }

            (GridPosition A, GridPosition B)? bridge = cutOff
                .SelectMany(cell => cell.OrthogonalNeighbors().Select(neighbor => (A: neighbor, B: cell)))
                .Where(pair => reached.ContainsKey(pair.A) && pair.A != bossRoom)
                .Select(pair => ((GridPosition A, GridPosition B)?)pair)
                .FirstOrDefault()
                ?? removed.Where(link => cutOff.Contains(link.A) || cutOff.Contains(link.B))
                    .Select(link => ((GridPosition A, GridPosition B)?)link)
                    .FirstOrDefault();
            if (bridge is not { } link)
            {
                return;
            }

            if (Linked(links, link.A, link.B))
            {
                loops.Remove((link.A, link.B));
                loops.Remove((link.B, link.A));
            }
            else
            {
                links.Add(link);
            }
        }
    }

    private static bool InMap(GridPosition cell) => cell.X is >= 0 and < MapSize && cell.Y is >= 0 and < MapSize;

    private static bool Linked(List<(GridPosition A, GridPosition B)> links, GridPosition a, GridPosition b) =>
        links.Contains((a, b)) || links.Contains((b, a));
}
