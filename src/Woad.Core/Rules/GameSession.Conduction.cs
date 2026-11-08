using Woad.Core.Dice;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// Elektrik through water: an attack with Elektrik dice on someone standing in water (shallow or deep) runs on through
/// that body of water, the same roll striking every other creature in it within 8 tiles, friend or foe, each defending
/// as usual (its Elektrik sixes stunning on a hit). The one who made the attack is spared.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>How far through the water an Elektrik attack runs.</summary>
    public const int ConductReach = 8;

    private void ConductThroughWater(Room room, GridPosition at, int total, DiceRoll roll, IReadOnlyList<DieType> types, IReadOnlyCollection<int> struck,
        Hero? heroAttacker, Monster? monsterAttacker, List<GameEvent> events)
    {
        if (!types.Contains(DieType.Elektrik) || !room.Layout.Contains(at) || !InWater(room, at))
        {
            return;
        }

        // The body of water: the water tiles joined to the struck one, within reach of it.
        var water = new HashSet<GridPosition> { at };
        var frontier = new Queue<GridPosition>([at]);
        while (frontier.TryDequeue(out GridPosition tile))
        {
            foreach (GridPosition next in tile.OrthogonalNeighbors())
            {
                if (room.Layout.Contains(next) && InWater(room, next) && next.ChebyshevDistanceTo(at) <= ConductReach && water.Add(next))
                {
                    frontier.Enqueue(next);
                }
            }
        }

        int attackerId = heroAttacker?.Id ?? monsterAttacker?.Id ?? 0;
        List<Hero> heroes = [.. State.HeroesIn(room).Where(hero => hero.IsInPlay && hero.Id != attackerId && !struck.Contains(hero.Id) && water.Contains(hero.Position))];
        List<Monster> monsters = [.. room.Monsters.Where(monster => monster.Id != attackerId && !struck.Contains(monster.Id) && monster.Tiles.Any(water.Contains))];
        if (heroes.Count == 0 && monsters.Count == 0)
        {
            return;
        }

        events.Add(new CurrentConducted(room.Id, at, [.. heroes.Select(hero => hero.Id), .. monsters.Select(monster => monster.Id)]));
        Dictionary<DieType, int> sixes = TypedSixes(roll, types);
        foreach (Hero hero in heroes.Where(hero => hero.IsInPlay))
        {
            string bane = monsterAttacker is not null ? MonsterBane(monsterAttacker) : "Electrocuted";
            if (DamageHero(room, hero, total, bane, attackerId, events, ranged: true) > 0 && hero.IsInPlay)
            {
                LayTypedDice(room, hero, sixes, attackerId, at, events);
            }
        }

        foreach (Monster monster in monsters.Where(monster => room.FindMonster(monster.Id) is not null))
        {
            if (DamageMonster(heroAttacker, room, monster, total, DamageSource.Weapon, events, ranged: true) > 0 && room.FindMonster(monster.Id) is not null)
            {
                LayTypedDice(room, monster, sixes, attackerId, at, heroAttacker, events);
            }
        }
    }
}
