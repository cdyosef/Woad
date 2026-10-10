using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Randomness;
using Woad.Core.Rules;

namespace Woad.Core.Dungeon;

/// <summary>
/// Builds a run's dungeon from its seed. For each floor a random walk lays out 6-8 rooms on a 5x5
/// map. The arrival room is where the walk starts, the farthest room gets the stairs (or the boss
/// on the last floor), and a dead end becomes the treasure room. Each room gets a randomly turned
/// layout from the theme and monsters bought with a difficulty budget that grows with floor depth
/// and party size. The same seed and party size always build the same dungeon.
/// </summary>
public static class DungeonGenerator
{
    public const int MapSize = 5;
    public const int MinRooms = 6;
    public const int MaxRooms = 8;

    private const ulong Stream = 4;

    public static RunSetup CreateRun(ContentCatalog content, string themeId, IReadOnlyList<HeroSetup> heroes, ulong seed)
    {
        ThemeDefinition theme = content.GetTheme(themeId);
        IReadOnlyList<ThemeRoom> layouts = content.RoomsFor(themeId);
        foreach (RoomRole role in Enum.GetValues<RoomRole>())
        {
            bool needed = role != RoomRole.Stairs || theme.Floors > 1;
            if (needed && !layouts.Any(layout => layout.Roles.Contains(role)))
            {
                throw new InvalidOperationException($"Theme '{themeId}' has no {role} room layouts.");
            }
        }

        if (theme.Boss is null)
        {
            throw new InvalidOperationException($"Theme '{themeId}' has no boss.");
        }

        var random = new Pcg32(seed, Stream);
        var rooms = new List<RoomSetup>();
        for (int floor = 1; floor <= theme.Floors; floor++)
        {
            rooms.AddRange(GenerateFloor(floor, floor == theme.Floors, theme, content, layouts, heroes.Count, random));
        }

        return new RunSetup
        {
            Seed = seed,
            ThemeId = themeId,
            Heroes = heroes,
            Rooms = rooms,
            StartRoomId = rooms.First(room => room.Floor == 1 && room.Role == RoomRole.Arrival).Id,
        };
    }

    /// <summary>A room's id: its floor and map cell, e.g. "f2-31".</summary>
    public static string RoomId(int floor, GridPosition cell) => $"f{floor}-{cell.X}{cell.Y}";

    private static List<RoomSetup> GenerateFloor(
        int floor,
        bool lastFloor,
        ThemeDefinition theme,
        ContentCatalog content,
        IReadOnlyList<ThemeRoom> layouts,
        int partySize,
        IRandomSource random)
    {
        // Lay out cells with a random walk; every new cell is linked to the one it grew from.
        int roomCount = random.NextInt(MinRooms, MaxRooms + 1);
        var start = new GridPosition(random.NextInt(0, MapSize), random.NextInt(0, MapSize));
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
        foreach (GridPosition cell in cells.ToList())
        {
            foreach (Direction side in new[] { Direction.East, Direction.South })
            {
                GridPosition neighbor = cell.Step(side);
                if (cells.Contains(neighbor) && !Linked(links, cell, neighbor) && random.NextInt(0, 4) == 0)
                {
                    links.Add((cell, neighbor));
                }
            }
        }

        Dictionary<GridPosition, RoomRole> roles = AssignRoles(cells, links, start, lastFloor, random);
        GridPosition? trap = null;
        List<GridPosition> combat = cells.Where(cell => roles[cell] == RoomRole.Combat).ToList();
        if (floor >= 2 && combat.Count > 0)
        {
            trap = random.Pick(combat);
        }

        var rooms = new List<RoomSetup>();
        foreach (GridPosition cell in cells)
        {
            RoomRole role = roles[cell];
            List<ThemeRoom> candidates = layouts.Where(layout => layout.Roles.Contains(role)).ToList();
            RoomTemplate template = random.Pick(candidates).Template.Transformed(random.NextInt(0, 4), random.CoinFlip());
            bool isTrap = cell == trap;
            IEnumerable<MonsterSpawn> monsters = PlaceMonsters(template, role, floor, partySize, isTrap, theme, content, random);

            var doors = new Dictionary<Direction, string>();
            foreach (Direction side in Enum.GetValues<Direction>())
            {
                GridPosition neighbor = cell.Step(side);
                if (Linked(links, cell, neighbor))
                {
                    doors[side] = RoomId(floor, neighbor);
                }
            }

            rooms.Add(new RoomSetup(
                RoomId(floor, cell),
                template.WithMonsters(template.Monsters.Concat(monsters)),
                doors,
                floor,
                cell,
                role,
                isTrap));
        }

        return rooms;
    }

    private static Dictionary<GridPosition, RoomRole> AssignRoles(
        List<GridPosition> cells,
        List<(GridPosition A, GridPosition B)> links,
        GridPosition start,
        bool lastFloor,
        IRandomSource random)
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

        GridPosition farthest = cells.Where(cell => cell != start).OrderByDescending(cell => distance[cell]).First();
        var roles = cells.ToDictionary(cell => cell, _ => RoomRole.Combat);
        roles[start] = RoomRole.Arrival;
        roles[farthest] = lastFloor ? RoomRole.Boss : RoomRole.Stairs;

        List<GridPosition> others = cells.Where(cell => cell != start && cell != farthest).ToList();
        List<GridPosition> deadEnds = others.Where(cell => cell.OrthogonalNeighbors().Count(next => Linked(links, cell, next)) == 1).ToList();
        roles[random.Pick(deadEnds.Count > 0 ? deadEnds : others)] = RoomRole.Treasure;
        return roles;
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

        int budget = role switch
        {
            RoomRole.Arrival => 0,
            RoomRole.Treasure => 1,
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

    private static bool InMap(GridPosition cell) => cell.X is >= 0 and < MapSize && cell.Y is >= 0 and < MapSize;

    private static bool Linked(List<(GridPosition A, GridPosition B)> links, GridPosition a, GridPosition b) =>
        links.Contains((a, b)) || links.Contains((b, a));
}
