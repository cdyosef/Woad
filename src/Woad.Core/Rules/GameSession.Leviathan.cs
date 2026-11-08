using Woad.Core.Content;
using Woad.Core.Dice;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// Leviathan Jr.'s ways in the water. Elektrik dice that attack it are absorbed: they don't count against it, and are
/// stored, all released at the start of its turn in a shockwave on everyone else in its body of water (out of the
/// water, they fizzle out). Out of the water it can only crawl a tile a turn back toward it. With nobody in reach of its
/// claws, it looses one of its barrages a turn (Bolt Ball, Mass Swirly Eyes) or its tongue; a barrage, once loosed,
/// comes back on a die roll at the start of its turns. With moves left after all that, it dives back into the deep.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>
    /// Elektrik dice in an attack on a monster that absorbs them: taken off the total against it and stored. Returns the
    /// total left, and the typed sixes without the Elektrik ones.
    /// </summary>
    private (int Total, Dictionary<DieType, int> Sixes) AbsorbElektrik(Monster monster, DiceRoll roll, IReadOnlyList<DieType> types, int total, Dictionary<DieType, int> sixes, List<GameEvent> events)
    {
        if (!monster.Definition.AbsorbsElektrik)
        {
            return (total, sixes);
        }

        List<int> elektrik = [.. Enumerable.Range(0, Math.Min(roll.Faces.Count, types.Count)).Where(i => types[i] == DieType.Elektrik).Select(i => roll.Faces[i])];
        if (elektrik.Count == 0)
        {
            return (total, sixes);
        }

        monster.StoredElektrik += elektrik.Count;
        events.Add(new ElektrikAbsorbed(monster.Id, elektrik.Count, monster.StoredElektrik));
        Dictionary<DieType, int> left = new(sixes);
        left.Remove(DieType.Elektrik);
        return (total - elektrik.Sum(), left);
    }

    /// <summary>At the start of its turn: its spent barrages may come back, and its stored Elektrik goes off.</summary>
    private void LeviathanTurnStart(Room room, Monster monster, List<GameEvent> events)
    {
        foreach (MonsterBarrage barrage in monster.Definition.Barrages.Where(barrage => monster.SpentBarrages.Contains(barrage.Id)))
        {
            int face = RollOneDie();
            if (face >= barrage.Recharge)
            {
                monster.SpentBarrages.Remove(barrage.Id);
                events.Add(new BarrageRecharged(monster.Id, barrage.Name, face));
            }
        }

        if (monster.StoredElektrik <= 0)
        {
            return;
        }

        int stored = monster.StoredElektrik;
        monster.StoredElektrik = 0;
        if (!InWater(room, monster.Position))
        {
            events.Add(new ShockwaveFizzled(monster.Id, stored));
            return;
        }

        // Everyone else in its body of water, however far.
        HashSet<GridPosition> water = BodyOfWater(room, monster.Position, int.MaxValue);
        List<Hero> heroes = [.. State.HeroesIn(room).Where(hero => hero.IsInPlay && water.Contains(hero.Position))];
        List<Monster> monsters = [.. room.Monsters.Where(other => other != monster && other.Tiles.Any(water.Contains))];
        (int[] faces, DieType[] types) = RollTypedDice([(DieType.Elektrik, stored)]);
        var roll = new DiceRoll(faces);
        events.Add(new ShockwaveReleased(monster.Id, roll, [.. heroes.Select(hero => hero.Id), .. monsters.Select(other => other.Id)]));
        Dictionary<DieType, int> sixes = TypedSixes(roll, types);
        foreach (Hero hero in heroes.Where(hero => hero.IsInPlay))
        {
            if (DamageHero(room, hero, roll.Total, MonsterBane(monster), monster.Id, events, ranged: true) > 0 && hero.IsInPlay)
            {
                LayTypedDice(room, hero, sixes, monster.Id, monster.Position, events);
            }
        }

        foreach (Monster other in monsters.Where(other => room.FindMonster(other.Id) is not null))
        {
            DamageMonster(null, room, other, roll.Total, DamageSource.Weapon, events, ranged: true);
        }
    }

    /// <summary>The water tiles joined to <paramref name="at"/>'s, within <paramref name="reach"/> of it.</summary>
    private static HashSet<GridPosition> BodyOfWater(Room room, GridPosition at, int reach)
    {
        var water = new HashSet<GridPosition> { at };
        var frontier = new Queue<GridPosition>([at]);
        while (frontier.TryDequeue(out GridPosition tile))
        {
            foreach (GridPosition next in tile.OrthogonalNeighbors())
            {
                if (room.Layout.Contains(next) && InWater(room, next) && next.ChebyshevDistanceTo(at) <= reach && water.Add(next))
                {
                    frontier.Enqueue(next);
                }
            }
        }

        return water;
    }

    /// <summary>
    /// A water creature stranded on land crawls a tile a turn back toward the water (its land move). True if it was on
    /// land: that's its move.
    /// </summary>
    private bool CrawlToWater(Room room, Monster monster, List<GameEvent> events)
    {
        if (!monster.Definition.WaterOnly || monster.Definition.LandMove < 0 || InWater(room, monster.Position))
        {
            return false;
        }

        PathSearch search = PathSearch.Run(monster.Position, tile => room.Layout.Contains(tile)
            && (room.Layout[tile].IsWalkable() || InWater(room, tile)) && !IsOccupied(room, tile));
        if (search.Distances.Keys.Where(tile => InWater(room, tile)).OrderBy(tile => search.Distances[tile]).ThenBy(tile => tile.Y).ThenBy(tile => tile.X)
                .Select(tile => (GridPosition?)tile).FirstOrDefault() is { } shore)
        {
            List<GridPosition> path = [.. search.PathTo(shore).Take(monster.Definition.LandMove)];
            if (path.Count > 0)
            {
                monster.Position = path[^1];
                events.Add(new MonsterMoved(monster.Id, path));
            }
        }

        return true;
    }

    /// <summary>
    /// Nobody in reach of its claws: one ranged attack a turn. Bolt Ball first at someone standing in water (its Elektrik
    /// runs on through it), else Mass Swirly Eyes on everyone in sight (luring them in), else Bolt Ball at the weakest,
    /// else its tongue. True if it loosed one.
    /// </summary>
    private bool LooseBarrage(Room room, Monster monster, IReadOnlyList<Hero> standing, List<GameEvent> events)
    {
        MonsterBarrage? Ready(Func<MonsterBarrage, bool> kind) =>
            monster.Definition.Barrages.FirstOrDefault(barrage => kind(barrage) && !monster.SpentBarrages.Contains(barrage.Id));
        List<Hero> InSight(int range) => [.. standing.Where(hero => !Diving(room, hero)
            && Targeting.CanAttack(room.Layout, AttackPattern.Ranged, range, monster.NearestTile(hero.Position), hero.Position))];

        if (!IsBlind(monster.Conditions))
        {
            MonsterBarrage? bolt = Ready(barrage => !barrage.Mass);
            MonsterBarrage? swirl = Ready(barrage => barrage.Mass);
            if (bolt is not null && InSight(bolt.Range).Where(hero => InWater(room, hero.Position)).OrderBy(hero => hero.Hearts).ThenBy(hero => hero.Seat).FirstOrDefault() is { } wet)
            {
                Bolt(room, monster, bolt, wet, events);
                return true;
            }

            if (swirl is not null && InSight(swirl.Range) is { Count: > 0 } lured)
            {
                MassSwirl(room, monster, swirl, lured, events);
                return true;
            }

            if (bolt is not null && InSight(bolt.Range).OrderBy(hero => hero.Hearts).ThenBy(hero => hero.Seat).FirstOrDefault() is { } target)
            {
                Bolt(room, monster, bolt, target, events);
                return true;
            }
        }

        return LashTongue(room, monster, standing, events);
    }

    /// <summary>A single-target barrage (Bolt Ball): its typed dice at one hero; on a hit they take hold, and its Elektrik runs on through water.</summary>
    private void Bolt(Room room, Monster monster, MonsterBarrage barrage, Hero target, List<GameEvent> events)
    {
        monster.SpentBarrages.Add(barrage.Id);
        events.Add(new BarrageLoosed(monster.Id, barrage.Name, [target.Id]));
        (int[] faces, DieType[] types) = RollTypedDice([.. barrage.Dice.Select(die => (die.Type, die.Dice))]);
        var roll = new DiceRoll(faces);
        int total = roll.Total - AttackPenalty(monster.Conditions);
        events.Add(new AttackRolled(monster.Id, roll, total - roll.Total, total, [target.Id]) { DieTypes = types });
        GridPosition at = target.Position;
        if (DamageHero(room, target, total, MonsterBane(monster), monster.Id, events, ranged: true) > 0 && target.IsInPlay)
        {
            LayTypedDice(room, target, TypedSixes(roll, types), monster.Id, monster.NearestTile(target.Position), events);
        }

        if (State.Phase == RunPhase.InProgress)
        {
            ConductThroughWater(room, at, total, roll, types, [target.Id], null, monster, events);
        }
    }

    /// <summary>A barrage on everyone in sight (Mass Swirly Eyes): each defends its dice, and each it hits is drawn a tile toward it.</summary>
    private void MassSwirl(Room room, Monster monster, MonsterBarrage barrage, IReadOnlyList<Hero> targets, List<GameEvent> events)
    {
        monster.SpentBarrages.Add(barrage.Id);
        events.Add(new BarrageLoosed(monster.Id, barrage.Name, [.. targets.Select(hero => hero.Id)]));
        foreach (Hero target in targets.Where(hero => hero.IsActive))
        {
            (int[] faces, DieType[] types) = RollTypedDice([.. barrage.Dice.Select(die => (die.Type, die.Dice))]);
            var roll = new DiceRoll(faces);
            int total = roll.Total - AttackPenalty(monster.Conditions);
            events.Add(new AttackRolled(monster.Id, roll, total - roll.Total, total, [target.Id]) { DieTypes = types });
            if (DamageHero(room, target, total, MonsterBane(monster), monster.Id, events, ranged: true) == 0 || !target.IsInPlay)
            {
                continue;
            }

            LayTypedDice(room, target, TypedSixes(roll, types), monster.Id, monster.NearestTile(target.Position), events);
            if (barrage.Swirl && target.IsActive && SwirlToward(room, target, monster) is { } to)
            {
                PushHero(null, room, target, to, events);
            }
        }
    }

    /// <summary>With moves left at the end of its turn, a diver goes back down into the nearest deep water it can reach.</summary>
    private void DiveBack(Room room, Monster monster, int movesLeft, List<GameEvent> events)
    {
        if (!monster.Definition.DivesAfter || movesLeft <= 0 || room.Layout[monster.Position] == TileKind.DeepWater || room.FindMonster(monster.Id) is null)
        {
            return;
        }

        PathSearch search = PathSearch.Run(monster.Position, tile => room.Layout.Contains(tile) && InWater(room, tile) && !IsOccupied(room, tile), maxSteps: movesLeft);
        if (search.Distances.Keys.Where(tile => room.Layout[tile] == TileKind.DeepWater).OrderBy(tile => search.Distances[tile]).ThenBy(tile => tile.Y).ThenBy(tile => tile.X)
                .Select(tile => (GridPosition?)tile).FirstOrDefault() is { } deep)
        {
            List<GridPosition> path = [.. search.PathTo(deep)];
            if (path.Count > 0)
            {
                monster.Position = path[^1];
                events.Add(new MonsterMoved(monster.Id, path));
                events.Add(new MonsterDove(monster.Id));
            }
        }
    }
}
