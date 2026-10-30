using Woad.Core.Content;
using Woad.Core.Dice;
using Woad.Core.Events;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// Disease (Nekrot, for now). A monster that carries one tries to give it to a hero it hits whenever a 6
/// shows among its dice; the hero rolls a d6 to resist (Nekrot: a 6; with Thieves' Oil, 3 or more), and
/// a Medicine Bag in hand gives them a second roll. With Nekrot, every heart they lose becomes a Black
/// Heart, and both stay with them, run after run, until cured.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>A hero loses hearts (any way at all); with Nekrot, each one lost turns black.</summary>
    private void LoseHearts(Hero hero, int amount, List<GameEvent> events)
    {
        int before = hero.Hearts;
        hero.Hearts = Math.Max(0, hero.Hearts - amount);
        events.Add(new Damaged(hero.Id, amount, hero.Hearts));
        if (hero.Conditions.Has(Condition.Nekrot) && before > hero.Hearts)
        {
            Necrosis(hero.Conditions, hero.Id, hero.MaxHearts - hero.Hearts, before - hero.Hearts, hero.Id, events);
        }
    }

    /// <summary>A disease tries to take hold of a hero: they roll to resist it, twice with a Medicine Bag in hand.</summary>
    private void Infect(Hero hero, Infection infection, int byId, List<GameEvent> events)
    {
        if (!hero.IsInPlay || hero.Conditions.Has(infection.Disease))
        {
            return;
        }

        int needed = hero.ThievesOilRounds > 0 ? Math.Min(infection.Resist, GameRules.OiledResist) : infection.Resist;
        List<int> faces = [RollOneDie()];
        if (faces[0] < needed && hero.HoldsMedicineBag)
        {
            faces.Add(RollOneDie());
        }

        bool resisted = faces.Any(face => face >= needed);
        events.Add(new InfectionRolled(hero.Id, infection.Disease, faces, needed, resisted));
        if (!resisted)
        {
            AddCondition(hero.Conditions, hero.Id, infection.Disease, 1, byId, events);
        }
    }

    /// <summary>The d6 at or under which grave dust gets into a hero's lungs (then they roll to resist its disease).</summary>
    public const int GraveDustBreath = 3;

    /// <summary>
    /// Grave dust puffs off a monster as it loses a heart: each hero beside it rolls a d6, and on 3 or under
    /// its disease tries to take them. Destroyed, it bursts: the dust reaches 2 tiles, and a Poison and a
    /// Necro die strike every hero and living monster there first (not the undead).
    /// </summary>
    private void GraveDust(Room room, Monster monster, bool destroyed, List<GameEvent> events)
    {
        int reach = destroyed ? 2 : 1;
        List<Hero> near = [.. State.ActiveHeroesIn(room).Where(hero => monster.NearestTile(hero.Position).ChebyshevDistanceTo(hero.Position) <= reach)];
        if (near.Count == 0 && !destroyed)
        {
            return;
        }

        // Living monsters caught in the burst take its dice too; the undead shrug it off.
        List<Monster> living = destroyed
            ? [.. room.Monsters.Where(other => other != monster && other.Definition.Undead is null && other.NearestTile(monster.Position).ChebyshevDistanceTo(monster.Position) <= reach)]
            : [];
        events.Add(new GraveDustBurst(monster.Id, room.Id, monster.Position, destroyed));
        if (near.Count + living.Count > 0 && destroyed)
        {
            (int[] faces, DieType[] types) = RollTypedDice([(DieType.Poison, 1), (DieType.Necro, 1)]);
            var roll = new DiceRoll(faces);
            Dictionary<DieType, int> sixes = TypedSixes(roll, types);
            events.Add(new AttackRolled(monster.Id, roll, 0, roll.Total, [.. near.Select(hero => hero.Id), .. living.Select(other => other.Id)]) { DieTypes = types });
            foreach (Hero hero in near.Where(hero => hero.IsActive))
            {
                if (DamageHero(room, hero, roll.Total, $"Burst by a {monster.Definition.Name}", monster.Id, events) > 0 && hero.IsInPlay)
                {
                    LayTypedDice(room, hero, sixes, monster.Id, monster.Position, events);
                }
            }

            foreach (Monster other in living.Where(other => room.FindMonster(other.Id) is not null))
            {
                if (DamageMonster(null, room, other, roll.Total, DamageSource.Monster, events) > 0 && room.FindMonster(other.Id) is not null)
                {
                    LayTypedDice(room, other, sixes, monster.Id, monster.Position, null, events);
                }
            }
        }

        if (monster.Definition.Infects is not { } infection)
        {
            return;
        }

        foreach (Hero hero in near.Where(hero => hero.IsActive))
        {
            if (RollOneDie() <= GraveDustBreath)
            {
                Infect(hero, infection, monster.Id, events);
            }
        }
    }
}
