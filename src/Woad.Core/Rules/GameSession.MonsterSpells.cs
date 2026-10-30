using Woad.Core.Content;
using Woad.Core.Dice;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// Monsters with a spell (a Rotten Mummy's Swirly Eyes) cast it when no blow of theirs reaches anyone:
/// at the weakest hero in its range with a clear line, its dice against their defense as an attack. On
/// a hit its typed dice take hold, and Swirly Eyes drags the hero a tile toward the caster.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Casts the monster's spell at a hero in range, if it has one and anyone's there. Returns whether it cast.</summary>
    private bool CastAtHero(Room room, Monster monster, IReadOnlyList<Hero> heroes, List<GameEvent> events)
    {
        if (monster.Definition.Spell is not { } spellId)
        {
            return false;
        }

        SpellDefinition spell = Content.GetSpell(spellId);
        Hero? target = heroes
            .Where(hero => hero.IsActive && monster.Tiles.Any(tile => Targeting.CanAttack(room.Layout, AttackPattern.Ranged, spell.Range, tile, hero.Position)))
            .OrderBy(hero => hero.Hearts)
            .ThenBy(hero => hero.Seat)
            .FirstOrDefault();
        if (target is null)
        {
            return false;
        }

        events.Add(new MonsterCast(monster.Id, spell.Id, target.Id));
        (int[] faces, DieType[] types) = RollTypedDice([(spell.DieType, spell.Dice)]);
        var roll = new DiceRoll(faces);
        int bonus = -AttackPenalty(monster.Conditions);
        events.Add(new AttackRolled(monster.Id, roll, bonus, roll.Total + bonus, [target.Id]) { DieTypes = types });
        if (DamageHero(room, target, roll.Total + bonus, MonsterBane(monster), monster.Id, events, ranged: true) == 0 || !target.IsInPlay)
        {
            return true;
        }

        LayTypedDice(room, target, TypedSixes(roll, types), monster.Id, monster.NearestTile(target.Position), events);
        if (spell.Swirl && target.IsActive && SwirlToward(room, target, monster) is { } to)
        {
            PushHero(null, room, target, to, events);
        }

        return true;
    }

    /// <summary>The free tile beside the hero nearest the monster, if it's nearer than where they stand.</summary>
    private GridPosition? SwirlToward(Room room, Hero hero, Monster monster)
    {
        int now = monster.NearestTile(hero.Position).ChebyshevDistanceTo(hero.Position);
        return hero.Position.SurroundingTiles()
            .Where(tile => room.Layout.Contains(tile) && room.Layout[tile].IsWalkable() && !IsOccupied(room, tile))
            .Where(tile => monster.NearestTile(tile).ChebyshevDistanceTo(tile) < now)
            .OrderBy(tile => monster.NearestTile(tile).ChebyshevDistanceTo(tile))
            .ThenBy(tile => monster.NearestTile(tile).ManhattanDistanceTo(tile))
            .ThenBy(tile => tile.Y)
            .ThenBy(tile => tile.X)
            .Select(tile => (GridPosition?)tile)
            .FirstOrDefault();
    }
}
