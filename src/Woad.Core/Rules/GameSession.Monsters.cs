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

            MoveMerchants(room, events);
            foreach (Monster monster in room.Monsters.OrderBy(monster => monster.Id).ToList())
            {
                // One struck down earlier in the phase (by another's swing, say) is gone.
                if (room.FindMonster(monster.Id) is null)
                {
                    continue;
                }

                TakeMonsterTurn(room, monster, events);
                if (State.Phase != RunPhase.InProgress)
                {
                    return;
                }
            }
        }

        events.Add(new EnemyPhaseEnded(State.Round));
    }

    /// <summary>A monster's turn; a stun on it wears off at the end (if it still stands).</summary>
    private void TakeMonsterTurn(Room room, Monster monster, List<GameEvent> events)
    {
        monster.Stun.WearsOffThisTurn = monster.Stun.IsStunned;
        bool burning = monster.FireDice > 0;
        MonsterDefinition own = monster.Definition;
        (int wendLost, int rumbleLost, _) = monster.Stun.Lost(own.OdHandDice > 0, own.WendDice, own.AttackDice, own.DefenseDice);
        monster.MovesThisTurn = Math.Max(0, own.Move - 2 * wendLost);
        if (monster.Slowed)
        {
            monster.MovesThisTurn /= 2;
            monster.Slowed = false;
        }
        monster.AttackDiceThisTurn = Math.Max(0, own.AttackDice - rumbleLost);
        _actingMonsterId = monster.Id;
        try
        {
            ActMonsterTurn(room, monster, events);
        }
        finally
        {
            _actingMonsterId = null;
        }

        if (monster.Stun.WearsOffThisTurn && room.FindMonster(monster.Id) is not null)
        {
            monster.Stun.Clear();
            events.Add(new StunWoreOff(monster.Id));
        }

        // A troll knits its wounds back together, unless fire got at it.
        if (own.Regenerates && !burning && room.FindMonster(monster.Id) is not null && !monster.Collapsed && monster.Hearts < monster.MaxHearts - monster.Conditions[Condition.BlackHeart])
        {
            monster.Hearts++;
            events.Add(new MonsterRegenerated(monster.Id, monster.Hearts));
        }

        ConditionsAtTurnEnd(room, monster, events);
        if (State.Phase == RunPhase.InProgress)
        {
            BurnFuses(monster.Id, heroTurn: false, events);
        }
    }

    private void ActMonsterTurn(Room room, Monster monster, List<GameEvent> events)
    {
        if (!BurnAtTurnStart(room, monster, events) || State.Phase != RunPhase.InProgress)
        {
            return;
        }

        if (monster.Collapsed)
        {
            monster.Collapsed = false;
            monster.Hearts = 1;
            events.Add(new MonsterReassembled(monster.Id, monster.Hearts));
            return;
        }

        if (monster.FrozenTurns > 0)
        {
            monster.FrozenTurns--;
            events.Add(new MonsterTurnSkipped(monster.Id));
            return;
        }

        if (!ConditionsAtTurnStart(room, monster, events))
        {
            return;
        }

        // Buried: it digs, and that's its turn.
        if (DigOutMonster(monster, events))
        {
            return;
        }

        // Confused: it lashes out at whoever's beside it and staggers off, and that's its turn.
        if (monster.Conditions.Has(Condition.Confused))
        {
            Stagger(room, monster, events);
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

        // Knocked flat: getting up takes 2 of its moves.
        if (monster.Prone)
        {
            monster.Prone = false;
            monster.MovesThisTurn = Math.Max(0, monster.MovesThisTurn - GameRules.GetUpCost);
            events.Add(new GotUp(monster.Id));
        }

        monster.TurnsTaken++;
        MonsterDefinition definition = monster.Definition;
        RaiseDead(room, monster, events);

        // A thief with loot runs from everyone, and does nothing else.
        if (definition.Thief && monster.HasLoot)
        {
            IReadOnlyList<GridPosition> flight = PlanFlight(room, monster, heroes);
            if (flight.Count > 0)
            {
                monster.Position = flight[^1];
                events.Add(new MonsterMoved(monster.Id, flight));
            }

            return;
        }

        if (definition.Sweep is { Bonus: false } sweep && monster.TurnsTaken % sweep.Every == 0)
        {
            List<Hero> swept = standing.Where(hero => hero.Position.ChebyshevDistanceTo(monster.NearestTile(hero.Position)) <= sweep.Radius && !(Diving(room, hero) && hero.Position.ChebyshevDistanceTo(monster.NearestTile(hero.Position)) > 1)).ToList();
            if (swept.Count > 0)
            {
                // One swing with every Attack die, which each hero in reach defends against.
                RollMonsterAttack(room, monster, monster.AttackDiceThisTurn, definition.Weapon, swept, events);
                return;
            }
        }

        FlingChain(room, monster, events);
        if (State.Phase != RunPhase.InProgress)
        {
            return;
        }

        standing = [.. State.ActiveHeroesIn(room)];
        bool cornered = definition.KeepsDistance && heroes.Any(hero => hero.Position.ManhattanDistanceTo(monster.NearestTile(hero.Position)) == 1);
        if (!cornered && !definition.Erratic)
        {
            SurgeWithPowerDice(room, monster, heroes, events);
        }

        IReadOnlyList<GridPosition> path = cornered ? PlanRetreat(room, monster, heroes) : PlanAdvance(room, monster, heroes);
        if (path.Count > 0)
        {
            monster.Position = path[^1];
            events.Add(new MonsterMoved(monster.Id, path));
            if (monster.FireDice > 0 && path.Any(tile => InWater(room, tile)))
            {
                monster.FireDice = 0;
                events.Add(new FireOut(monster.Id, FireOutCause.Water));
            }

            if (definition.Engulfs)
            {
                Engulf(room, monster, path, events);
            }
        }

        // Its Od Hand action: a steal from a hero beside it. A thief that got something runs next turn instead of fighting.
        if (TrySteal(room, monster, State.HeroesIn(room).ToList(), events) && definition.Thief && monster.HasLoot)
        {
            return;
        }

        if (ChooseTarget(room, monster, monster.Position, standing) is { } target)
        {
            MonsterAttacks(room, monster, target, events);
        }
        else if (ChooseTarget(room, monster, monster.Position, keeling) is { } keeler)
        {
            StrikeKeeler(room, monster, keeler, events);
        }
        else
        {
            MonsterAttacksMerchant(room, monster, events);
        }

        // A spin on top of it all, at everyone near.
        if (definition.Sweep is { Bonus: true } spin && monster.TurnsTaken % spin.Every == 0 && room.FindMonster(monster.Id) is not null && State.Phase == RunPhase.InProgress)
        {
            List<Hero> spun = [.. State.ActiveHeroesIn(room).Where(hero => hero.Position.ChebyshevDistanceTo(monster.NearestTile(hero.Position)) <= spin.Radius && !(Diving(room, hero) && hero.Position.ChebyshevDistanceTo(monster.NearestTile(hero.Position)) > 1))];
            if (spun.Count > 0)
            {
                RollMonsterAttack(room, monster, monster.AttackDiceThisTurn, definition.Weapon, spun, events);
            }
        }
    }

    /// <summary>Of the heroes attackable from a tile, the one with the fewest hearts (then lowest seat).</summary>
    private static Hero? ChooseTarget(Room room, Monster monster, GridPosition from, IEnumerable<Hero> heroes) =>
        heroes
            // Blind, it can only find those right beside it.
            .Where(hero => !IsBlind(monster.Conditions) || FootprintAt(monster, from).Any(tile => hero.Position.ChebyshevDistanceTo(tile) <= 1))
            .Where(hero => ArcReaches(monster, from, hero) || FootprintAt(monster, from).Any(tile => Targeting.CanAttack(room.Layout, monster.Definition.AttackPattern, monster.Definition.AttackRange, tile, hero.Position)))
            // Someone diving in deep water can only be reached up close.
            .Where(hero => !Diving(room, hero) || FootprintAt(monster, from).Any(tile => hero.Position.ChebyshevDistanceTo(tile) <= 1))
            // A mugger goes for the gold.
            .OrderByDescending(hero => monster.Definition.Mugger ? hero.Gold : 0)
            .ThenBy(hero => hero.Hearts)
            .ThenBy(hero => hero.Seat)
            .FirstOrDefault();

    /// <summary>
    /// Monsters can't pass through heroes, other monsters, walls or chests; only flyers cross pits, and
    /// only flyers and swimmers deep water. A water-only swimmer never leaves the water.
    /// </summary>
    private bool CanMonsterEnter(Room room, Monster monster, GridPosition position) =>
        FootprintAt(monster, position).All(tile => room.Layout.Contains(tile)
            && CanMonsterStand(room.Layout[tile], monster)
            // Monsters keep off burning floor, unless fire can't hurt them.
            && (monster.Definition.FireImmune || !room.IsBurning(tile) || monster.Occupies(tile))
            && (room.MonsterAt(tile) is not { } other || other == monster)
            && room.MerchantAt(tile) is null
            && !State.HeroesIn(room).Any(hero => hero.Position == tile));

    /// <summary>The tiles a monster would fill standing at <paramref name="at"/> (just it, unless it's huge).</summary>
    private static IEnumerable<GridPosition> FootprintAt(Monster monster, GridPosition at) =>
        monster.Definition.Size <= 1 ? [at] : Woarld.Skirmish.Footprint(monster.Definition, at);

    private static bool CanMonsterStand(TileKind tile, Monster monster) =>
        monster.Definition.WaterOnly
            ? tile is TileKind.ShallowWater or TileKind.DeepWater
            : tile.IsWalkable(monster.Flies) || (monster.Definition.Swimmer && tile == TileKind.DeepWater);

    /// <summary>
    /// A monster that can't reach a tile to attack from this turn rolls banked Power Dice into its
    /// move, one at a time, while that could still get it there.
    /// </summary>
    private void SurgeWithPowerDice(Room room, Monster monster, IReadOnlyList<Hero> heroes, List<GameEvent> events)
    {
        if (monster.Power.Banked == 0 || ChooseTarget(room, monster, monster.Position, heroes) is not null)
        {
            return;
        }

        PathSearch search = PathSearch.Run(monster.Position, position => CanMonsterEnter(room, monster, position), stepCost: position => StepCost(room, monster, position));
        int? steps = search.Distances.Where(entry => ChooseTarget(room, monster, entry.Key, heroes) is not null).Select(entry => (int?)entry.Value).Min();
        if (steps is not { } needed || needed <= monster.MovesThisTurn || needed > monster.MovesThisTurn + GameRules.PowerDieFace * monster.Power.Banked)
        {
            return;
        }

        var faces = new List<int>();
        while (monster.Power.Banked > 0 && monster.MovesThisTurn < needed)
        {
            monster.Power.Banked--;
            int face = RollOneDie();
            faces.Add(face);
            monster.MovesThisTurn += face;
        }

        events.Add(new MonsterSurged(monster.Id, faces, monster.MovesThisTurn));
        events.Add(new PowerDiceSpent(monster.Id, faces.Count, monster.Power.Banked));
    }

    /// <summary>Steps toward the nearest tile it can attack from, stopping as soon as it can attack.</summary>
    private IReadOnlyList<GridPosition> PlanAdvance(Room room, Monster monster, IReadOnlyList<Hero> heroes)
    {
        var path = new List<GridPosition>();
        GridPosition current = monster.Position;

        // Rough ground and shallow water cost 2 of its moves (flyers pay 1 everywhere).
        for (int movesLeft = monster.MovesThisTurn; movesLeft > 0;)
        {
            if (ChooseTarget(room, monster, current, heroes) is not null)
            {
                break;
            }

            GridPosition? next = (monster.Definition.Erratic && _random.Behavior.CoinFlip()) || IsBlind(monster.Conditions)
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

    private static int StepCost(Room room, Monster monster, GridPosition tile) =>
        (monster.Definition.Swimmer && InWater(room, tile) ? 1 : room.Layout[tile].MoveCost(monster.Flies))
        + (!monster.Flies && room.PlantsAgainst(tile, heroes: false) ? 1 : 0);

    private GridPosition? RandomStep(Room room, Monster monster, GridPosition from)
    {
        List<GridPosition> options = from.OrthogonalNeighbors()
            .Where(position => room.Layout.Contains(position) && CanMonsterEnter(room, monster, position))
            .ToList();
        return options.Count == 0 ? null : _random.Behavior.Pick(options);
    }

    /// <summary>Backs off to a tile it can still attack from, as far from the heroes as its move allows.</summary>
    private IReadOnlyList<GridPosition> PlanRetreat(Room room, Monster monster, IReadOnlyList<Hero> heroes)
    {
        PathSearch search = PathSearch.Run(monster.Position, position => CanMonsterEnter(room, monster, position), monster.MovesThisTurn, stepCost: position => StepCost(room, monster, position));

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
