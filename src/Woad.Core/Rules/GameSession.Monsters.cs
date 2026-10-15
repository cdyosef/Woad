using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Randomness;

namespace Woad.Core.Rules;

public sealed partial class GameSession
{
    /// <summary>Monsters act in every room holding at least one hero; other rooms stay paused.</summary>
    private void RunEnemyPhase(List<GameEvent> events)
    {
        events.Add(new EnemyPhaseStarted(State.Round));

        foreach (Room room in State.Rooms.Values.OrderBy(room => room.Id, StringComparer.Ordinal).ToList())
        {
            if (!State.HeroesIn(room).Any())
            {
                continue;
            }

            foreach (Monster monster in room.Monsters.OrderBy(monster => monster.Id).ToList())
            {
                TakeMonsterTurn(room, monster, events);
                if (State.Phase != RunPhase.InProgress)
                {
                    return;
                }
            }
        }

        events.Add(new EnemyPhaseEnded(State.Round));
    }

    private void TakeMonsterTurn(Room room, Monster monster, List<GameEvent> events)
    {
        if (monster.Collapsed)
        {
            monster.Collapsed = false;
            monster.Hearts = 1;
            events.Add(new MonsterReassembled(monster.Id, monster.Hearts));
            return;
        }

        if (monster.StunnedTurns > 0)
        {
            monster.StunnedTurns--;
            events.Add(new MonsterTurnSkipped(monster.Id));
            return;
        }

        // Monsters go after heroes still standing; those Keeling Over only when nobody standing is in reach.
        List<Hero> standing = State.ActiveHeroesIn(room).ToList();
        List<Hero> keeling = State.HeroesIn(room).Where(hero => hero.IsKeelingOver).ToList();
        List<Hero> heroes = standing.Count > 0 ? standing : keeling;
        if (heroes.Count == 0)
        {
            return;
        }

        monster.TurnsTaken++;
        MonsterDefinition definition = monster.Definition;

        if (definition.Sweep is { } sweep && monster.TurnsTaken % sweep.Every == 0)
        {
            List<Hero> swept = standing.Where(hero => hero.Position.ChebyshevDistanceTo(monster.Position) <= sweep.Radius).ToList();
            if (swept.Count > 0)
            {
                // One swing with every Attack die, which each hero in reach defends against.
                RollMonsterAttack(room, monster, definition.AttackDice, definition.Weapon, swept, events);
                return;
            }
        }

        bool cornered = definition.KeepsDistance && heroes.Any(hero => hero.Position.ManhattanDistanceTo(monster.Position) == 1);
        IReadOnlyList<GridPosition> path = cornered ? PlanRetreat(room, monster, heroes) : PlanAdvance(room, monster, heroes);
        if (path.Count > 0)
        {
            monster.Position = path[^1];
            events.Add(new MonsterMoved(monster.Id, path));
        }

        if (ChooseTarget(room, monster, monster.Position, standing) is { } target)
        {
            MonsterAttacks(room, monster, target, events);
        }
        else if (ChooseTarget(room, monster, monster.Position, keeling) is { } keeler)
        {
            StrikeKeeler(room, monster, keeler, events);
        }
    }

    /// <summary>Of the heroes attackable from a tile, the one with the fewest hearts (then lowest seat).</summary>
    private static Hero? ChooseTarget(Room room, Monster monster, GridPosition from, IEnumerable<Hero> heroes) =>
        heroes
            .Where(hero => Targeting.CanAttack(room.Layout, monster.Definition.AttackPattern, monster.Definition.AttackRange, from, hero.Position))
            .OrderBy(hero => hero.Hearts)
            .ThenBy(hero => hero.Seat)
            .FirstOrDefault();

    /// <summary>Monsters can't pass through heroes, other monsters, walls or chests; only flyers cross pits.</summary>
    private bool CanMonsterEnter(Room room, Monster monster, GridPosition position) =>
        room.Layout[position].IsWalkable(monster.Definition.Flying)
        && (room.MonsterAt(position) is not { } other || other == monster)
        && !State.HeroesIn(room).Any(hero => hero.Position == position);

