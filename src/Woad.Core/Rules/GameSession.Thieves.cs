using Woad.Core.Content;
using Woad.Core.Dice;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Randomness;

namespace Woad.Core.Rules;

/// <summary>
/// Monsters that steal (the Petty Thief, the Mugger Fly). A monster with Od Hand dice may steal from a
/// hero beside it once a turn (a stun takes that away): its dice against the hero's own Od Hand dice
/// and tools, every die over their total a hit. Hits take, in turn, gold, an item from the backpack,
/// then gold again (<see cref="GoldPerTheft"/> a hit), never more than one item, and only gold for a
/// gold-only thief. A thief with loot runs from everyone; a mugger goes after the richest hero, and
/// each of its hits takes a gold piece too. Whatever a monster stole drops where it falls.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Gold a hit of a theft takes.</summary>
    public const int GoldPerTheft = 2;

    /// <summary>Gold each hit of a mugger's attack takes.</summary>
    public const int GoldPerMugging = 1;

    /// <summary>Whether a monster could steal anything from the hero.</summary>
    private static bool WorthStealing(Monster monster, Hero hero) =>
        hero.Gold > 0 || (!monster.Definition.GoldOnly && hero.Backpack.Count > 0);

    /// <summary>A monster's Od Hand action: steal from the richest hero beside it, if any. True if it tried.</summary>
    private bool TrySteal(Room room, Monster monster, IEnumerable<Hero> heroes, List<GameEvent> events)
    {
        if (monster.Definition.OdHandDice < 1 || monster.Stun.IsStunned)
        {
            return false;
        }

        Hero? mark = heroes
            .Where(hero => hero.Position.ManhattanDistanceTo(monster.Position) == 1 && WorthStealing(monster, hero))
            .OrderByDescending(hero => hero.Gold)
            .ThenBy(hero => hero.Seat)
            .FirstOrDefault();
        if (mark is null)
        {
            return false;
        }

        (DiceRoll roll, int powerDice) = RollMonsterDice(monster.Definition.OdHandDice);
        events.Add(new AttackRolled(monster.Id, roll, 0, roll.Total, [mark.Id], powerDice));
        Stats stats = mark.EffectiveStats;
        DiceRoll hold = _dice.Roll(Math.Max(0, stats.OdHandDice));
        int tools = Math.Max(0, stats.Tools);
        events.Add(new DefenseRolled(mark.Id, hold, tools, hold.Total + tools));

        int hits = roll.Faces.Count(face => face > hold.Total + tools);
        int gold = 0;
        CardInstance? taken = null;
        for (int hit = 1; hit <= hits; hit++)
        {
            if (hit == 2 && !monster.Definition.GoldOnly && mark.Backpack.Count > 0)
            {
                taken = mark.Backpack[_random.Loot.NextInt(0, mark.Backpack.Count)];
                mark.Remove(taken);
                monster.StolenCards.Add(taken);
                continue;
            }

            int purse = Math.Min(GoldPerTheft, mark.Gold);
            mark.Gold -= purse;
            gold += purse;
        }

        monster.StolenGold += gold;
        events.Add(new MonsterStole(monster.Id, mark.Id, hits, gold, taken?.Id));
        return true;
    }

    /// <summary>A mugger's hit takes gold too.</summary>
    private static void Mug(Monster monster, Hero hero, List<GameEvent> events)
    {
        int gold = Math.Min(GoldPerMugging, hero.Gold);
        if (gold > 0)
        {
            hero.Gold -= gold;
            monster.StolenGold += gold;
            events.Add(new Mugged(monster.Id, hero.Id, gold));
        }
    }

    /// <summary>A thief with loot runs: to the tile it can reach that's farthest from every hero.</summary>
    private IReadOnlyList<GridPosition> PlanFlight(Room room, Monster monster, IReadOnlyList<Hero> heroes)
    {
        PathSearch search = PathSearch.Run(monster.Position, position => CanMonsterEnter(room, monster, position), monster.MovesThisTurn, stepCost: position => StepCost(room, monster, position));
        GridPosition best = search.Distances.Keys
            .OrderByDescending(tile => heroes.Min(hero => hero.Position.ManhattanDistanceTo(tile)))
            .ThenBy(tile => search.Distances[tile])
            .ThenBy(tile => tile.Y)
            .ThenBy(tile => tile.X)
            .First();
        return best == monster.Position ? [] : search.PathTo(best);
    }

    /// <summary>A monster that falls drops everything it stole, gold and cards, where it fell.</summary>
    private static void DropStolen(Room room, Monster monster, GridPosition at, List<GameEvent> events)
    {
        if (monster.StolenGold > 0)
        {
            room.AddGold(at, monster.StolenGold);
            events.Add(new GoldAppeared(room.Id, at, monster.StolenGold));
            monster.StolenGold = 0;
        }

        foreach (CardInstance card in monster.StolenCards)
        {
            room.AddLoot(at, [card]);
            events.Add(new LootDropped(room.Id, at, card.Id));
        }

        monster.StolenCards.Clear();
    }
}
