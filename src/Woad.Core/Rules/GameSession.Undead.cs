using Woad.Core.Content;
using Woad.Core.Dice;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// The undead. Lesser undead (Skeletons, the Drowned, zombies risen from monsters) are destroyed
/// outright by Holy Water, bones and all; greater undead (Ghouls, heroes risen as zombies) take 5
/// Radiate dice from it. A Ghoul's miasma attacks every living creature that starts its turn beside it
/// with a Poison die.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Holy Water thrown at the undead within its range: lesser undead are destroyed outright, greater ones take its Radiate dice.</summary>
    private void Sanctify(Hero hero, Room room, CardEffect effect, Monster target, List<GameEvent> events)
    {
        if (target.Definition.Undead == UndeadKind.Lesser)
        {
            events.Add(new Sanctified(target.Id, hero.Id, Destroyed: true));
            DefeatMonster(hero, room, target, DamageSource.Spell, events, crushed: true);
            ClearRoomIfEmpty(room, events);
            return;
        }

        events.Add(new Sanctified(target.Id, hero.Id, Destroyed: false));
        RollHeroAttack(hero, 0, 0, [target.Id], DamageSource.Spell, events, typed: [(DieType.Radiate, effect.Amount)]);
    }

    /// <summary>Why Holy Water can't go at <paramref name="tile"/>: drunk on your own tile, or thrown at the undead in range.</summary>
    private string? CheckSanctify(Hero hero, Room room, CardEffect effect, GridPosition? tile, out IReadOnlyList<Monster> targets)
    {
        targets = [];
        if (tile is not { } aim || aim == hero.Position)
        {
            return effect.Cures is { } cures && hero.Conditions.Has(cures) ? null : "There's nothing for it to cure. Throw it at the undead.";
        }

        if (room.MonsterAt(aim) is not { } monster || monster.Definition.Undead is null)
        {
            return "Holy Water is drunk, or thrown at the undead.";
        }

        if (!Targeting.InRangeAndSight(room.Layout, hero.Position, aim, effect.Range)
            || (IsBlind(hero.Conditions) && aim.ChebyshevDistanceTo(hero.Position) > 1))
        {
            return "That's out of range.";
        }

        if (!SpellReaches(hero, room, monster))
        {
            return "It's under the water, out of reach.";
        }

        targets = [monster];
        return null;
    }

    /// <summary>Ghouls beside a hero starting their turn breathe their miasma on them: a Poison die each.</summary>
    private void Miasma(Room room, Hero hero, List<GameEvent> events)
    {
        foreach (Monster ghoul in MiasmaBeside(room, hero.Position, hero.Id))
        {
            if (!hero.IsActive)
            {
                return;
            }

            DiceRoll roll = _dice.Roll(1);
            events.Add(new AttackRolled(ghoul.Id, roll, 0, roll.Total, [hero.Id]) { DieTypes = [DieType.Poison], Miasma = true });
            if (DamageHero(room, hero, roll.Total, $"a {ghoul.Definition.Name}'s miasma", ghoul.Id, events) > 0 && hero.IsInPlay)
            {
                LayTypedDice(room, hero, TypedSixes(roll, [DieType.Poison]), ghoul.Id, ghoul.NearestTile(hero.Position), events);
            }
        }
    }

    /// <summary>The same for a living monster starting its turn beside a Ghoul.</summary>
    private void Miasma(Room room, Monster monster, List<GameEvent> events)
    {
        if (monster.Definition.Undead is not null)
        {
            return;
        }

        foreach (Monster ghoul in MiasmaBeside(room, monster.Position, monster.Id))
        {
            if (room.FindMonster(monster.Id) is null || monster.Collapsed)
            {
                return;
            }

            DiceRoll roll = _dice.Roll(1);
            events.Add(new AttackRolled(ghoul.Id, roll, 0, roll.Total, [monster.Id]) { DieTypes = [DieType.Poison], Miasma = true });
            if (DamageMonster(null, room, monster, roll.Total, DamageSource.Spell, events) > 0 && room.FindMonster(monster.Id) is not null)
            {
                LayTypedDice(room, monster, TypedSixes(roll, [DieType.Poison]), ghoul.Id, ghoul.NearestTile(monster.Position), null, events);
            }
        }
    }

    private static IReadOnlyList<Monster> MiasmaBeside(Room room, GridPosition position, int exceptId) =>
        [.. room.Monsters.Where(other => other.Id != exceptId && other.Definition.Miasma && !other.Collapsed && other.NearestTile(position).ChebyshevDistanceTo(position) == 1)];
}