    /// <summary>Steps toward the nearest tile it can attack from, stopping as soon as it can attack.</summary>
    private IReadOnlyList<GridPosition> PlanAdvance(Room room, Monster monster, IReadOnlyList<Hero> heroes)
    {
        var path = new List<GridPosition>();
        GridPosition current = monster.Position;

        // Rough ground and shallow water cost 2 of its moves (flyers pay 1 everywhere).
        for (int movesLeft = monster.Definition.Move; movesLeft > 0;)
        {
            if (ChooseTarget(room, monster, current, heroes) is not null)
            {
                break;
            }

            GridPosition? next = monster.Definition.Erratic && _random.Behavior.CoinFlip()
                ? RandomStep(room, monster, current)
                : StepToward(room, monster, current, heroes);
            int cost = next is { } step ? StepCost(room, monster, step) : 0;
            if (next is null || cost > movesLeft)
            {
                break;
            }

            path.Add(next.Value);
            current = next.Value;
            movesLeft -= cost;
        }

        return path;
    }

    private GridPosition? StepToward(Room room, Monster monster, GridPosition from, IReadOnlyList<Hero> heroes)
    {
        PathSearch search = PathSearch.Run(from, position => CanMonsterEnter(room, monster, position), stepCost: position => StepCost(room, monster, position));

        var attackSpot = search.Distances
            .Select(entry => (Tile: entry.Key, Steps: entry.Value, Target: ChooseTarget(room, monster, entry.Key, heroes)))
            .Where(spot => spot.Target is not null)
            .OrderBy(spot => spot.Steps)
            .ThenBy(spot => spot.Target!.Hearts)
            .ThenBy(spot => spot.Target!.Seat)
            .ThenBy(spot => spot.Tile.Y)
            .ThenBy(spot => spot.Tile.X)
            .FirstOrDefault();

        // With no tile to attack from (e.g. heroes walled off), just close the distance.
        GridPosition goal = attackSpot.Target is not null
            ? attackSpot.Tile
            : search.Distances.Keys
                .OrderBy(tile => heroes.Min(hero => hero.Position.ManhattanDistanceTo(tile)))
                .ThenBy(tile => search.Distances[tile])
                .ThenBy(tile => tile.Y)
                .ThenBy(tile => tile.X)
                .First();

        return goal == from ? null : search.PathTo(goal)[0];
    }

    private static int StepCost(Room room, Monster monster, GridPosition tile) => room.Layout[tile].MoveCost(monster.Definition.Flying);

    private GridPosition? RandomStep(Room room, Monster monster, GridPosition from)
    {
        List<GridPosition> options = from.OrthogonalNeighbors()
            .Where(position => position.IsInRoom && CanMonsterEnter(room, monster, position))
            .ToList();
        return options.Count == 0 ? null : _random.Behavior.Pick(options);
    }

    /// <summary>Backs off to a tile it can still attack from, as far from the heroes as its move allows.</summary>
    private IReadOnlyList<GridPosition> PlanRetreat(Room room, Monster monster, IReadOnlyList<Hero> heroes)
    {
        PathSearch search = PathSearch.Run(monster.Position, position => CanMonsterEnter(room, monster, position), monster.Definition.Move, stepCost: position => StepCost(room, monster, position));

        GridPosition best = search.Distances.Keys
            .OrderBy(tile => ChooseTarget(room, monster, tile, heroes) is null ? 1 : 0)
            .ThenByDescending(tile => heroes.Min(hero => hero.Position.ManhattanDistanceTo(tile)))
            .ThenBy(tile => search.Distances[tile])
            .ThenBy(tile => tile.Y)
            .ThenBy(tile => tile.X)
            .First();

        return best == monster.Position ? [] : search.PathTo(best);
    }
}
