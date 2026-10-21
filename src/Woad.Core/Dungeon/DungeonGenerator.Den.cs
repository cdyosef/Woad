using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Randomness;
using Woad.Core.Rules;

namespace Woad.Core.Dungeon;

public static partial class DungeonGenerator
{
    /// <summary>
    /// A den, drawn with its way in at the bottom: pits all round, a one-tile bridge from the doorway to
    /// the main platform with its keeper ('O') in the middle, and another bridge on to a little platform
    /// holding the grand chest. Turned to face whichever side its door is on.
    /// </summary>
    private static readonly string[] DenRows =
    [
        "~~~~~~~~~~~",
        "~~~~...~~~~",
        "~~~~.C.~~~~",
        "~~~~...~~~~",
        "~~~~~.~~~~~",
        "~~~~~.~~~~~",
        "~~.......~~",
        "~~.......~~",
        "~~...O...~~",
        "~~.......~~",
        "~~.......~~",
        "~~~~~.~~~~~",
        "~~~~~.~~~~~",
    ];

    /// <summary>
    /// The den's layout with its keeper, its way in on <paramref name="doorSide"/>. Public for the
    /// debug screens.
    /// </summary>
    public static RoomTemplate DenLayout(MonsterDefinition keeper, Direction doorSide)
    {
        RoomTemplate drawn = RoomTemplate.Parse(DenRows, glyph => glyph == 'O' ? keeper : null);
        for (int turns = 0; turns < 4; turns++)
        {
            RoomTemplate turned = drawn.Transformed(turns, mirrored: false);
            if (turned.Layout[turned.Layout.MiddleOf(doorSide)] == TileKind.Floor)
            {
                return turned;
            }
        }

        throw new InvalidOperationException("The den's way in should face every side in some turn.");
    }

    /// <summary>
    /// Adds the den to its floor: a free map cell beside one ordinary room, linked by a plain door and
    /// no other. It goes over a room on the floor below when it can, so its pits lead somewhere.
    /// </summary>
    private static void PlanDen(List<FloorPlan> plans, int floor, IRandomSource random)
    {
        if (floor > plans.Count)
        {
            return;
        }

        FloorPlan plan = plans[floor - 1];
        FloorPlan? next = floor < plans.Count ? plans[floor] : null;
        List<(GridPosition Host, GridPosition Cell)> spots = plan.Cells
            .Where(host => plan.Roles[host] is RoomRole.Combat or RoomRole.Treasure or RoomRole.Stairs)
            .SelectMany(host => host.OrthogonalNeighbors().Where(cell => InMap(cell) && !plan.Cells.Contains(cell)).Select(cell => (host, cell)))
            .ToList();
        List<(GridPosition Host, GridPosition Cell)> overRooms = spots
            .Where(spot => next is not null && next.Cells.Contains(spot.Cell) && next.Roles[spot.Cell] is not (RoomRole.Boss or RoomRole.Secret))
            .ToList();
        if ((overRooms.Count > 0 ? overRooms : spots) is not { Count: > 0 } choices)
        {
            return;
        }

        (GridPosition hostCell, GridPosition denCell) = random.Pick(choices);
        plan.Cells.Add(denCell);
        plan.Links.Add((hostCell, denCell));
        plan.Roles[denCell] = RoomRole.Den;
    }

    /// <summary>The den: its keeper alone on the platform, and a grand chest under a hard lock beyond it.</summary>
    private static RoomSetup BuildDen(int floor, GridPosition cell, List<(GridPosition A, GridPosition B)> links, ThemeDefinition theme, ContentCatalog content, IRandomSource random)
    {
        Direction side = Enum.GetValues<Direction>().First(direction => Linked(links, cell, cell.Step(direction)));
        RoomTemplate template = DenLayout(content.GetMonster(theme.Den!.Monster), side);
        (template, Dictionary<Direction, GridPosition> doorTiles) = PlaceDoors(template, [side], random);
        GridPosition chest = template.TilesOf(TileKind.Chest).Single();
        var features = new RoomFeatures
        {
            LockedChests = new Dictionary<GridPosition, int> { [chest] = GameRules.GrandChestLockDifficulty(floor) },
            GrandChests = [chest],
        };
        var doors = new Dictionary<Direction, string> { [side] = RoomId(floor, cell.Step(side)) };
        return new RoomSetup(RoomId(floor, cell), template, doors, floor, cell, RoomRole.Den, Trap: false, features, doorTiles);
    }
}
