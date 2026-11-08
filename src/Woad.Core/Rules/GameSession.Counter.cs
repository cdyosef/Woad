using Woad.Core.Content;
using Woad.Core.Dice;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Randomness;

namespace Woad.Core.Rules;

/// <summary>
/// Counterattacks. The Emerald Knight hits back at whoever attacks him, hit or miss: a die, his weapon and his
/// axe's Life die (see <see cref="MonsterDefinition.Counter"/>). A hero wielding the Emerald Axe hits back too,
/// once a round, when a Power Die comes up on their defense roll: a die, the axe's value and its Life dice, and
/// those Power Dice rolled in. Neither can while stunned, or frozen past half their Wend; confused, the blow
/// lands on whatever's beside them, friend or foe. The Life dice's sixes grow plants on a hit or a miss.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Heroes waiting to hit back (with the Emerald Axe) once the attack on them is done: who, at whom, and the Power Dice off their defense.</summary>
    private readonly List<(Hero Hero, int AttackerId, int[] Power)> _counters = [];

    /// <summary>The waiting counterattacks, now that the attacks on those heroes are done.</summary>
    private void FlushCounters(List<GameEvent> events)
    {
        while (_counters.Count > 0)
        {
            (Hero hero, int attackerId, int[] power) = _counters[0];
            _counters.RemoveAt(0);
            HeroCounters(State.RoomOf(hero), hero, attackerId, power, events);
        }
    }

    /// <summary>Stunned, or frozen past half their Wend dice: no hitting back.</summary>
    private static bool CannotCounter(StunState stun, Conditions conditions, int wendDice) =>
        stun.IsStunned || WendFrozen(conditions) * 2 > Math.Max(1, wendDice);

    /// <summary>Everything beside a figure on <paramref name="tiles"/>, hero or monster, by id (for a confused blow).</summary>
    private List<int> Beside(Room room, IReadOnlyCollection<GridPosition> tiles, int self) =>
    [
        .. room.Monsters.Where(other => other.Id != self && other.Tiles.Any(tile => tiles.Any(own => own.ChebyshevDistanceTo(tile) == 1))).Select(other => other.Id),
        .. State.ActiveHeroesIn(room).Where(hero => hero.Id != self && tiles.Any(own => own.ChebyshevDistanceTo(hero.Position) == 1)).Select(hero => hero.Id),
    ];

    /// <summary>The monsters a hero's attack was at that hit back (the Emerald Knight), each at them if they're in reach.</summary>
    private void MonstersCounter(Room room, Hero attacker, IEnumerable<int> targetIds, List<GameEvent> events)
    {
        foreach (int id in targetIds.Distinct().ToList())
        {
            if (room.FindMonster(id) is not { Definition.Counter: { } counter } monster || monster.Collapsed || !attacker.IsInPlay
                || CannotCounter(monster.Stun, monster.Conditions, monster.Definition.WendDice) || State.Phase != RunPhase.InProgress)
            {
                continue;
            }

            int? victim = monster.Conditions.Has(Condition.Confused) ? PickBeside(room, monster.Tiles.ToList(), monster.Id)
                : monster.NearestTile(attacker.Position).ChebyshevDistanceTo(attacker.Position) <= 1 ? attacker.Id
                : null;
            if (victim is not int victimId)
            {
                continue;
            }

            events.Add(new Countered(monster.Id, victimId));
            (DiceRoll roll, IReadOnlyList<DieType> plainTypes, int powerDice) = RollMonsterDice(monster, counter.Dice, plain: true);
            (int[] typedFaces, DieType[] typed) = RollTypedDice(counter.DieType is { } type ? [(type, counter.TypedDice)] : []);
            foreach (int face in typedFaces)
            {
                roll = roll.With(face);
            }

            List<DieType> types = [.. plainTypes, .. Enumerable.Repeat(DieType.Plain, roll.Faces.Count - typedFaces.Length - plainTypes.Count), .. typed];
            int bonus = monster.Definition.Weapon - AttackPenalty(monster.Conditions);
            CounterBlow(room, monster.Id, monster.NearestTile(attacker.Position), roll, types, bonus, powerDice, victimId, null, MonsterBane(monster), events);
        }
    }

    /// <summary>A hero's Emerald Axe (or anything that counters) hits back at the monster that attacked them, the Power Dice off their defense rolled in.</summary>
    private void HeroCounters(Room room, Hero hero, int attackerId, IReadOnlyList<int> powerFaces, List<GameEvent> events)
    {
        if (powerFaces.Count == 0 || !hero.IsActive || hero.MainWeapon is not { Definition.Counters: true } axe || hero.CounteredRound == State.Round
            || CannotCounter(hero.Stun, hero.Conditions, hero.EffectiveStats.MoveDice) || State.Phase != RunPhase.InProgress)
        {
            return;
        }

        int? victim = hero.Conditions.Has(Condition.Confused) ? PickBeside(room, [hero.Position], hero.Id)
            : room.FindMonster(attackerId) is { } attacker && attacker.NearestTile(hero.Position).ChebyshevDistanceTo(hero.Position) <= 1 ? attackerId
            : null;
        if (victim is not int victimId)
        {
            return;
        }

        hero.CounteredRound = State.Round;
        events.Add(new Countered(hero.Id, victimId));
        DiceRoll roll = _dice.Roll(1);
        CardDefinition weapon = axe.Definition;
        (int[] typedFaces, DieType[] typed) = RollTypedDice(weapon.DieType is { } type && weapon.Modifiers.WeaponDice > 0 ? [(type, weapon.Modifiers.WeaponDice)] : []);
        foreach (int face in typedFaces.Concat(powerFaces))
        {
            roll = roll.With(face);
        }

        List<DieType> types = [DieType.Plain, .. typed, .. powerFaces.Select(_ => DieType.Plain)];
        int bonus = Math.Max(0, hero.EffectiveStats.Weapon) - AttackPenalty(hero.Conditions);
        CounterBlow(room, hero.Id, hero.Position, roll, types, bonus, powerFaces.Count, victimId, hero, $"Cut down by {hero.Name}'s counterattack", events);
    }

    /// <summary>A random one of whatever's beside the tiles (a confused blow), or null with nothing there.</summary>
    private int? PickBeside(Room room, List<GridPosition> tiles, int self) =>
        Beside(room, tiles, self) is { Count: > 0 } beside ? _random.Behavior.Pick(beside.Order().ToList()) : null;

    /// <summary>The counterblow lands: defended as any attack; its typed dice's sixes (the Life dice) take hold on a hit or a miss.</summary>
    private void CounterBlow(Room room, int byId, GridPosition from, DiceRoll roll, IReadOnlyList<DieType> types, int bonus, int powerDice, int victimId, Hero? byHero, string bane, List<GameEvent> events)
    {
        int total = roll.Total + bonus;
        events.Add(new AttackRolled(byId, roll, bonus, total, [victimId], powerDice) { DieTypes = types });
        Dictionary<DieType, int> sixes = TypedSixes(roll, types);
        if (room.FindMonster(victimId) is { } monster)
        {
            DamageMonster(byHero, room, monster, total, byHero is null ? DamageSource.Monster : DamageSource.Weapon, events);
            if (room.FindMonster(monster.Id) is not null)
            {
                LayTypedDice(room, monster, sixes, byId, from, byHero, events);
            }
        }
        else if (State.Heroes.FirstOrDefault(hero => hero.Id == victimId) is { } hero && hero.IsInPlay)
        {
            DamageHero(room, hero, total, bane, byId, events);
            if (hero.IsInPlay)
            {
                LayTypedDice(room, hero, sixes, byId, from, events);
            }
        }
    }
}
