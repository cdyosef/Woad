using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Randomness;
using Woad.Core.Rules;

namespace Woad.Core.Dungeon;

/// <summary>
/// A tower above the dungeon (the Emerald Keep's): stairs up from an ordinary room on floor 1 to Level 1,
/// a floor of rooms laid out like those below; its farthest room's stairs climb to Level 2, the same again;
/// and Level 2's to the roof, Level 3: one great open room, its edge crenellated, green stone pillars with
/// open air between them, where anyone knocked off falls to the courtyard. Generated from a stream of its
/// own, so the dungeon below comes out as it would without it.
/// </summary>
public static partial class DungeonGenerator
{
    private const ulong TowerStream = 6;

    /// <summary>The tower's levels of rooms, under its roof.</summary>
    public const int TowerLevels = 2;

    /// <summary>The roof's width and height: a whole floor's worth of open stone.</summary>
    public const int RoofSize = 31;

    private static void BuildTower(ThemeDefinition theme, ContentCatalog content, IReadOnlyList<ThemeRoom> layouts, IReadOnlyList<HeroSetup> heroes, ulong seed,
        List<RoomSetup> rooms, List<PassageSetup> passages)
    {
        var random = new Pcg32(seed, TowerStream);

        // The stairs up: in an ordinary room on floor 1 (not the entrance, nor where the stairs go down).
        List<RoomSetup> hosts = [.. rooms.Where(room => room.Floor == 1 && room.Level == 0 && room.Role is RoomRole.Combat or RoomRole.Treasure
            && !room.Template.TilesOf(TileKind.StairsDown).Any())];
        if (hosts.Count == 0)
        {
            return;
        }

        RoomSetup host = random.Pick(hosts);
        Replace(rooms, host with { Template = host.Template.WithTile(ReachableStairsSpot(host), TileKind.StairsUp) });
        string below = host.Id;
        GridPosition cell = host.MapPosition;
        for (int level = 1; level <= TowerLevels; level++)
        {
            int floor = theme.Floors + level;
            FloorPlan plan = PlanFloor(floor, lastFloor: false, cell, random, passages);
            var built = new List<RoomSetup>();
            BuildFloor(plan, [], theme, content, layouts, GameRules.PartyStrength(heroes), random, built);

            // Stairs the other way round: the arrival's lead back down, the far room's on up.
            built = [.. built.Select(room => room with { Template = TurnStairs(room.Template), Level = level })];
            RoomSetup arrival = built.Single(room => room.Role == RoomRole.Arrival);
            RoomSetup stairs = built.Single(room => room.Role == RoomRole.Stairs);
            built[built.IndexOf(arrival)] = arrival with { StairsDownTo = below };
            Replace(rooms, rooms.First(room => room.Id == below) with { StairsUpTo = arrival.Id });
            rooms.AddRange(built);
            below = stairs.Id;
            cell = stairs.MapPosition;
        }

        // The roof, over the last level's stairs.
        string courtyard = rooms.First(room => room.Floor == 1 && room.Level == 0 && room.Role == RoomRole.Arrival).Id;
        var roof = new RoomSetup(RoofId, Roof(random, heroes.Count, content), new Dictionary<Direction, string>(), Floor: theme.Floors + TowerLevels + 1, MapPosition: cell, Role: RoomRole.Combat)
        {
            Level = TowerLevels + 1,
            StairsDownTo = below,
            FallsTo = courtyard,
        };
        Replace(rooms, rooms.First(room => room.Id == below) with { StairsUpTo = RoofId });
        rooms.Add(roof);
    }

    /// <summary>
    /// Where the stairs up go in a floor-1 room: open floor near the middle that can be walked to from its
    /// doors (not behind a dividing wall), clear of the doorways.
    /// </summary>
    private static GridPosition ReachableStairsSpot(RoomSetup room)
    {
        RoomTemplate template = room.Template;
        IReadOnlyDictionary<Direction, GridPosition> doors = room.DoorTiles ?? new Dictionary<Direction, GridPosition>();
        HashSet<GridPosition> reserved = [.. DoorAreas(template, doors), .. template.SpawnPoints];
        bool Open(GridPosition tile) => template.Layout.Contains(tile) && template.Layout[tile].IsWalkable();
        GridPosition from = doors.Values.Select(door => (GridPosition?)door).FirstOrDefault() ?? template.Layout.Center;
        IReadOnlyCollection<GridPosition> reached = PathSearch.Run(from, Open).Distances.Keys.ToList();
        GridPosition center = template.Layout.Center;
        return template.TilesOf(TileKind.Floor)
            .Where(tile => reached.Contains(tile) && !reserved.Contains(tile))
            .OrderBy(tile => tile.ManhattanDistanceTo(center))
            .ThenBy(tile => tile.Y)
            .ThenBy(tile => tile.X)
            .First();
    }

    /// <summary>The roof's room id.</summary>
    public const string RoofId = "roof";

    /// <summary>
    /// The roof: open stone, its stairs down in the middle, and its edge crenellated: a green stone pillar on
    /// every other tile (and the corners), open air between them.
    /// </summary>
    private static RoomTemplate Roof(IRandomSource random, int heroes, ContentCatalog content)
    {
        RoomTemplate template = RoomBuilder.Build(RoofSize, RoofSize, RoomShape.Plain, RoomRole.Combat, random);
        RoomLayout layout = template.Layout;
        foreach (GridPosition tile in layout.Positions)
        {
            bool edge = tile.X == 0 || tile.Y == 0 || tile.X == RoofSize - 1 || tile.Y == RoofSize - 1;
            bool corner = (tile.X == 0 || tile.X == RoofSize - 1) && (tile.Y == 0 || tile.Y == RoofSize - 1);
            TileKind kind = !edge ? TileKind.Floor
                : corner || (tile.X + tile.Y) % 2 == 0 ? TileKind.Wall
                : TileKind.Pit;
            template = template.WithTile(tile, kind);
        }

        // Baron Herkullos on his beetle, across the roof from the stairs, his Grasshopper Knights around him:
        // two, and one more for every two heroes.
        GridPosition baron = layout.Center with { Y = layout.Center.Y - 8 };
        var spawns = new List<MonsterSpawn> { new(baron, content.GetMonster("baron_herkullos")) };
        int knights = 2 + heroes / 2;
        (int Dx, int Dy)[] posts = [(-4, 1), (5, 1), (-3, 4), (4, 4), (-6, -2), (7, -2), (0, 5), (1, -4)];
        spawns.AddRange(posts.Take(knights).Select(post => new MonsterSpawn(new GridPosition(baron.X + post.Dx, baron.Y + post.Dy), content.GetMonster("grasshopper_knight"))));
        return template.WithTile(layout.Center, TileKind.StairsDown).WithSpawnPoints([]).WithMonsters(spawns);
    }

    /// <summary>A tower room's stairs, turned round: up for down, down for up.</summary>
    private static RoomTemplate TurnStairs(RoomTemplate template)
    {
        List<GridPosition> up = [.. template.TilesOf(TileKind.StairsUp)], down = [.. template.TilesOf(TileKind.StairsDown)];
        foreach (GridPosition tile in up)
        {
            template = template.WithTile(tile, TileKind.StairsDown);
        }

        foreach (GridPosition tile in down)
        {
            template = template.WithTile(tile, TileKind.StairsUp);
        }

        return template;
    }

    private static void Replace(List<RoomSetup> rooms, RoomSetup room) => rooms[rooms.FindIndex(other => other.Id == room.Id)] = room;
}
